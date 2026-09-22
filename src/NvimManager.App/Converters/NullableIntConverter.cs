using System.Globalization;
using Avalonia.Data.Converters;

namespace NvimManager.App.Converters;

/// <summary>Bridges int? view-model values and the decimal? used by NumericUpDown.</summary>
public sealed class NullableIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => null,
            int i => (decimal)i,
            long l => (decimal)l,
            _ => null,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => null,
            decimal d => (int)d,
            _ => null,
        };
}