using NvimManager.Core.Lua;
using NvimManager.Core.Models;
using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

public sealed class AstroNvimTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nvastro-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Detects_lazy_setup_marker()
    {
        string cfg = MkConfig("a1");
        File.WriteAllText(Path.Combine(cfg, "lua", "lazy_setup.lua"), "return {}");
        Assert.True(AstroNvim.IsAstroNvimConfig(cfg));
    }

    [Fact]
    public void Detects_astrocore_plugin_file()
    {
        string cfg = MkConfig("a2");
        File.WriteAllText(Path.Combine(cfg, "lua", "plugins", "astrocore.lua"), "return {}");
        Assert.True(AstroNvim.IsAstroNvimConfig(cfg));
    }

    [Fact]
    public void Detects_astronvim_in_init_lua()
    {
        string cfg = MkConfig("a3");
        File.WriteAllText(Path.Combine(cfg, "init.lua"), "require(\"astrocore\")\n");
        Assert.True(AstroNvim.IsAstroNvimConfig(cfg));
    }

    [Fact]
    public void Plain_config_is_not_astronvim()
    {
        string cfg = MkConfig("plain");
        File.WriteAllText(Path.Combine(cfg, "init.lua"), "require(\"nvimmanager\")\n");
        Assert.False(AstroNvim.IsAstroNvimConfig(cfg));
        Assert.False(AstroNvim.IsAstroNvimConfig(Path.Combine(_root, "missing")));
    }

    [Theory]
    [InlineData("nvim-telescope/telescope.nvim", "telescope-nvim.lua")]
    [InlineData("folke/lazy.nvim", "lazy-nvim.lua")]
    [InlineData("owner/My_Plugin!", "my-plugin.lua")]
    [InlineData("bare-tail", "bare-tail.lua")]
    public void SpecFileName_derives_from_repo_tail(string repo, string expected)
        => Assert.Equal(expected, AstroNvim.SpecFileName(repo));

    [Fact]
    public void RenderAstroSpecFile_emits_standalone_spec()
    {
        var gen = new LazyConfigGenerator();
        var spec = new LazySpec
        {
            Repo = "acme/foo.nvim",
            Enabled = true,
            Events = new List<string> { "VeryLazy" },
            Opts = new LuaTable().Set("a", 1L),
        };

        string text = gen.RenderAstroSpecFile(spec);

        Assert.Contains(LazyConfigGenerator.ManagedMarker, text);
        Assert.Contains("\"acme/foo.nvim\"", text);
        Assert.Contains("event = \"VeryLazy\"", text);
        Assert.Contains("a = 1", text);
        Assert.StartsWith(LazyConfigGenerator.ManagedMarker, text.TrimStart());
    }

    [Fact]
    public void WriteAstroSpec_writes_and_removes_managed_files_only()
    {
        string cfg = MkConfig("managed");
        string data = MkDir("managed-data");
        var paths = TestPaths(cfg, data);
        using var store = new StoreService(new CatalogService(), new GitService(5000), new SettingsStore(), paths);
        var entry = new CatalogEntry("t", "Foo", "acme/foo.nvim", "d", null,
            "Editing", new List<string>(), new List<string>(), null, null);

        string msg = store.WriteAstroSpec(entry, new PluginSettings
        {
            Repo = "acme/foo.nvim",
            Enabled = true,
            Events = new List<string> { "VeryLazy" },
        });
        string specPath = AstroNvim.SpecPath(cfg, "acme/foo.nvim");
        Assert.True(File.Exists(specPath));
        Assert.Contains(specPath, msg);
        Assert.Contains(LazyConfigGenerator.ManagedMarker, File.ReadAllText(specPath));

        // Disabled settings remove our file...
        string removed = store.WriteAstroSpec(entry, new PluginSettings { Repo = "acme/foo.nvim", Enabled = false });
        Assert.False(File.Exists(specPath));
        Assert.Contains(specPath, removed);

        // ...but never a user-owned file at the same path.
        File.WriteAllText(specPath, "return { \"acme/foo.nvim\" }");
        string skipped = store.WriteAstroSpec(entry, new PluginSettings { Repo = "acme/foo.nvim", Enabled = false });
        Assert.True(File.Exists(specPath));
        Assert.DoesNotContain(specPath, skipped);
        Assert.Equal(string.Empty, store.DeleteAstroSpec("acme/foo.nvim"));
    }

    [Fact]
    public void ApplyAstroConfig_runs_without_throwing()
    {
        string cfg = MkConfig("apply");
        var paths = TestPaths(cfg, MkDir("apply-data"));
        using var store = new StoreService(new CatalogService(), new GitService(5000), new SettingsStore(), paths);
        var messages = store.ApplyAstroConfig();
        Assert.NotEmpty(messages);
    }

    // ---------- helpers ----------

    private string MkConfig(string name)
    {
        string cfg = MkDir(name);
        Directory.CreateDirectory(Path.Combine(cfg, "lua", "plugins"));
        return cfg;
    }

    private NvimPaths TestPaths(string config, string data) => new(
        config, data, Path.Combine(data, "lazy"),
        Path.Combine(config, "init.lua"),
        Path.Combine(config, "plugins.lua"),
        Path.Combine(config, "nvimmanager.lua"),
        Path.Combine(config, "lazy-lock.json"));

    private string MkDir(params string[] parts)
    {
        string dir = parts.Aggregate(_root, Path.Combine);
        Directory.CreateDirectory(dir);
        return dir;
    }
}