using System.Text.Json;
using NvimManager.Core.Lua;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

public sealed record OpResult(bool Success, string Message);

public sealed record InstalledItem(CatalogEntry? Entry, string Repo, string Name, string Dir, string Manager, string Kind, string? ShortCommit, string? Tag, bool HasUpdate);

/// <summary>
/// Coordinates catalog, git and config generation: the single façade the UI talks to.
/// </summary>
public sealed class StoreService : IDisposable
{
    private readonly CatalogService _catalog;
    private readonly GitService _git;
    private readonly SettingsStore _settings;
    private readonly LazyConfigGenerator _generator;
    private readonly NvimPaths _paths;

    public StoreService(CatalogService catalog, GitService git, SettingsStore settings, NvimPaths paths)
    {
        _catalog = catalog;
        _git = git;
        _settings = settings;
        _generator = new LazyConfigGenerator();
        _paths = paths;
    }

    public CatalogService Catalog => _catalog;
    public GitService Git => _git;
    public SettingsStore Settings => _settings;
    public LazyConfigGenerator Generator => _generator;
    public NvimPaths Paths => _paths;

    public string InstallDirFor(string repo)
        => Path.Combine(_paths.LazyRoot, repo.Split('/')[^1]);

    /// <summary>True when the active config is AstroNvim: specs live in lua/plugins/, never in a managed bundle.</summary>
    public bool IsAstroNvim => AstroNvim.IsAstroNvimConfig(_paths.ConfigDir);

    public async Task<OpResult> InstallAsync(CatalogEntry entry, string? branch = null)
    {
        try
        {
            Directory.CreateDirectory(_paths.LazyRoot);
            string dir = InstallDirFor(entry.Repo);
            if (GitService.LooksInstalled(dir))
                return new OpResult(true, "Already installed.");

            var r = await _git.CloneAsync(entry.Repo, dir, branch);
            if (!r.Success)
                return new OpResult(false, "git clone failed:\n" + r.Combined);

            var commit = await _git.RevParseAsync(dir);
            string message = $"Installed {entry.Repo} @ {CommitShort(commit)}";
            if (IsAstroNvim)
                message += "\n" + WriteAstroSpec(entry, _settings.GetOrCreate(entry.Repo));
            return new OpResult(true, message);
        }
        catch (Exception ex)
        {
            return new OpResult(false, ex.Message);
        }
    }

    public async Task<OpResult> UninstallAsync(string repo)
    {
        string dir = InstallDirFor(repo);
        if (!GitService.LooksInstalled(dir))
            return new OpResult(true, "Not installed.");

        try
        {
            GitService.DeleteDirectory(dir);
            string message = $"Removed {repo}.";
            if (IsAstroNvim)
            {
                string spec = DeleteAstroSpec(repo);
                if (!string.IsNullOrEmpty(spec)) message += "\n" + spec;
            }
            return new OpResult(true, message);
        }
        catch (Exception ex)
        {
            return new OpResult(false, $"Failed to remove {dir}: {ex.Message}");
        }
    }

    public async Task<OpResult> UpdateAsync(string repo)
    {
        string dir = InstallDirFor(repo);
        if (!GitService.LooksInstalled(dir))
            return new OpResult(false, $"Not installed: {repo}");

        var r = await _git.UpdateAsync(dir);
        return r.Success
            ? new OpResult(true, $"Updated {repo}.")
            : new OpResult(false, $"Update of {repo} failed:\n{r.Combined}");
    }

    public async Task<OpResult> UpdateAllAsync()
    {
        var items = await ListInstalledAsync(checkUpdates: false);
        var failures = new List<string>();
        int ok = 0;
        foreach (var item in items)
        {
            var r = await UpdateDirAsync(item.Dir);
            if (r.Success) ok++;
            else failures.Add(item.Repo + ": " + r.Message);
        }
        string summary = $"Updated {ok}/{items.Count} plugin(s).";
        if (failures.Count > 0) summary += "\n\n" + string.Join("\n", failures);
        return new OpResult(failures.Count == 0, summary);
    }

    /// <summary>Fast-forward updates the git checkout at dir (any manager).</summary>
    public async Task<OpResult> UpdateDirAsync(string dir)
    {
        if (!GitDirReader.HasGit(dir))
            return new OpResult(false, $"Not a git checkout: {dir}");

        var r = await _git.UpdateAsync(dir);
        return r.Success
            ? new OpResult(true, $"Updated {dir}.")
            : new OpResult(false, $"Update of {dir} failed:\n{r.Combined}");
    }

    /// <summary>Removes a discovered plugin directory (any manager).</summary>
    public async Task<OpResult> UninstallDirAsync(string dir)
    {
        if (!IsManagedDir(dir))
            return new OpResult(false, $"Refusing to remove unmanaged directory: {dir}");
        if (!Directory.Exists(dir))
            return new OpResult(true, "Not installed.");

        try
        {
            await Task.Run(() => GitService.DeleteDirectory(dir));
            string message = $"Removed {dir}.";
            if (IsAstroNvim)
            {
                string spec = DeleteAstroSpec(Path.GetFileName(dir));
                if (!string.IsNullOrEmpty(spec)) message += "\n" + spec;
            }
            return new OpResult(true, message);
        }
        catch (Exception ex)
        {
            return new OpResult(false, $"Failed to remove {dir}: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes (or removes, when disabled) the AstroNvim spec file for one plugin.
    /// Only ever touches files carrying the managed marker.
    /// </summary>
    public string WriteAstroSpec(CatalogEntry entry, PluginSettings settings)
    {
        string path = AstroNvim.SpecPath(_paths.ConfigDir, entry.Repo);
        if (!settings.Enabled)
        {
            if (AstroNvim.IsManagedSpecFile(path))
            {
                File.Delete(path);
                return $"Removed disabled {path}";
            }
            return $"Skipped disabled {entry.Repo}.";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Generator.RenderAstroSpecFile(BuildSpec(entry, settings)));
        return $"Wrote {path}";
    }

    /// <summary>Deletes our spec file for a repo (or bare tail); returns "" when there is nothing managed to delete.</summary>
    public string DeleteAstroSpec(string repo)
    {
        string path = AstroNvim.SpecPath(_paths.ConfigDir, repo);
        if (AstroNvim.IsManagedSpecFile(path))
        {
            File.Delete(path);
            return $"Removed {path}";
        }
        return string.Empty;
    }

    /// <summary>
    /// Regenerates every AstroNvim spec file from saved settings (enabled ones
    /// are written, disabled ones removed). Never touches user-owned files.
    /// </summary>
    public IReadOnlyList<string> ApplyAstroConfig()
    {
        var messages = new List<string>();
        foreach (var (repo, settings) in _settings.All)
        {
            var entry = _catalog.GetByRepo(repo);
            if (entry is null) continue;
            messages.Add(WriteAstroSpec(entry, settings));
        }
        if (messages.Count == 0)
            messages.Add("AstroNvim specs are up to date: no configured plugins to write.");
        return messages;
    }

    /// <summary>Safety guard: only directories sitting directly inside a known plugin container may be removed.</summary>
    public bool IsManagedDir(string dir)
    {
        try
        {
            string? parent = Path.GetDirectoryName(
                Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return parent is not null && PluginDiscoveryService.PluginContainerDirs(_paths).Contains(parent);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists installed plugins across all supported managers (lazy.nvim, packer.nvim,
    /// paq-nvim, pckr.nvim, mini.deps, vim.pack, native :packadd packages, vim-plug,
    /// dein.vim, LunarVim, rocks.nvim, Vundle-style bundles, minpac).
    /// Git metadata is read from files (no processes); update checks compare HEAD
    /// against the locally known origin/HEAD, exactly like a bare rev-parse would.
    /// </summary>
    public async Task<List<InstalledItem>> ListInstalledAsync(bool checkUpdates = false, string? homeDirOverride = null)
    {
        var lockMap = ReadLockFile();
        var items = new List<InstalledItem>();

        foreach (var found in PluginDiscoveryService.Discover(_paths, homeDirOverride))
        {
            string dirName = Path.GetFileName(found.Dir);
            string name = found.Manager == "rocks.nvim"
                ? PluginDiscoveryService.StripRockSuffix(dirName)
                : dirName;

            string? remote = GitDirReader.TryGetRemoteUrl(found.Dir);
            string? remoteRepo = null;
            string? remoteHost = null;
            if (remote is not null
                && GitDirReader.TryNormalizeRepoUrl(remote, out var host, out var path))
            {
                remoteHost = host;
                remoteRepo = path;
            }

            string repo;
            // NOTE: some lockfiles (observed with AstroNvim) key entries by bare
            // plugin name instead of "owner/repo". Such values carry no owner
            // info, so ignore them here and let the git remote below win.
            if (lockMap.TryGetValue(name, out var full) && IsOwnerRepo(full))
            {
                repo = full;
            }
            else if (remoteRepo is not null && remoteHost == "github.com" && IsOwnerRepo(remoteRepo))
            {
                repo = remoteRepo;
            }
            else if (remoteRepo is not null)
            {
                repo = $"{remoteHost}/{remoteRepo}";
            }
            else if (found.Manager == "dein.vim" && TryDeinRepo(found.Dir, out var deinRepo))
            {
                repo = deinRepo;
            }
            else
            {
                repo = ResolveRepoFromCatalog(name);
            }

            var entry = _catalog.GetByRepo(repo);
            entry ??= remote is null ? ResolveEntryByName(name) : null;

            string? head = null;
            string? tag = null;
            if (GitDirReader.TryGetHeadSha(found.Dir, out var h) && h is not null)
            {
                head = h;
                tag = GitDirReader.TryGetExactTag(found.Dir, h);
            }

            bool hasUpdate = false;
            if (checkUpdates && head is not null)
            {
                string? gitDir = GitDirReader.TryResolveGitDir(found.Dir);
                if (gitDir is not null
                    && GitDirReader.TryResolveRef(gitDir, "refs/remotes/origin/HEAD", out var latest)
                    && latest is not null)
                    hasUpdate = !string.Equals(latest, head, StringComparison.OrdinalIgnoreCase);
            }

            items.Add(new InstalledItem(
                Entry: entry,
                Repo: repo,
                Name: name,
                Dir: found.Dir,
                Manager: found.Manager,
                Kind: found.Kind,
                ShortCommit: head is { Length: > 7 } ? head[..7] : head,
                Tag: tag,
                HasUpdate: hasUpdate));
        }

        // Preserve a stable order; ListInstalledAsync is also sync-completed work, keep it awaitable.
        await Task.CompletedTask;
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsOwnerRepo(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2;
    }

    private static bool TryDeinRepo(string dir, out string repo)
    {
        repo = string.Empty;
        string? owner = Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty);
        string name = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name)) return false;
        repo = $"{owner}/{name}";
        return true;
    }

    private CatalogEntry? ResolveEntryByName(string name)
        => _catalog.All.FirstOrDefault(e => string.Equals(e.ProjectName, name, StringComparison.OrdinalIgnoreCase));

    private string ResolveRepoFromCatalog(string name)
    {
        var match = _catalog.All.FirstOrDefault(e => string.Equals(e.ProjectName, name, StringComparison.OrdinalIgnoreCase));
        return match?.Repo ?? $"?/{name}";
    }

    private Dictionary<string, string> ReadLockFile()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_paths.LockPath)) return map;
            using var doc = JsonDocument.Parse(File.ReadAllText(_paths.LockPath));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string shortName = prop.Name.Split('/')[^1];
                map[shortName] = prop.Name;
            }
        }
        catch
        {
            // optional metadata only
        }
        return map;
    }

    /// <summary>Builds the spec table for the managed config from catalog + saved settings.</summary>
    public IReadOnlyList<LazySpec> BuildSpecs()
    {
        var specs = new List<LazySpec>();

        foreach (var (repo, settings) in _settings.All)
        {
            var entry = _catalog.GetByRepo(repo);
            if (entry is not null)
                specs.Add(BuildSpec(entry, settings));
        }

        foreach (var candidate in Directory.EnumerateDirectories(_paths.LazyRoot))
        {
            if (!GitService.LooksInstalled(candidate)) continue;
            string name = Path.GetFileName(candidate);
            if (string.Equals(name, "lazy.nvim", StringComparison.OrdinalIgnoreCase)) continue;

            string repo = ResolveRepoFromCatalog(name);
            if (_settings.All.ContainsKey(repo)) continue;
            var entry = _catalog.GetByRepo(repo);
            if (entry is not null)
            {
                var settings = new PluginSettings { Repo = repo };
                specs.Add(BuildSpec(entry, settings));
            }
        }

        return specs.DistinctBy(s => s.Repo).ToList();
    }

    public LazySpec BuildSpec(CatalogEntry entry, PluginSettings? s)
    {
        s ??= new PluginSettings { Repo = entry.Repo };
        var spec = new LazySpec
        {
            Repo = entry.Repo,
            Enabled = s.Enabled,
            Lazy = s.Lazy,
            Version = string.IsNullOrWhiteSpace(s.Version) ? null : s.Version,
            Branch = string.IsNullOrWhiteSpace(s.Branch) ? null : s.Branch,
            Priority = s.Priority,
            Events = s.Events ?? new List<string>(),
            Commands = s.Commands ?? new List<string>(),
            FileTypes = s.FileTypes ?? new List<string>(),
            Keys = string.IsNullOrWhiteSpace(s.Keys) ? null : s.Keys,
            Build = !string.IsNullOrWhiteSpace(s.Build) ? s.Build : entry.Build,
            Dev = s.Dev,
            Dependencies = entry.Dependencies,
            Config = string.IsNullOrWhiteSpace(s.Config) ? null : s.Config,
            Init = string.IsNullOrWhiteSpace(s.Init) ? null : s.Init,
        };

        spec.Opts = string.IsNullOrWhiteSpace(s.RawOptsLua)
            ? LuaOptionsBuilder.Build(s.Opts)
            : new RawLua(s.RawOptsLua);

        return spec;
    }

    public PluginSchema? TryLoadSchema(CatalogEntry entry) => _catalog.TryLoadSchema(entry);

    public async Task ApplyConfigAsync()
    {
        var specs = BuildSpecs();
        await Task.Run(() => Generator.ApplyToDisk(_paths, specs.ToList()));
    }

    public IReadOnlyList<string> ApplyConfig()
    {
        // AstroNvim owns its lazy.nvim setup: per-plugin spec files under
        // lua/plugins/ instead of the nvimmanager bundle (whose bootstrap
        // would otherwise fight AstroNvim's own).
        if (IsAstroNvim)
            return ApplyAstroConfig();
        var specs = BuildSpecs();
        return Generator.ApplyToDisk(_paths, specs.ToList());
    }

    public static string CommitShort(string? commit)
        => string.IsNullOrEmpty(commit) ? "unknown" : commit.Length <= 7 ? commit : commit[..7];

    public void Dispose() { }
}