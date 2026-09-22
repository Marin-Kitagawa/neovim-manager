using NvimManager.Core.Lua;
using NvimManager.Core.Models;
using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

public class LazyConfigGeneratorTests
{
    private readonly LazyConfigGenerator _generator = new();

    private static LazySpec SampleSpec()
    {
        return new LazySpec
        {
            Repo = "folke/tokyonight.nvim",
            Priority = 1000,
            Lazy = false,
            Version = "*",
            Dependencies = new List<string> { "nvim-lua/plenary.nvim" },
            Events = new List<string> { "VeryLazy" },
            Commands = new List<string> { "Telescope" },
            FileTypes = new List<string> { "lua", "js" },
            Opts = new LuaTable()
                .Set("style", "night")
                .Set("transparent", false)
                .Set("hl", new LuaTable().Set("enable", true)),
        };
    }

    [Fact]
    public void Renders_full_spec_entry()
    {
        string text = _generator.RenderPluginsFile(new[] { SampleSpec() });

        Assert.Contains("\"folke/tokyonight.nvim\"", text);
        Assert.Contains("priority = 1000", text);
        Assert.Contains("lazy = false", text);
        Assert.Contains("version = \"*\"", text);
        Assert.Contains("dependencies = { \"nvim-lua/plenary.nvim\" }", text);
        Assert.Contains("event = \"VeryLazy\"", text);
        Assert.Contains("cmd = \"Telescope\"", text);
        Assert.Contains("ft = { \"lua\", \"js\" }", text);
        Assert.Contains("opts = {", text);
        Assert.Contains("  style = \"night\"", text);
        Assert.Contains("  hl = {", text);
        Assert.Contains("    enable = true", text);
    }

    [Fact]
    public void Raw_opts_lua_replaces_table()
    {
        var spec = SampleSpec();
        spec.Opts = new RawLua("{ icons = true }");
        string text = _generator.RenderPluginsFile(new[] { spec });

        Assert.Contains("opts = { icons = true }", text);
    }

    [Fact]
    public void Config_function_body_is_indented()
    {
        var spec = SampleSpec();
        spec.Config = "vim.g.demo = 1\nrequire(\"x\")";
        string text = _generator.RenderPluginsFile(new[] { spec });

        Assert.Contains("config = function(_, opts)", text);
        Assert.Contains("      vim.g.demo = 1", text);
        Assert.Contains("    end,", text);
    }

    [Fact]
    public void Disabled_specs_are_skipped()
    {
        var spec = SampleSpec();
        spec.Enabled = false;
        string text = _generator.RenderPluginsFile(new[] { spec });
        Assert.DoesNotContain("tokyonight", text);
    }

    [Fact]
    public void Module_bootstraps_lazy()
    {
        string module = _generator.RenderModule();
        Assert.Contains("lazy.nvim", module);
        Assert.Contains("vim.opt.rtp:prepend", module);
        Assert.Contains("require(\"lazy\").setup(require(\"nvimmanager.plugins\")", module);
    }

    [Fact]
    public void Init_lua_requires_module()
    {
        string init = _generator.RenderInitLua();
        Assert.Contains("require(\"nvimmanager\")", init);
    }

    [Fact]
    public void ApplyToDisk_writes_files_and_creates_init()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "nvimmanager-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new NvimPaths(
                tmp,
                Path.Combine(tmp, "data"),
                Path.Combine(tmp, "data", "lazy"),
                Path.Combine(tmp, "init.lua"),
                Path.Combine(tmp, "lua", "nvimmanager", "plugins.lua"),
                Path.Combine(tmp, "lua", "nvimmanager.lua"),
                Path.Combine(tmp, "lazy-lock.json"));

            var messages = _generator.ApplyToDisk(paths, new[] { SampleSpec() });

            Assert.True(File.Exists(paths.InitLuaPath));
            Assert.True(File.Exists(paths.PluginsLuaPath));
            Assert.True(File.Exists(paths.ModulePath));
            Assert.Contains(messages, m => m.StartsWith("Created"));
            Assert.Contains(messages, m => m.Contains("plugins.lua"));
            Assert.Contains(LazyConfigGenerator.ManagedMarker, File.ReadAllText(paths.InitLuaPath));
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void ApplyToDisk_keeps_foreign_init_lua()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "nvimmanager-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            string initPath = Path.Combine(tmp, "init.lua");
            File.WriteAllText(initPath, "-- my own config\nvim.g.x = 1");

            var paths = new NvimPaths(
                tmp,
                Path.Combine(tmp, "data"),
                Path.Combine(tmp, "data", "lazy"),
                initPath,
                Path.Combine(tmp, "lua", "nvimmanager", "plugins.lua"),
                Path.Combine(tmp, "lua", "nvimmanager.lua"),
                Path.Combine(tmp, "lazy-lock.json"));

            var messages = _generator.ApplyToDisk(paths, Array.Empty<LazySpec>());

            Assert.Contains("Kept existing", messages[^1]);
            Assert.Equal("-- my own config\nvim.g.x = 1", File.ReadAllText(initPath));
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);
        }
    }
}