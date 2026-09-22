using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NvimManager.Core.Lua;
using NvimManager.Core.Models;

namespace NvimManager.App.ViewModels;

/// <summary>Base class for one row of the schema-driven config form.</summary>
public abstract partial class FieldViewModel : ViewModelBase
{
    protected FieldViewModel(SchemaField field)
    {
        Field = field;
    }

    public SchemaField Field { get; }
    public string Label => Field.Label;
    public string? Description => Field.Description;

    /// <summary>Restores the control value from a persisted value (or schema default).</summary>
    public abstract void SetInitialValue(object? value);

    /// <summary>Converts the control state into a typed config value for settings/generation.</summary>
    public abstract object? ToConfigValue();
}

public sealed partial class BooleanFieldViewModel : FieldViewModel
{
    [ObservableProperty] private bool _value;

    public BooleanFieldViewModel(SchemaField field) : base(field)
    {
        Value = field.DefaultValue is true;
    }

    public override void SetInitialValue(object? value)
    {
        if (value is bool b) Value = b;
        else if (value is long l) Value = l != 0;
    }

    public override object? ToConfigValue() => Value;
}

public abstract partial class NumericFieldViewModel : FieldViewModel
{
    [ObservableProperty] private decimal? _value;

    protected NumericFieldViewModel(SchemaField field) : base(field)
    {
        Value = Translate(field.DefaultValue);
    }

    private static decimal? Translate(object? value) => value switch
    {
        null => null,
        long l => l,
        int i => i,
        double d => (decimal)d,
        string s when decimal.TryParse(s, out var d) => d,
        bool b => b ? 1m : 0m,
        _ => null,
    };

    public override void SetInitialValue(object? value) => Value = Translate(value);
}

public sealed class IntegerFieldViewModel : NumericFieldViewModel
{
    public IntegerFieldViewModel(SchemaField field) : base(field) { }
    public override object? ToConfigValue() => Value.HasValue ? (long)Value.Value : null;
}

public sealed class NumberFieldViewModel : NumericFieldViewModel
{
    public NumberFieldViewModel(SchemaField field) : base(field) { }
    public override object? ToConfigValue() => Value.HasValue ? (double)Value.Value : null;
}

public sealed partial class StringFieldViewModel : FieldViewModel
{
    [ObservableProperty] private string _value = string.Empty;

    public StringFieldViewModel(SchemaField field) : base(field)
    {
        Value = field.DefaultValue as string ?? Normalize(field.DefaultValue);
    }

    private static string Normalize(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public override void SetInitialValue(object? value) => Value = Normalize(value);
    public override object? ToConfigValue() => string.IsNullOrEmpty(Value) ? null : Value;
}

public sealed partial class SelectFieldViewModel : FieldViewModel
{
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] private string? _value;

    public SelectFieldViewModel(SchemaField field) : base(field)
    {
        Options = field.Options?.Select(o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToList()
                  ?? new List<string>();
        Value = field.DefaultValue as string ?? Options.FirstOrDefault();
    }

    public override void SetInitialValue(object? value)
    {
        var s = value as string;
        Value = s is not null && Options.Contains(s) ? s : Options.FirstOrDefault();
    }

    public override object? ToConfigValue() => string.IsNullOrEmpty(Value) ? null : Value;
}

public sealed partial class MultiSelectFieldViewModel : FieldViewModel
{
    public sealed partial class OptionItem : ObservableObject
    {
        public string Label { get; }

        public OptionItem(string label)
        {
            Label = label;
        }

        [ObservableProperty] private bool _isChecked;
    }

    public ObservableCollection<OptionItem> Items { get; } = new();

    public MultiSelectFieldViewModel(SchemaField field) : base(field)
    {
        foreach (var option in field.Options ?? Array.Empty<object?>())
            Items.Add(new OptionItem(Convert.ToString(option, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
    }

    public override void SetInitialValue(object? value)
    {
        var selected = value as IEnumerable<object?> ?? value switch
        {
            null => null,
            _ => SplitLines(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
        };
        var set = new HashSet<string>(selected?.Select(o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) ?? Array.Empty<string>());
        foreach (var item in Items)
            item.IsChecked = set.Contains(item.Label);
    }

    public override object? ToConfigValue()
    {
        var selected = Items.Where(i => i.IsChecked).Select(i => (object)i.Label).ToList();
        return selected.Count == 0 ? null : selected;
    }

    private static IEnumerable<object?> SplitLines(string? text)
        => (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed partial class StringListFieldViewModel : FieldViewModel
{
    [ObservableProperty] private string _value = string.Empty;

    public StringListFieldViewModel(SchemaField field) : base(field)
    {
        Value = ReadLines(field.DefaultValue);
    }

    public override void SetInitialValue(object? value) => Value = ReadLines(value);

    public override object? ToConfigValue()
    {
        var lines = Lines();
        return lines.Count == 0 ? null : lines;
    }

    private List<object?> Lines()
        => Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(l => (object?)l)
                 .ToList();

    private static string ReadLines(object? value)
    {
        var items = value switch
        {
            IEnumerable<object?> list => list.ToList(),
            null => new List<object?>(),
            _ => new List<object?> { value },
        };
        return string.Join('\n', items.Select(o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
    }
}

public sealed partial class NumberListFieldViewModel : FieldViewModel
{
    [ObservableProperty] private string _value = string.Empty;

    public NumberListFieldViewModel(SchemaField field) : base(field)
    {
        SetInitialValue(field.DefaultValue);
    }

    public override void SetInitialValue(object? value)
    {
        var items = value switch
        {
            IEnumerable<object?> list => list.ToList(),
            null => new List<object?>(),
            _ => new List<object?> { value },
        };
        Value = string.Join('\n', items.Select(o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
    }

    public override object? ToConfigValue()
    {
        var list = new List<object?>();
        foreach (var line in Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(line, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                list.Add(d == Math.Floor(d) ? (object)(long)d : d);
        }
        return list.Count == 0 ? null : list;
    }
}

public sealed partial class TableFieldViewModel : FieldViewModel
{
    public ObservableCollection<FieldViewModel> Children { get; } = new();

    public TableFieldViewModel(SchemaField field) : base(field)
    {
        foreach (var child in field.Children ?? Array.Empty<SchemaField>())
            Children.Add(Factory.Create(child));
    }

    public override void SetInitialValue(object? value)
    {
        var map = value as IEnumerable<KeyValuePair<string, object?>>;
        if (map is null)
        {
            foreach (var child in Children)
                child.SetInitialValue(child.Field.DefaultValue);
            return;
        }
        var dict = map.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var child in Children)
        {
            if (dict.TryGetValue(child.Field.Key, out var v))
                child.SetInitialValue(v);
            else
                child.SetInitialValue(child.Field.DefaultValue);
        }
    }

    public override object? ToConfigValue()
    {
        var map = new Dictionary<string, object?>();
        foreach (var child in Children)
        {
            var value = child.ToConfigValue();
            if (value is not null)
                map[child.Field.Key] = value;
        }
        return map.Count == 0 ? null : map;
    }
}

public sealed partial class LuaFieldViewModel : FieldViewModel
{
    public static string EmbeddedMarker = "--NVMANAGER_RAW--";

    [ObservableProperty] private string _value = string.Empty;

    public LuaFieldViewModel(SchemaField field) : base(field)
    {
        Value = ReadRaw(field.DefaultValue);
    }

    public override void SetInitialValue(object? value) => Value = ReadRaw(value);

    public override object? ToConfigValue() => string.IsNullOrWhiteSpace(Value) ? null : new RawLua(Value);

    private static string ReadRaw(object? value) => value switch
    {
        RawLua raw => raw.Code,
        string s => s.StartsWith(EmbeddedMarker, StringComparison.Ordinal) ? s[EmbeddedMarker.Length..] : s,
        null => string.Empty,
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

public static class Factory
{
    public static FieldViewModel Create(SchemaField field) => field.Type switch
    {
        FieldType.Boolean => new BooleanFieldViewModel(field),
        FieldType.Integer => new IntegerFieldViewModel(field),
        FieldType.Number => new NumberFieldViewModel(field),
        FieldType.String => new StringFieldViewModel(field),
        FieldType.Select => new SelectFieldViewModel(field),
        FieldType.MultiSelect => new MultiSelectFieldViewModel(field),
        FieldType.StringList => new StringListFieldViewModel(field),
        FieldType.NumberList => new NumberListFieldViewModel(field),
        FieldType.Table => new TableFieldViewModel(field),
        FieldType.Lua => new LuaFieldViewModel(field),
        _ => new StringFieldViewModel(field),
    };
}