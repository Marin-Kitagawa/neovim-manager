namespace NvimManager.Core.Models;

/// <summary>An installed plugin directory (inside the lazy root) plus its git state.</summary>
public sealed record InstalledPlugin(
    string Repo,
    string Directory,
    string? Commit,
    string? ShortCommit,
    string? Tag,
    string? LatestCommit = null)
{
    public bool HasUpdate => LatestCommit is not null && !string.Equals(LatestCommit, Commit, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Resolved neovim config/data paths for this machine.</summary>
public sealed record NvimPaths(
    string ConfigDir,
    string DataDir,
    string LazyRoot,
    string InitLuaPath,
    string PluginsLuaPath,
    string ModulePath,
    string LockPath)
{
    public override string ToString()
        => $"Config: {ConfigDir}\nData:   {DataDir}\nLazy:   {LazyRoot}";
}