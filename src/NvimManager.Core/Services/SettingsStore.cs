using System.Text.Json.Serialization;
using System.Text.Json;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>Loads and saves per-plugin settings to the app data directory.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonValueConverter() },
    };

    private readonly string _path;
    private Dictionary<string, PluginSettings> _settings;

    public SettingsStore()
    {
        _path = Path.Combine(NeovimLocator.AppDataDir(), "settings.json");
        _settings = Load(_path);
    }

    public IReadOnlyDictionary<string, PluginSettings> All => _settings;

    public PluginSettings GetOrCreate(string repo)
    {
        if (!_settings.TryGetValue(repo, out var settings))
        {
            settings = new PluginSettings { Repo = repo };
            _settings[repo] = settings;
        }
        return settings;
    }

    public bool TryGet(string repo, out PluginSettings? settings)
        => _settings.TryGetValue(repo, out settings);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, _settings, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save settings to {_path}: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, PluginSettings> Load(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, PluginSettings>();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Dictionary<string, PluginSettings>>(stream, JsonOptions)
                   ?? new Dictionary<string, PluginSettings>();
        }
        catch
        {
            return new Dictionary<string, PluginSettings>();
        }
    }
}