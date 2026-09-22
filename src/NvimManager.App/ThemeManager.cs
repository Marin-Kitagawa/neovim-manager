using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using NvimManager.Core.Services;

namespace NvimManager.App;

/// <summary>
/// Swappable cozy palettes, persisted to app.json.
/// Palettes live in Themes.axaml as ThemeDictionaries ("Cream"/"Plum"/"Pastel"/"Sage");
/// switching RequestedThemeVariant re-resolves every DynamicResource app-wide.
/// </summary>
public static class ThemeManager
{
    public static readonly IReadOnlyList<string> ThemeNames =
        ["Cream", "Midnight Plum", "Blush Pastel", "Garden Sage"];

    // NOTE: these must be declared BEFORE Variants below — C# runs static
    // initializers in textual order, and the dictionary reads these properties.
    public static ThemeVariant CreamVariant { get; } = new ThemeVariant("Cream", ThemeVariant.Light);
    public static ThemeVariant PlumVariant { get; } = new ThemeVariant("Plum", ThemeVariant.Dark);
    public static ThemeVariant PastelVariant { get; } = new ThemeVariant("Pastel", ThemeVariant.Light);
    public static ThemeVariant SageVariant { get; } = new ThemeVariant("Sage", ThemeVariant.Light);

    private static readonly Dictionary<string, ThemeVariant> Variants = new()
    {
        ["Cream"] = CreamVariant,
        ["Midnight Plum"] = PlumVariant,
        ["Blush Pastel"] = PastelVariant,
        ["Garden Sage"] = SageVariant,
    };

    private static readonly string PrefsPath = Path.Combine(NeovimLocator.AppDataDir(), "app.json");

    private static Application? _app;

    public static string CurrentName { get; private set; } = "Cream";

    public static bool IsKnown(string? name) => name is not null && Variants.ContainsKey(name);

    public static void Initialize(Application app)
    {
        _app = app;
        ApplyTheme(Load(), save: false);
    }

    public static void ApplyTheme(string name, bool save = true)
    {
        if (!Variants.TryGetValue(name, out var variant))
        {
            name = "Cream";
            variant = Variants[name];
        }
        CurrentName = name;

        var app = _app ?? Application.Current;
        if (app is not null)
            app.RequestedThemeVariant = variant;

        if (save) Save();
    }

    private static string Load()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return "Cream";
            using var doc = JsonDocument.Parse(File.ReadAllText(PrefsPath));
            if (doc.RootElement.TryGetProperty("Theme", out var theme)
                && theme.ValueKind == JsonValueKind.String
                && IsKnown(theme.GetString()))
                return theme.GetString()!;
        }
        catch
        {
            // Corrupt prefs must never block startup.
        }
        return "Cream";
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            File.WriteAllText(PrefsPath, JsonSerializer.Serialize(new { Theme = CurrentName }));
        }
        catch
        {
            // Persisting the theme is best-effort.
        }
    }

}