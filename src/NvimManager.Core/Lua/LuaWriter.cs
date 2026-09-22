using System.Text;

namespace NvimManager.Core.Lua;

/// <summary>
/// A Lua table, represented as an insertion-ordered key/value map.
/// Values must be one of: bool, long, double, string, IReadOnlyList&lt;object?&gt; (array),
/// RawLua, or a nested LuaTable.
/// </summary>
public sealed class LuaTable : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _entries = new();

    public IReadOnlyList<KeyValuePair<string, object?>> Entries => _entries;

    public object? this[string key]
    {
        get
        {
            foreach (var (k, v) in _entries)
            {
                if (k == key) return v;
            }
            return null;
        }
        set => Set(key, value);
    }

    public LuaTable Set(string key, object? value)
    {
        int index = _entries.FindIndex(e => e.Key == key);
        if (index >= 0) _entries[index] = new(key, value);
        else _entries.Add(new(key, value));
        return this;
    }

    public LuaTable GetOrAddTable(string key)
    {
        var existing = this[key];
        if (existing is LuaTable t) return t;
        var table = new LuaTable();
        Set(key, table);
        return table;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _entries.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _entries.GetEnumerator();
}

/// <summary>Marks a value that must be emitted verbatim as Lua source (not escaped).</summary>
public sealed record RawLua(string Code);

/// <summary>
/// Serializes .NET values into Lua source. Identifiers are emitted as-is; keys with
/// spaces or special characters are emitted with the ["odd key"] syntax.
/// </summary>
public static class LuaWriter
{
    public static string Serialize(object? value, int indent = 0)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent);
        return sb.ToString();
    }

    public static string WriteIdentifier(string key)
        => IsIdentifier(key) ? key : LuaWriterLiteral(key);

    public static bool IsIdentifier(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        char c0 = key[0];
        if (!(char.IsLetter(c0) || c0 == '_')) return false;
        for (int i = 1; i < key.Length; i++)
        {
            char c = key[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

    private static string LuaWriterLiteral(string s)
        => "[" + QuoteString(s) + "]";

    public static string QuoteString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? value, int indent)
    {
        switch (value)
        {
            case null:
                sb.Append("nil");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case long l:
                sb.Append(l.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case int i:
                sb.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(FormatNumber(d));
                break;
            case RawLua raw:
                sb.Append(raw.Code);
                break;
            case string s:
                sb.Append(QuoteString(s));
                break;
            case LuaTable table:
                WriteTable(sb, table, indent);
                break;
            case System.Collections.IEnumerable enumerable:
                WriteArray(sb, enumerable.Cast<object?>(), indent);
                break;
            default:
                sb.Append(QuoteString(value.ToString() ?? ""));
                break;
        }
    }

    private static string FormatNumber(double d)
        => d == Math.Floor(d)
            ? d.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteArray(StringBuilder sb, IEnumerable<object?> items, int indent)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append("{\n");
        string pad = new string(' ', indent + 2);
        foreach (var item in list)
        {
            sb.Append(pad);
            Write(sb, item, indent + 2);
            sb.Append(",\n");
        }
        sb.Append(Pad(indent)).Append('}');
    }

    private static void WriteTable(StringBuilder sb, LuaTable table, int indent)
    {
        if (table.Entries.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append("{\n");
        foreach (var (key, val) in table.Entries)
        {
            sb.Append(Pad(indent + 2));
            sb.Append(WriteIdentifier(key));
            sb.Append(" = ");
            Write(sb, val, indent + 2);
            sb.Append(",\n");
        }
        sb.Append(Pad(indent)).Append('}');
    }

    private static string Pad(int indent) => new(' ', indent);
}