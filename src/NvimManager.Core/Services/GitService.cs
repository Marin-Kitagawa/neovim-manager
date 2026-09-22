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
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(_timeoutMs));
                return new GitResult(stdout.ToString().Trim(), stderr.ToString().Trim(), proc.ExitCode);
            }
            catch (Exception ex)
            {
                return new GitResult(string.Empty, ex.Message, -1);
            }
        });
    }

    /// <summary>Clone owner/repo into destDir (deep-less, pinned to branch when given).</summary>
    public async Task<GitResult> CloneAsync(string repo, string destDir, string? branch = null)
    {
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

    /// <summary>Fast-forward update an existing clone. Fails if the tree is dirty.</summary>
    public async Task<GitResult> UpdateAsync(string dir)
        => await RunAsync(dir, "pull", "--ff-only");

    public async Task<GitResult> FetchAsync(string dir)
        => await RunAsync(dir, "fetch", "--depth", "1", "origin", "HEAD");

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