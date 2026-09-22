using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>
/// Resolves the neovim config / data paths used on this machine, following the
/// same precedence as neovim itself (XDG vars, then Windows defaults).
/// </summary>
public static class NeovimLocator
{
    public static NvimPaths Locate()
    {
        string cfgBase = EnvDir("XDG_CONFIG_HOME") ?? ConfigDefault();
        string? xdgData = EnvDir("XDG_DATA_HOME");

        string configDir = Path.Combine(cfgBase, "nvim");
        // Explicit XDG wins everywhere (Linux/macOS layout). Otherwise the
        // platform default already includes the leaf directory name.
        // NOTE: on Windows real Neovim uses %LOCALAPPDATA%\nvim-data, not \nvim.
        string dataDir = xdgData is not null ? Path.Combine(xdgData, "nvim") : DataDefault();

        return new NvimPaths(
            configDir,
            dataDir,
            Path.Combine(dataDir, "lazy"),
            Path.Combine(configDir, "init.lua"),
            Path.Combine(configDir, "lua", "nvimmanager", "plugins.lua"),
            Path.Combine(configDir, "lua", "nvimmanager.lua"),
            Path.Combine(configDir, "lazy-lock.json"));
    }

    private static string? EnvDir(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ConfigDefault()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? user : local;
        }
        return Path.Combine(user, ".config");
    }

    private static string DataDefault()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Must match Neovim's own stdpath("data"): %LOCALAPPDATA%\nvim-data.
            // (Previously this resolved to %LOCALAPPDATA%\nvim, a directory real
            // Neovim never reads plugins from.)
            return string.IsNullOrEmpty(local) ? user : Path.Combine(local, "nvim-data");
        }
        return Path.Combine(user, ".local", "share", "nvim");
    }

    /// <summary>Cache/data directory for the app itself (settings + enrichment cache).</summary>
    public static string AppDataDir()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "NvimManager");
    }
}