namespace NvimManager.Core.Models;

/// <summary>One entry in the community store catalog.</summary>
public sealed record CatalogEntry(
    string Id,
    string Name,
    string Repo,
    string Description,
    string? Homepage,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies,
    int? Stars,
    string? SchemaId,
    string? Build = null,
    bool Featured = false)
{
    public string Owner => Repo.Split('/', 2)[0];
    public string ProjectName => Repo.Split('/', 2)[1];
    public string CloneUrl => $"https://github.com/{Repo}.git";
    public string HomepageOrRepo => Homepage ?? $"https://github.com/{Repo}";
}