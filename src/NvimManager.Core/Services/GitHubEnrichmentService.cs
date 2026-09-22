using System.Net.Http.Headers;
using System.Text.Json;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>
/// Enriches catalog entries with live GitHub data (stars, description).
/// Cached on disk for a TTL so we stay well under unauthenticated rate limits.
/// </summary>
public sealed class GitHubEnrichmentService : IDisposable
{
    private const string UserAgent = "NvimManager/1.0";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private sealed record CacheEntry(int? Stars, string? Description, DateTime FetchedUtc);

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private Dictionary<string, CacheEntry> _cache;

    public GitHubEnrichmentService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NvimManager", "1"));
        _http.Timeout = TimeSpan.FromSeconds(15);
        _cachePath = Path.Combine(NeovimLocator.AppDataDir(), "github-cache.json");
        _cache = Load(_cachePath) ?? new Dictionary<string, CacheEntry>();
    }

    /// <summary>Returns the entry with live data if obtainable, otherwise with cached/bundled data.</summary>
    public async Task<CatalogEntry> EnrichAsync(CatalogEntry entry, bool force = false)
    {
        if (_cache.TryGetValue(entry.Repo, out var cached) && !force && DateTime.UtcNow - cached.FetchedUtc < Ttl)
            return Apply(entry, cached);

        try
        {
            using var response = await _http.GetAsync($"https://api.github.com/repos/{entry.Repo}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                int? stars = root.TryGetProperty("stargazers_count", out var s) && s.TryGetInt32(out var si) ? si : null;
                string? desc = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var fresh = new CacheEntry(stars, desc, DateTime.UtcNow);
                UpdateCache(entry.Repo, fresh);
                return Apply(entry, fresh);
            }
        }
        catch
        {
            // offline or rate-limited; fall back to the bundled numbers
        }

        return Apply(entry, cached!);
    }

    private static CatalogEntry Apply(CatalogEntry entry, CacheEntry? cached)
    {
        if (cached is null) return entry;
        return entry with
        {
            Stars = cached.Stars ?? entry.Stars,
            Description = string.IsNullOrWhiteSpace(cached.Description) ? entry.Description : cached.Description!,
        };
    }

    public void Dispose() => _http.Dispose();

    private void UpdateCache(string repo, CacheEntry value)
    {
        _cache[repo] = value;
        Save(_cachePath, _cache);
    }

    private static Dictionary<string, CacheEntry>? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(stream);
            if (dict is null) return null;
            var stale = dict.Where(kv => DateTime.UtcNow - kv.Value.FetchedUtc > Ttl).Select(kv => kv.Key).ToList();
            foreach (var k in stale) dict.Remove(k);
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(string path, Dictionary<string, CacheEntry> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, cache);
        }
        catch
        {
            // a cache write must never break the app
        }
    }
}