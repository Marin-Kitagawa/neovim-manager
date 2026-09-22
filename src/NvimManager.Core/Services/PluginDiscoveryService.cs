using System.Text.RegularExpressions;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>A plugin directory found on disk, attributed to the manager that owns it.</summary>
public sealed record DiscoveredPlugin(string Dir, string Manager, string Kind);

/// <summary>How a container directory holds plugins.</summary>
public enum PluginContainerKind
{
    /// <summary>Every child directory is a plugin (lazy root, plugged, bundle, ...).</summary>
    Flat,
    /// <summary>pack/&lt;name&gt;/{start,opt}/* layout (all pack-based managers).</summary>
    Pack,
    /// <summary>dein layout: repos/github.com/&lt;owner&gt;/&lt;repo&gt;.</summary>
    DeinRepos,
    /// <summary>rocks.nvim layout: lib/luarocks/rocks-5.1/&lt;rock&gt;-&lt;version&gt;-&lt;rev&gt;.</summary>
    Rocks,
}

/// <summary>One directory that contains plugins, plus how to walk it.</summary>
public sealed record PluginContainer(string Dir, string Manager, PluginContainerKind Kind);

/// <summary>
/// Finds installed Neovim plugins across every common plugin manager and
/// framework layout: lazy.nvim (incl. LazyVim / NvChad / AstroNvim, which all
/// use it), packer.nvim, paq-nvim, pckr.nvim, mini.deps, vim.pack (nvim 0.12+),
/// native :packadd packages, vim-plug, dein.vim (incl. SpaceVim-style bases),
/// LunarVim, rocks.nvim, Vundle/pathogen-style bundles and minpac.
/// Pure filesystem scan — no processes spawned. Results deduped by directory.
/// </summary>
public static partial class PluginDiscoveryService
{
    /// <summary>Builds the container list for this machine and walks it.</summary>
    public static IReadOnlyList<DiscoveredPlugin> Discover(NvimPaths paths, string? homeDirOverride = null)
        => DiscoverContainers(BuildContainers(paths, homeDirOverride ?? HomeDir()));

    /// <summary>Walks an explicit container list (used by tests).</summary>
    public static IReadOnlyList<DiscoveredPlugin> DiscoverContainers(IEnumerable<PluginContainer> containers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<DiscoveredPlugin>();
        foreach (var container in containers)
        {
            foreach (var plugin in Walk(container))
            {
                string full = Normalize(plugin.Dir);
                if (seen.Add(full))
                    found.Add(plugin with { Dir = full });
            }
        }
        return found;
    }

    /// <summary>
    /// Parent directories that directly hold plugins (used to guard destructive
    /// operations: a dir may only be uninstalled when its parent is listed here).
    /// </summary>
    public static IReadOnlySet<string> PluginContainerDirs(NvimPaths paths, string? homeDirOverride = null)
    {
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in BuildContainers(paths, homeDirOverride ?? HomeDir()))
        {
            foreach (var parent in ContainerParents(container))
                parents.Add(Normalize(parent));
        }
        return parents;
    }

    /// <summary>Strips the luarocks "-&lt;version&gt;-&lt;rev&gt;" suffix, e.g. telescope.nvim-0.1.8-1.</summary>
    public static string StripRockSuffix(string dirName)
    {
        var m = RockRegex().Match(dirName);
        return m.Success ? m.Groups["name"].Value : dirName;
    }

    internal static IReadOnlyList<PluginContainer> BuildContainers(NvimPaths paths, string home)
    {
        string data = paths.DataDir;
        string config = paths.ConfigDir;
        string vimHome = Path.Combine(home, ".vim");
        string cache = CacheDir(home);
        string lunarData = Path.Combine(home, ".local", "share", "lunarvim");

        return new List<PluginContainer>
        {
            // lazy.nvim and the distros built on it (LazyVim, NvChad, AstroNvim).
            // Legacy rule preserved: only git checkouts count here.
            new(paths.LazyRoot, "lazy.nvim", PluginContainerKind.Flat),
            new(Path.Combine(lunarData, "lazy"), "lunarvim", PluginContainerKind.Flat),

            // pack-based managers + native :packadd, in data/config/vim homes.
            new(Path.Combine(data, "site", "pack"), "vim pack", PluginContainerKind.Pack),
            new(Path.Combine(config, "pack"), "vim pack", PluginContainerKind.Pack),
            new(Path.Combine(vimHome, "pack"), "vim pack", PluginContainerKind.Pack),
            new(Path.Combine(lunarData, "site", "pack"), "lunarvim", PluginContainerKind.Pack),

            // vim-plug default location (+ vim-home variant).
            new(Path.Combine(data, "plugged"), "vim-plug", PluginContainerKind.Flat),
            new(Path.Combine(vimHome, "plugged"), "vim-plug", PluginContainerKind.Flat),

            // Vundle / pathogen / NeoBundle style bundles.
            new(Path.Combine(vimHome, "bundle"), "vim bundle", PluginContainerKind.Flat),
            new(Path.Combine(config, "bundle"), "vim bundle", PluginContainerKind.Flat),
            new(Path.Combine(home, ".SpaceVim", "bundle"), "SpaceVim", PluginContainerKind.Flat),

            // dein.vim bases (dein clones to repos/github.com/owner/repo).
            new(Path.Combine(cache, "dein", "repos", "github.com"), "dein.vim", PluginContainerKind.DeinRepos),
            new(Path.Combine(cache, "vimfiles", "repos", "github.com"), "dein.vim", PluginContainerKind.DeinRepos),

            // rocks.nvim luarocks tree.
            new(Path.Combine(data, "rocks", "lib", "luarocks", "rocks-5.1"), "rocks.nvim", PluginContainerKind.Rocks),
        };
    }

    private static IEnumerable<DiscoveredPlugin> Walk(PluginContainer container)
    {
        IEnumerable<DiscoveredPlugin> Empty() => Enumerable.Empty<DiscoveredPlugin>();
        switch (container.Kind)
        {
            case PluginContainerKind.Flat:
                if (!Directory.Exists(container.Dir)) return Empty();
                return EnumerateChildren(container.Dir)
                    .Where(d => IsLazyRoot(container)
                        ? IsLegacyLazyPlugin(d)   // preserve lazy.nvim semantics
                        : true)
                    .Select(d => new DiscoveredPlugin(d, container.Manager, string.Empty));

            case PluginContainerKind.Pack:
                return WalkPack(container);

            case PluginContainerKind.DeinRepos:
                if (!Directory.Exists(container.Dir)) return Empty();
                return EnumerateChildren(container.Dir)
                    .SelectMany(EnumerateChildren)
                    .Select(d => new DiscoveredPlugin(d, container.Manager, string.Empty));

            case PluginContainerKind.Rocks:
                if (!Directory.Exists(container.Dir)) return Empty();
                return EnumerateChildren(container.Dir)
                    .Select(d => new DiscoveredPlugin(d, container.Manager, string.Empty));

            default:
                return Empty();
        }

        static bool IsLazyRoot(PluginContainer c)
            => c.Manager is "lazy.nvim" or "lunarvim" && c.Kind == PluginContainerKind.Flat;
    }

    private static IEnumerable<DiscoveredPlugin> WalkPack(PluginContainer container)
    {
        if (!Directory.Exists(container.Dir)) yield break;
        foreach (string packDir in EnumerateChildren(container.Dir))
        {
            string manager = PackManager(Path.GetFileName(packDir)) ?? container.Manager;
            foreach (string kind in new[] { "start", "opt" })
            {
                string kindDir = Path.Combine(packDir, kind);
                if (!Directory.Exists(kindDir)) continue;
                foreach (string plugin in EnumerateChildren(kindDir))
                    yield return new DiscoveredPlugin(plugin, manager, kind);
            }
        }
    }

    private static IEnumerable<string> ContainerParents(PluginContainer container)
    {
        switch (container.Kind)
        {
            case PluginContainerKind.Flat:
            case PluginContainerKind.Rocks:
                if (Directory.Exists(container.Dir)) yield return container.Dir;
                break;
            case PluginContainerKind.Pack:
                if (!Directory.Exists(container.Dir)) yield break;
                foreach (string packDir in EnumerateChildren(container.Dir))
                    foreach (string kind in new[] { "start", "opt" })
                    {
                        string kindDir = Path.Combine(packDir, kind);
                        if (Directory.Exists(kindDir)) yield return kindDir;
                    }
                break;
            case PluginContainerKind.DeinRepos:
                if (!Directory.Exists(container.Dir)) yield break;
                foreach (string owner in EnumerateChildren(container.Dir))
                    yield return owner;
                break;
        }
    }

    private static IEnumerable<string> EnumerateChildren(string dir)
    {
        IEnumerable<string> dirs = Enumerable.Empty<string>();
        try { dirs = Directory.EnumerateDirectories(dir); } catch { /* unreadable -> skip */ }
        foreach (string child in dirs)
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith('.')) continue; // .git, .DS_Store-style entries are never plugins
            yield return child;
        }
    }

    // Legacy lazy.nvim rule: the root only counts git checkouts, never lazy.nvim itself.
    private static bool IsLegacyLazyPlugin(string dir)
    {
        string name = Path.GetFileName(dir);
        if (string.Equals(name, "lazy.nvim", StringComparison.OrdinalIgnoreCase)) return false;
        return GitDirReader.HasGit(dir);
    }

    private static string? PackManager(string packName) => packName.ToLowerInvariant() switch
    {
        "packer" => "packer.nvim",
        "paqs" => "paq-nvim",
        "pckr" => "pckr.nvim",
        "deps" => "mini.deps",
        "core" => "vim.pack",
        "minpac" => "minpac",
        _ => null,
    };

    private static string HomeDir()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string CacheDir(string home)
    {
        // Explicit XDG_CACHE_HOME wins everywhere; otherwise dein/SpaceVim
        // convention (~/.cache) applies on all platforms.
        string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdg)) return xdg;
        return Path.Combine(home, ".cache");
    }

    private static string Normalize(string dir)
        => Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    [GeneratedRegex("""^(?<name>.+)-(?<ver>[^-]+)-(?<rev>\d+)$""")]
    private static partial Regex RockRegex();
}