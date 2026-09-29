using System.Diagnostics;
using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

/// <summary>
/// Integration tests for the real git update flow (shallow clones, detached
/// HEAD checkouts, dirty trees). Skipped silently when no git executable is
/// available, so the suite still passes on git-less machines.
/// </summary>
public sealed class UpdateFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nvmupdate-" + Guid.NewGuid().ToString("N"));
    private readonly GitService _git = new(60_000);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task UpdateAsync_fast_forwards_shallow_branch_clone()
    {
        if (!GitAvailable()) return;

        string origin = MkDir("origin");
        string plugin = MkDir("plugin");
        Git(origin, "init", "-b", "main");
        File.WriteAllText(Path.Combine(origin, "a.txt"), "v1\n");
        Git(origin, "add", ".");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "v1");
        Git(plugin, "clone", "--depth", "1", OriginUrl(origin), plugin);
        string oldSha = Git(origin, "rev-parse", "HEAD");

        File.WriteAllText(Path.Combine(origin, "a.txt"), "v2\n");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-am", "v2");
        string newSha = Git(origin, "rev-parse", "HEAD");
        Assert.NotEqual(oldSha, newSha);

        var result = await _git.UpdateAsync(plugin);
        Assert.True(result.Success, result.Combined);
        Assert.Equal(newSha, Git(plugin, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task UpdateAsync_resets_detached_head_checkout()
    {
        if (!GitAvailable()) return;

        string origin = MkDir("origin");
        string plugin = MkDir("plugin");
        Git(origin, "init", "-b", "main");
        File.WriteAllText(Path.Combine(origin, "a.txt"), "v1\n");
        Git(origin, "add", ".");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "v1");
        Git(plugin, "clone", "--depth", "1", OriginUrl(origin), plugin);
        Git(plugin, "checkout", "--detach", "HEAD");

        File.WriteAllText(Path.Combine(origin, "a.txt"), "v2\n");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-am", "v2");
        string newSha = Git(origin, "rev-parse", "HEAD");

        var result = await _git.UpdateAsync(plugin);
        Assert.True(result.Success, result.Combined);
        Assert.Equal(newSha, Git(plugin, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task UpdateAsync_refuses_dirty_tree()
    {
        if (!GitAvailable()) return;

        string origin = MkDir("origin");
        string plugin = MkDir("plugin");
        Git(origin, "init", "-b", "main");
        File.WriteAllText(Path.Combine(origin, "a.txt"), "v1\n");
        Git(origin, "add", ".");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "v1");
        Git(plugin, "clone", "--depth", "1", OriginUrl(origin), plugin);

        File.WriteAllText(Path.Combine(plugin, "a.txt"), "locally modified\n");

        var result = await _git.UpdateAsync(plugin);
        Assert.False(result.Success);
        Assert.Contains("local changes", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_updates_origin_head_ref()
    {
        if (!GitAvailable()) return;

        string origin = MkDir("origin");
        string plugin = MkDir("plugin");
        Git(origin, "init", "-b", "main");
        File.WriteAllText(Path.Combine(origin, "a.txt"), "v1\n");
        Git(origin, "add", ".");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "v1");
        Git(plugin, "clone", "--depth", "1", OriginUrl(origin), plugin);

        File.WriteAllText(Path.Combine(origin, "a.txt"), "v2\n");
        Git(origin, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-am", "v2");
        string newSha = Git(origin, "rev-parse", "HEAD");

        var fetch = await _git.FetchAsync(plugin);
        Assert.True(fetch.Success, fetch.Combined);

        string? gitDir = GitDirReader.TryResolveGitDir(plugin);
        Assert.NotNull(gitDir);
        // Before the fix, this ref was never written by `fetch origin HEAD`,
        // so "Check for updates" could never see new commits.
        Assert.True(GitDirReader.TryResolveRef(gitDir!, "refs/remotes/origin/HEAD", out var latest));
        Assert.Equal(newSha, latest);
    }

    // ---------- helpers ----------

    private string MkDir(params string[] parts)
    {
        string dir = parts.Aggregate(_root, Path.Combine);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>file:// is required for shallow clones from a local path.</summary>
    private static string OriginUrl(string origin)
        => new Uri(origin).AbsoluteUri;

    private static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit(30_000);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {proc.StandardError.ReadToEnd().Trim()}");
        return stdout;
    }

    private bool GitAvailable()
    {
        try
        {
            Git(MkDir("_probe"), "--version");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
