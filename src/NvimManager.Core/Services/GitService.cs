using System.Diagnostics;
using System.Text;

namespace NvimManager.Core.Services;

public sealed record GitResult(string StdOut, string StdErr, int ExitCode)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.Join('\n', new[] { StdOut, StdErr }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Thin wrapper around the git CLI used to install / update / query plugins.
/// Clones land inside the lazy root so lazy.nvim can manage them at runtime.
/// </summary>
public sealed class GitService
{
    private static readonly string GitExecutable = ResolveGit();
    private readonly int _timeoutMs;

    public GitService(int timeoutMs = 900_000) => _timeoutMs = timeoutMs;

    public string Executable => GitExecutable;

    public Task<GitResult> RunAsync(string workDir, params string[] args)
        => RunAsync(workDir, (IReadOnlyList<string>)args);

    public Task<GitResult> RunAsync(string workDir, IReadOnlyList<string> args)
    {
        if (!string.IsNullOrEmpty(workDir))
            Directory.CreateDirectory(workDir);

        var psi = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        return Task.Run(async () =>
        {
            try
            {
                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                try
                {
                    await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(_timeoutMs));
                }
                catch (TimeoutException)
                {
                    TryKill(proc);
                    return new GitResult(
                        stdout.ToString().Trim(),
                        $"git timed out after {_timeoutMs} ms and was terminated.",
                        -1);
                }
                return new GitResult(stdout.ToString().Trim(), stderr.ToString().Trim(), proc.ExitCode);
            }
            catch (Exception ex)
            {
                return new GitResult(string.Empty, ex.Message, -1);
            }
        });
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // process already gone — nothing to do
        }
    }

    /// <summary>
    /// Clone owner/repo into destDir (shallow, pinned to branch when given).
    /// A leftover directory without a .git checkout (e.g. the debris of a
    /// previously failed clone) is removed first, or git itself would refuse
    /// to clone into the non-empty path.
    /// </summary>
    public async Task<GitResult> CloneAsync(string repo, string destDir, string? branch = null)
    {
        if (Directory.Exists(destDir) && !LooksInstalled(destDir))
        {
            try { DeleteDirectory(destDir); }
            catch { /* fall through: clone will report the real error */ }
        }

        var args = new List<string> { "clone", "--depth", "1" };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            args.Add("--branch");
            args.Add(branch);
        }
        args.Add($"https://github.com/{repo}.git");
        args.Add(destDir);
        return await RunAsync(Path.GetDirectoryName(destDir) ?? string.Empty, args);
    }

    /// <summary>
    /// Fast-forward updates an existing clone, whatever shape it has:
    ///  - clean tree on a branch with upstream  -> merge --ff-only @{u}
    ///  - detached HEAD / no upstream (typical for shallow installs and
    ///    lazy.nvim lockfile checkouts)         -> reset --hard origin/HEAD
    /// The tree is never touched when it carries local changes.
    /// </summary>
    public async Task<GitResult> UpdateAsync(string dir)
    {
        var status = await RunAsync(dir, "status", "--porcelain");
        if (!status.Success)
            return status;
        if (!string.IsNullOrWhiteSpace(status.StdOut))
            return new GitResult(
                status.StdOut,
                "Checkout has local changes; refusing to update. Commit, stash or revert them first.",
                1);

        var fetch = await FetchAsync(dir);
        if (!fetch.Success)
            return fetch;

        var head = await RunAsync(dir, "rev-parse", "--abbrev-ref", "HEAD");
        string branch = head.Success ? head.StdOut.Trim() : string.Empty;
        if (!string.IsNullOrEmpty(branch) && branch != "HEAD")
        {
            var upstream = await RunAsync(dir, "rev-parse", "--abbrev-ref", $"{branch}@{{u}}");
            if (upstream.Success && !string.IsNullOrWhiteSpace(upstream.StdOut))
                return await RunAsync(dir, "merge", "--ff-only", upstream.StdOut.Trim());
        }

        // Detached HEAD (lazy lockfile checkouts) or branch without upstream:
        // move to the remote default branch tip.
        var originHead = await RunAsync(dir, "rev-parse", "origin/HEAD");
        if (!originHead.Success || string.IsNullOrWhiteSpace(originHead.StdOut))
            return new GitResult(
                fetch.StdOut,
                "Cannot determine the remote default branch (origin/HEAD is missing).\n" + fetch.Combined,
                1);
        return await RunAsync(dir, "reset", "--hard", originHead.StdOut.Trim());
    }

    /// <summary>
    /// Fetches all remote branches, updating refs/remotes/origin/* (including
    /// the origin/HEAD symref target) so update checks see fresh data. Unlike a
    /// plain `fetch origin HEAD` this actually writes the remote-tracking refs.
    /// Clones pinned to a tag (`--branch v1.2.3`) get no origin/HEAD symref from
    /// git itself, so `remote set-head --auto` creates/refreshes it here.
    /// </summary>
    public async Task<GitResult> FetchAsync(string dir)
    {
        // The refspec must be explicit: clones pinned to a tag carry a
        // tag-only fetch refspec, so a bare `fetch origin` would never
        // populate refs/remotes/origin/* and updates could never resolve.
        var fetch = await RunAsync(dir, "fetch", "--prune", "origin",
            "+refs/heads/*:refs/remotes/origin/*");
        if (!fetch.Success)
            return fetch;

        await RunAsync(dir, "remote", "set-head", "origin", "--auto");
        return fetch;
    }

    public async Task<string?> RevParseAsync(string dir, string revision = "HEAD")
    {
        var r = await RunAsync(dir, "rev-parse", revision);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public async Task<string?> DescribeAsync(string dir)
    {
        var r = await RunAsync(dir, "describe", "--tags", "--abbrev=0");
        return r.Success ? r.StdOut.Trim() : null;
    }

    public async Task<InstalledModel?> InspectAsync(string dir)
    {
        if (!Directory.Exists(Path.Combine(dir, ".git"))) return null;
        var head = await RevParseAsync(dir);
        var shortHead = head is { Length: > 7 } ? head[..7] : head;
        var tag = await DescribeAsync(dir);
        return new InstalledModel(dir, head, shortHead, tag);
    }

    public readonly record struct InstalledModel(string Dir, string? Commit, string? ShortCommit, string? Tag);

    public static bool LooksInstalled(string dir)
        => Directory.Exists(Path.Combine(dir, ".git"));

    public static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                // best effort; some files are transiently locked on Windows
            }
        }
        Directory.Delete(path, true);
    }

    private static string ResolveGit()
    {
        const string executable = "git";
        try
        {
            var psi = new ProcessStartInfo(executable, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0) return executable;
            }
        }
        catch
        {
            // fall through
        }

        string[] candidates =
        {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            "/usr/bin/git",
            "/usr/local/bin/git",
            "/opt/homebrew/bin/git",
        };
        return candidates.FirstOrDefault(File.Exists) ?? executable;
    }
}