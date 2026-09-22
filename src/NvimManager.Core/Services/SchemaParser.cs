using System.Text.Json;
using NvimManager.Core.Models;

namespace NvimManager.Core.Services;

/// <summary>
/// Parses per-plugin config schemas. Accepts a JSON-Schema-like document:
/// <code>
/// { "type": "object", "properties": {
///     "icons":    { "type": "boolean", "title": "Icons", "default": true },
///     "bg":       { "type": "select",  "enum": ["light", "dark"], "default": "dark" },
///     "style":    { "type": "string",  "description": "..." },
///     "exts":     { "type": "array",   "items": { "type": "string" } },
///     "kind":     { "type": "lua",     "default": "..." },
///     "child":    { "type": "object",  "properties": {...} }
/// } }
/// </code>
/// Additional types: select, multiselect, lua.
/// </summary>
public static class SchemaParser
{
    public static PluginSchema Parse(string pluginId, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? version = null;
        if (root.TryGetProperty("$schemaVersion", out var ver)) version = ver.ToString();

        string? exampleLua = null;
        if (root.TryGetProperty("exampleLua", out var ex)) exampleLua = ex.GetString();

        var fields = new List<SchemaField>();
        if (root.TryGetProperty("fields", out var fieldsProp))
        {
            foreach (var prop in fieldsProp.EnumerateObject())
                fields.Add(ParseField(prop.Name, prop.Value));
        }
        else if (root.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateObject())
                fields.Add(ParseField(prop.Name, prop.Value));
        }

        return new PluginSchema(pluginId, version, fields, exampleLua);
    }

    private static SchemaField ParseField(string key, JsonElement element)
    {
        string? title = GetString(element, "title") ?? GetString(element, "name");
        string? description = GetString(element, "description") ?? GetString(element, "desc");

        object? defaultValue = element.TryGetProperty("default", out var def) ? ConvertDefault(def) : null;

        string typeName = (GetString(element, "type") ?? "string").ToLowerInvariant();
        IReadOnlyList<object?>? options = null;
        List<SchemaField>? children = null;

        switch (typeName)
        {
            case "bool":
            case "boolean":
                return new SchemaField(key, title, description, FieldType.Boolean, Coerce(typeName, defaultValue), null, null);

            case "int":
            case "integer":
                return new SchemaField(key, title, description, FieldType.Integer, Coerce(typeName, defaultValue), null, null);

            case "number":
            case "float":
                return new SchemaField(key, title, description, FieldType.Number, Coerce(typeName, defaultValue), null, null);

            case "select":
            case "stringenum":
                options = ReadOptions(element, "enum");
                return new SchemaField(key, title, description, FieldType.Select, defaultValue, options, null);

            case "multiselect":
                options = ReadOptions(element, "enum");
                return new SchemaField(key, title, description, FieldType.MultiSelect, defaultValue, options, null);

            case "array":
            case "list":
                var items = element.TryGetProperty("items", out var itemsElem) ? itemsElem : default;
                if (items.ValueKind == JsonValueKind.Object)
                {
                    options = ReadOptions(items, "enum");
                    string itemType = GetString(items, "type") ?? "string";
                    if (options is { Count: > 0 } )
                        return new SchemaField(key, title, description, FieldType.MultiSelect, defaultValue, options, null);
                    return new SchemaField(key, title, description,
                        itemType switch { "number" or "integer" => FieldType.NumberList, _ => FieldType.StringList },
                        defaultValue, null, null);
                }
                return new SchemaField(key, title, description, FieldType.StringList, defaultValue, null, null);

            case "object":
            case "table":
                children = new List<SchemaField>();
                if (element.TryGetProperty("properties", out var childProps))
                {
                    foreach (var prop in childProps.EnumerateObject())
                        children.Add(ParseField(prop.Name, prop.Value));
                }
                else if (element.TryGetProperty("fields", out var childFields))
                {
                    foreach (var prop in childFields.EnumerateObject())
                        children.Add(ParseField(prop.Name, prop.Value));
                }
                return new SchemaField(key, title, description, FieldType.Table, defaultValue, null, children);

            case "lua":
            case "luastring":
            case "raw":
            case "code":
                return new SchemaField(key, title, description, FieldType.Lua, ConvertDefault(def), null, null);

            default:
                return new SchemaField(key, title, description, FieldType.String, defaultValue, null, null);
        }
    }

    private static object? Coerce(string typeName, object? value)
    {
        if (value is string s && typeName is "integer" or "int")
        {
            if (long.TryParse(s, out var l)) return l;
        }
        return value;
    }

    private static object? ConvertDefault(JsonElement def)
    {
        switch (def.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number:
                if (def.TryGetInt64(out var l)) return l;
                if (def.TryGetDouble(out var d)) return d;
                return null;
            case JsonValueKind.String: return def.GetString();
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in def.EnumerateArray()) list.Add(ConvertDefault(item));
                return list;
            case JsonValueKind.Object:
                var map = new Dictionary<string, object?>();
                foreach (var prop in def.EnumerateObject()) map[prop.Name] = ConvertDefault(prop.Value);
                return map;
            default: return null;
        }
    }

    private static IReadOnlyList<object?>? ReadOptions(JsonElement element, string propName)
    {
        if (!element.TryGetProperty(propName, out var enumElem) || enumElem.ValueKind != JsonValueKind.Array)
            return null;
        var result = new List<object?>();
        foreach (var item in enumElem.EnumerateArray()) result.Add(item.GetString());
        return result;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}