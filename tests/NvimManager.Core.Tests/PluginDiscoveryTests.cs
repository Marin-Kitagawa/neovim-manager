using NvimManager.Core.Models;
using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nvmdisc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    // ---------- GitDirReader ----------

    [Fact]
    public void HeadSha_resolves_loose_ref()
    {
        string dir = GitPlugin("plug", remote: null, sha: new string('a', 40));
        Assert.True(GitDirReader.TryGetHeadSha(dir, out var sha));
        Assert.Equal(new string('a', 40), sha);
    }

    [Fact]
    public void HeadSha_detached_returns_sha()
    {
        string dir = MkDir("detached");
        string git = MkDir("detached", ".git");
        File.WriteAllText(Path.Combine(git, "HEAD"), new string('b', 40));
        Assert.True(GitDirReader.TryGetHeadSha(dir, out var sha));
        Assert.Equal(new string('b', 40), sha);
    }

    [Fact]
    public void HeadSha_falls_back_to_packed_refs()
    {
        string dir = MkDir("packed");
        string git = MkDir("packed", ".git");
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(git, "packed-refs"),
            "# pack-refs with: peeled fully-peeled sorted \n" + new string('c', 40) + " refs/heads/main\n");
        Assert.True(GitDirReader.TryGetHeadSha(dir, out var sha));
        Assert.Equal(new string('c', 40), sha);
    }

    [Fact]
    public void HeadSha_missing_git_returns_false()
    {
        string dir = MkDir("nogit");
        Assert.False(GitDirReader.TryGetHeadSha(dir, out _));
        Assert.False(GitDirReader.HasGit(dir));
    }

    [Fact]
    public void Gitfile_pointer_is_followed()
    {
        string dir = MkDir("wt");
        string real = MkDir("wt-real");
        string git = MkDir("wt-real", ".git");
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main");
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), new string('d', 40));
        File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: ../wt-real/.git");
        Assert.True(GitDirReader.TryGetHeadSha(dir, out var sha));
        Assert.Equal(new string('d', 40), sha);
    }

    [Theory]
    [InlineData("https://github.com/acme/foo.nvim.git", "github.com", "acme/foo.nvim")]
    [InlineData("https://github.com/acme/foo.nvim", "github.com", "acme/foo.nvim")]
    [InlineData("git@github.com:acme/foo.nvim.git", "github.com", "acme/foo.nvim")]
    [InlineData("ssh://git@github.com/acme/foo.nvim.git", "github.com", "acme/foo.nvim")]
    [InlineData("https://gitlab.com/group/sub/repo.git", "gitlab.com", "group/sub/repo")]
    public void RemoteUrl_normalizes(string url, string host, string path)
    {
        string dir = GitPlugin("r", remote: url, sha: new string('e', 40));
        Assert.Equal(url, GitDirReader.TryGetRemoteUrl(dir));
        Assert.True(GitDirReader.TryNormalizeRepoUrl(url, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(path, p);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/srv/git/local")]
    [InlineData(@"C:\repos\local")]
    public void RemoteUrl_rejects_non_remote(string? url)
    {
        Assert.False(GitDirReader.TryNormalizeRepoUrl(url, out _, out _));
    }

    [Fact]
    public void ExactTag_found_in_loose_and_packed_refs()
    {
        string sha = new string('f', 40);
        string a = GitPlugin("taga", remote: null, sha: sha, tag: "v1.2.3");
        Assert.Equal("v1.2.3", GitDirReader.TryGetExactTag(a, sha));

        string b = MkDir("tagb");
        string git = MkDir("tagb", ".git");
        File.WriteAllText(Path.Combine(git, "HEAD"), sha);
        File.WriteAllText(Path.Combine(git, "packed-refs"), sha + " refs/tags/v2.0.0\n");
        Assert.Equal("v2.0.0", GitDirReader.TryGetExactTag(b, sha));

        string c = GitPlugin("tagc", remote: null, sha: sha);
        Assert.Null(GitDirReader.TryGetExactTag(c, sha));
    }

    // ---------- PluginDiscoveryService ----------

    [Fact]
    public void Flat_lazy_keeps_legacy_rules()
    {
        string lazy = MkDir("lazy");
        GitPlugin(Path.Combine("lazy", "keep"), "https://github.com/a/keep.git", new string('1', 40));
        MkDir("lazy", "plain-dir");
        GitPlugin(Path.Combine("lazy", "lazy.nvim"), "https://github.com/folke/lazy.nvim.git", new string('3', 40));
        MkDir("lazy", ".hidden");

        var found = PluginDiscoveryService.DiscoverContainers(new[]
        {
            new PluginContainer(lazy, "lazy.nvim", PluginContainerKind.Flat),
        });

        var names = found.Select(f => Path.GetFileName(f.Dir)).ToList();
        Assert.Equal(new[] { "keep" }, names);
        Assert.Equal("lazy.nvim", found[0].Manager);
    }

    [Fact]
    public void Pack_sweep_attributes_managers_and_kinds()
    {
        string pack = MkDir("pack");
        MkDir("pack", "packer", "start", "a");
        MkDir("pack", "packer", "opt", "b");
        MkDir("pack", "paqs", "start", "c");
        MkDir("pack", "deps", "opt", "d");
        MkDir("pack", "core", "start", "e");
        MkDir("pack", "mybundle", "start", "f");
        MkDir("pack", "mybundle", "opt", "g");

        var found = PluginDiscoveryService.DiscoverContainers(new[]
        {
            new PluginContainer(pack, "vim pack", PluginContainerKind.Pack),
        });

        var map = found.ToDictionary(f => Path.GetFileName(f.Dir), f => (f.Manager, f.Kind));
        Assert.Equal(("packer.nvim", "start"), map["a"]);
        Assert.Equal(("packer.nvim", "opt"), map["b"]);
        Assert.Equal(("paq-nvim", "start"), map["c"]);
        Assert.Equal(("mini.deps", "opt"), map["d"]);
        Assert.Equal(("vim.pack", "start"), map["e"]);
        Assert.Equal(("vim pack", "start"), map["f"]);
        Assert.Equal(("vim pack", "opt"), map["g"]);
    }

    [Fact]
    public void Dein_repos_expand_two_levels()
    {
        string repos = MkDir("dein", "repos", "github.com");
        MkDir("dein", "repos", "github.com", "owner", "plug");

        var found = PluginDiscoveryService.DiscoverContainers(new[]
        {
            new PluginContainer(repos, "dein.vim", PluginContainerKind.DeinRepos),
        });

        Assert.Single(found);
        Assert.EndsWith(Path.Combine("owner", "plug"), found[0].Dir);
    }

    [Fact]
    public void Missing_roots_yield_nothing()
    {
        var found = PluginDiscoveryService.DiscoverContainers(new[]
        {
            new PluginContainer(Path.Combine(_root, "nope"), "lazy.nvim", PluginContainerKind.Flat),
            new PluginContainer(Path.Combine(_root, "nope2"), "vim pack", PluginContainerKind.Pack),
        });
        Assert.Empty(found);
    }

    [Fact]
    public void Same_dir_through_two_managers_lists_once()
    {
        string shared = MkDir("shared");
        GitPlugin(Path.Combine("shared", "dup"), "https://github.com/a/dup.git", new string('2', 40));

        var found = PluginDiscoveryService.DiscoverContainers(new[]
        {
            new PluginContainer(shared, "lazy.nvim", PluginContainerKind.Flat),
            new PluginContainer(shared, "vim-plug", PluginContainerKind.Flat),
        });

        Assert.Single(found);
        Assert.Equal("lazy.nvim", found[0].Manager); // first container wins
    }

    [Theory]
    [InlineData("telescope.nvim-0.1.8-1", "telescope.nvim")]
    [InlineData("plenary.nvim-scm-1", "plenary.nvim")]
    [InlineData("plain.nvim", "plain.nvim")]
    [InlineData("my-cool-plugin", "my-cool-plugin")]
    public void Rock_suffix_strips_version(string input, string expected)
        => Assert.Equal(expected, PluginDiscoveryService.StripRockSuffix(input));

    // ---------- StoreService integration ----------

    [Fact]
    public async Task ListInstalledAsync_covers_all_managers()
    {
        string data = MkDir("data");
        string config = MkDir("config");
        string home = MkDir("home");
        string lazy = MkDir("data", "lazy");

        // lazy.nvim plugin with github remote
        GitPlugin(Path.Combine("data", "lazy", "foo"), "https://github.com/acme/foo.nvim.git", new string('a', 40));
        // lazy.nvim plugin mapped through lazy-lock.json (dirs are named by repo tail)
        GitPlugin(Path.Combine("data", "lazy", "bar.nvim"), remote: null, sha: new string('b', 40));
        // git remote must win over a bare-name lock entry (seen in AstroNvim lockfiles)
        GitPlugin(Path.Combine("data", "lazy", "wrench"), "https://github.com/acme/wrench.nvim.git", new string('d', 40));
        File.WriteAllText(Path.Combine(config, "lazy-lock.json"), """{"acme/bar.nvim": {}, "wrench": {}}""");
        // packer.nvim plugin without git
        MkDir("data", "site", "pack", "packer", "start", "baz");
        // rocks.nvim rock
        MkDir("data", "rocks", "lib", "luarocks", "rocks-5.1", "qux.nvim-1.0-1");
        // dein.vim nested plugin with remote
        string dein = GitPlugin(Path.Combine("home", ".cache", "dein", "repos", "github.com", "quux", "quuxplug"),
            "git@github.com:quux/quuxplug.git", new string('c', 40));
        _ = dein;

        var paths = new NvimPaths(
            config, data, lazy,
            Path.Combine(config, "init.lua"),
            Path.Combine(config, "plugins.lua"),
            Path.Combine(config, "nvimmanager.lua"),
            Path.Combine(config, "lazy-lock.json"));

        // Point home at the fixture so dein/vim-home containers resolve inside it.
        using var store = new StoreService(new CatalogService(), new GitService(5000), new SettingsStore(), paths);

        var items = (await store.ListInstalledAsync(checkUpdates: false, homeDirOverride: home))
            .Where(i => i.Dir.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var byName = items.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(6, items.Count);
        Assert.Equal("acme/foo.nvim", byName["foo"].Repo);
        Assert.Equal(new string('a', 40)[..7], byName["foo"].ShortCommit);
        Assert.Equal("lazy.nvim", byName["foo"].Manager);
        Assert.Equal("acme/bar.nvim", byName["bar.nvim"].Repo);
        Assert.Equal("acme/wrench.nvim", byName["wrench"].Repo);
        Assert.Equal("packer.nvim", byName["baz"].Manager);
        Assert.Equal("start", byName["baz"].Kind);
        Assert.Equal("?/baz", byName["baz"].Repo);
        Assert.Equal("rocks.nvim", byName["qux.nvim"].Manager);
        Assert.Equal("dein.vim", byName["quuxplug"].Manager);
        Assert.Equal("quux/quuxplug", byName["quuxplug"].Repo);
    }

    // ---------- helpers ----------

    private string MkDir(params string[] parts)
    {
        string dir = parts.Aggregate(_root, Path.Combine);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GitPlugin(string relative, string? remote, string sha, string? tag = null)
    {
        string dir = MkDir(relative.Split(Path.AltDirectorySeparatorChar, '/'));
        string git = MkDir(relative.Split(Path.AltDirectorySeparatorChar, '/').Append(".git").ToArray());
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main");
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), sha);
        if (remote is not null)
            File.WriteAllText(Path.Combine(git, "config"), "[remote \"origin\"]\n\turl = " + remote + "\n");
        if (tag is not null)
        {
            Directory.CreateDirectory(Path.Combine(git, "refs", "tags"));
            File.WriteAllText(Path.Combine(git, "refs", "tags", tag), sha);
        }
        return dir;
    }
}