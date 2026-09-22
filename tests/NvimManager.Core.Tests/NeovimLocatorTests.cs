using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

public sealed class NeovimLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nvloc-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _oldConfig);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData);
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Xdg_overrides_produce_nvim_layout()
    {
        string cfg = Directory.CreateDirectory(Path.Combine(_root, "cfg")).FullName;
        string data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", cfg);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);

        var paths = NeovimLocator.Locate();

        Assert.Equal(Path.Combine(cfg, "nvim"), paths.ConfigDir);
        Assert.Equal(Path.Combine(data, "nvim"), paths.DataDir);
        Assert.Equal(Path.Combine(data, "nvim", "lazy"), paths.LazyRoot);
    }

    [Fact]
    public void Windows_default_data_dir_is_nvim_data()
    {
        if (!OperatingSystem.IsWindows()) return; // default branch is platform-specific
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);

        var paths = NeovimLocator.Locate();

        Assert.EndsWith("nvim-data", paths.DataDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("nvim-data", "lazy"), paths.LazyRoot, StringComparison.OrdinalIgnoreCase);
    }
}