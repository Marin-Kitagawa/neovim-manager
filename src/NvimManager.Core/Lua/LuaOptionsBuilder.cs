using NvimManager.Core.Models;

namespace NvimManager.Core.Lua;

/// <summary>
/// Converts the typed value tree produced by the schema form (bool, long, double,
/// string, List&lt;object?&gt;, Dictionary&lt;string,object?&gt;) into Lua expressions.
/// </summary>
public static class LuaOptionsBuilder
{
    public static LuaTable? Build(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null || values.Count == 0) return null;
        var table = new LuaTable();
        foreach (var (key, value) in values)
        {
            object? lua = ToLua(value);
            table.Set(key, lua);
        }
        return table;
    }

    public static object? ToLua(object? value)
    {
        switch (value)
        {
            case null: return null;
            case bool: return value;
            case long: return value;
            case int: return value;
            case double: return value;
            case float f: return (double)f;
            case string s: return s;
            case RawLua raw: return raw;
            case Dictionary<string, object?> map:
                var table = new LuaTable();
                foreach (var (k, v) in map) table.Set(k, ToLua(v));
                if (table.Entries.Count == 0) return null;
                return table;
            case IEnumerable<object?> list:
                var result = new List<object?>();
                foreach (var item in list)
                {
                    object? converted = ToLua(item);
                    if (converted is not null || item is null)
                        result.Add(converted);
                }
                if (result.Count == 0) return new List<object?>();
                return result;
            default:
                return value.ToString();
        }
    }
}