using System.Text.RegularExpressions;

namespace NvimManager.Core.Services;

/// <summary>
/// AstroNvim v4+ support. AstroNvim is lazy.nvim-based, so its plugins live in
/// the standard lazy root and are discovered normally — but its configuration
/// lives in per-plugin spec files under lua/plugins/, NOT in a central table.
/// These helpers detect an AstroNvim config and map repos to the spec files
/// NvimManager owns there (guarded by the managed marker, so user files are
/// never touched). AstroNvim v3 (packer-based) needs no special handling:
/// its plugins surface through the regular pack sweep.
/// </summary>
public static partial class AstroNvim
{
    /// <summary>Heuristic markers of an AstroNvim v4+ config directory.</summary>
    public static bool IsAstroNvimConfig(string configDir)
    {
        try
        {
            if (!Directory.Exists(configDir)) return false;
            if (File.Exists(Path.Combine(configDir, "lua", "lazy_setup.lua"))) return true;
            if (File.Exists(Path.Combine(configDir, "lua", "plugins", "astrocore.lua"))) return true;
            if (File.Exists(Path.Combine(configDir, "lua", "plugins", "astroui.lua"))) return true;
            string init = Path.Combine(configDir, "init.lua");
            if (File.Exists(init))
            {
                string text = File.ReadAllText(init);
                if (text.Contains("astronvim", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("astrocore", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("astroui", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // unreadable config -> not AstroNvim as far as we can tell
        }
        return false;
    }

    /// <summary>
    /// File name for a repo's spec, derived from the repo tail only
    /// ("nvim-telescope/telescope.nvim" -> "telescope-nvim.lua"), so the same
    /// repo always maps to the same file for install/save/uninstall alike.
    /// </summary>
    public static string SpecFileName(string repo)
    {
        string tail = (repo.Split('/')[^1] ?? string.Empty).ToLowerInvariant();
        var sb = new System.Text.StringBuilder(tail.Length);
        foreach (char c in tail)
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        string name = MultiDashRegex().Replace(sb.ToString(), "-").Trim('-');
        if (string.IsNullOrEmpty(name)) name = "plugin";
        return name + ".lua";
    }

    public static string SpecPath(string configDir, string repo)
        => Path.Combine(configDir, "lua", "plugins", SpecFileName(repo));

    /// <summary>True only for spec files NvimManager itself generated.</summary>
    public static bool IsManagedSpecFile(string path)
    {
        try
        {
            return File.Exists(path)
                && File.ReadAllText(path).Contains(LazyConfigGenerator.ManagedMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDashRegex();
}