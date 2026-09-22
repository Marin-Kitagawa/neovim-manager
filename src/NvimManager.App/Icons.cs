using Avalonia;
using Avalonia.Media;

namespace NvimManager.App;

/// <summary>Authored vector icons, resolved from the glass theme resources.</summary>
internal static class Icons
{
    public static StreamGeometry Star => Get("Icon.Star");
    public static StreamGeometry Heart => Get("Icon.Heart");
    public static StreamGeometry BoxChecked => Get("Icon.BoxChecked");
    public static StreamGeometry Sliders => Get("Icon.Sliders");

    private static StreamGeometry Get(string key)
        => (StreamGeometry)Application.Current!.Resources[key]!;
}