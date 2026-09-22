using System.Text.Json;
using System.Text.Json.Serialization;

namespace NvimManager.Core.Models;

/// <summary>
/// Persisted per-plugin configuration. Values inside <see cref="Opts"/> are one of:
/// bool, long, double, string, List&lt;object?&gt;, Dictionary&lt;string,object?&gt;.
/// </summary>
public sealed class PluginSettings
{
    public string Repo { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool? Lazy { get; set; }
    public string? Version { get; set; }
    public string? Branch { get; set; }
    public int? Priority { get; set; }
    public List<string> Events { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<string> FileTypes { get; set; } = new();
    public string? Keys { get; set; }
    public string? Build { get; set; }
    public bool Dev { get; set; }

    /// <summary>Structured opts values captured from the schema form.</summary>
    public Dictionary<string, object?>? Opts { get; set; }

    /// <summary>Set when the user chose the raw Lua editor instead of the form.</summary>
    public string? RawOptsLua { get; set; }
    public string? Config { get; set; }
    public string? Init { get; set; }
}

/// <summary>Converts JSON numbers/arrays/objects to long/double/List/Dictionary when deserializing.</summary>
public sealed class JsonValueConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Null: return null;
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l)) return l;
                return reader.GetDouble();
            case JsonTokenType.StartArray:
                var list = new List<object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    list.Add(Read(ref reader, typeToConvert, options));
                return list;
            case JsonTokenType.StartObject:
                var map = new Dictionary<string, object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string key = reader.GetString()!;
                        reader.Read();
                        map[key] = Read(ref reader, typeToConvert, options);
                    }
                }
                return map;
            default:
                return JsonDocument.ParseValue(ref reader).RootElement.Clone();
        }
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case long l: writer.WriteNumberValue(l); break;
            case int i: writer.WriteNumberValue(i); break;
            case double d: writer.WriteNumberValue(d); break;
            case string s: writer.WriteStringValue(s); break;
            case IEnumerable<object?> enumerable: writer.WriteStartArray(); foreach (var item in enumerable) Write(writer, item, options); writer.WriteEndArray(); break;
            case IDictionary<string, object?> dict: writer.WriteStartObject(); foreach (var kv in dict) { writer.WritePropertyName(kv.Key); Write(writer, kv.Value, options); } writer.WriteEndObject(); break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }
}