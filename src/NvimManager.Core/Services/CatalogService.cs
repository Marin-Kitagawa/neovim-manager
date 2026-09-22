using System.Reflection;
using System.Text.Json;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>
/// Loads the embedded community catalog and per-plugin schemas, and offers
/// search/filter helpers used by the store UI.
/// </summary>
public sealed class CatalogService
{
    private readonly List<CatalogEntry> _plugins;
    private readonly Dictionary<string, CatalogEntry> _byId;
    private readonly Dictionary<string, string> _schemaCache = new(StringComparer.OrdinalIgnoreCase);

public CatalogService()
    {
        _plugins = LoadCatalog();
        _byId = _plugins.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        AllCategories = ComputeCategories();
    }

    public IReadOnlyList<CatalogEntry> All => _plugins;

    public IReadOnlyList<string> Categories => AllCategories;

    public IReadOnlyList<string> AllCategories { get; }

    IReadOnlyList<string> ComputeCategories()
    {
        var cats = _plugins.Select(p => p.Category).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        var combined = new List<string> { "All" };
        combined.AddRange(cats);
        return combined;
    }

    public IReadOnlyList<CatalogEntry> Search(string? query, string? category, bool featuredOnly = false)
    {
        IEnumerable<CatalogEntry> result = _plugins;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            result = result.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Repo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (category is not null && category != "All")
            result = result.Where(p =>
                string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase) ||
                p.Tags.Contains(category, StringComparer.OrdinalIgnoreCase));

        if (featuredOnly)
            result = result.Where(p => p.Featured);

        return result.ToList();
    }

    public CatalogEntry? GetById(string id)
        => _byId.TryGetValue(id, out var entry) ? entry : null;

    public CatalogEntry? GetByRepo(string repo)
        => _plugins.FirstOrDefault(p => string.Equals(p.Repo, repo, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads a schema by SchemaId. Returns null when the plugin has no bundled schema.</summary>
    public PluginSchema? TryLoadSchema(CatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SchemaId)) return null;

        string id = entry.SchemaId!;
        if (_schemaCache.TryGetValue(id, out var cached))
            return cached == string.Empty ? null : ParseSchema(id, cached);

        string resourceName = $"NvimManager.Core.Catalog.schemas.{id}.json";
        var assembly = typeof(CatalogService).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _schemaCache[id] = string.Empty;
            return null;
        }

        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        var schema = ParseSchema(id, json);
        _schemaCache[id] = json;
        return schema;
    }

    private static PluginSchema ParseSchema(string pluginId, string json)
        => SchemaParser.Parse(pluginId, json);

    private static List<CatalogEntry> LoadCatalog()
    {
        var assembly = typeof(CatalogService).Assembly;
        const string resourceName = "NvimManager.Core.Catalog.community-catalog.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Missing embedded catalog resource: {resourceName}");
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        return ParseCatalog(json);
    }

    internal static List<CatalogEntry> ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("plugins", out var plugins))
            return new List<CatalogEntry>();

        var list = new List<CatalogEntry>();
        foreach (var p in plugins.EnumerateArray())
        {
            string id = Get(p, "id") ?? "";
            string name = Get(p, "name") ?? "";
            string repo = Get(p, "repo") ?? "";
            string description = Get(p, "description") ?? "";
            string category = Get(p, "category") ?? "Other";
            string? homepage = Get(p, "homepage");
            int? stars = p.TryGetProperty("stars", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : null;
            string? schemaId = Get(p, "schemaId");
            string? build = Get(p, "build");
            bool featured = p.TryGetProperty("featured", out var f) && f.ValueKind == JsonValueKind.True;

            list.Add(new CatalogEntry(
                id, name, repo, description, homepage, category,
                ReadStrings(p, "tags"), ReadStrings(p, "dependencies"),
                stars, schemaId, build, featured));
        }
        return list;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement element, string prop)
    {
        if (!element.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList();
    }

    private static string? Get(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}