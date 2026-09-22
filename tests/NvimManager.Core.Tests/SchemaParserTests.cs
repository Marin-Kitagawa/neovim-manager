using NvimManager.Core.Models;
using NvimManager.Core.Services;
using Xunit;

namespace NvimManager.Core.Tests;

public class SchemaParserTests
{
    private const string SchemaJson = """
        {
          "type": "object",
          "exampleLua": "opts = {}\n",
          "properties": {
            "enabled": { "type": "boolean", "title": "Enabled", "default": true },
            "count": { "type": "integer", "default": "3" },
            "ratio": { "type": "number", "default": 0.5 },
            "name": { "type": "string" },
            "style": { "type": "select", "enum": ["light", "dark", "auto"], "default": "auto" },
            "tags": { "type": "multiselect", "enum": ["b", "c"] },
            "exts": { "type": "array", "items": { "type": "string" } },
            "big": { "type": "object", "properties": { "size": { "type": "integer", "default": 10 } } },
            "hook": { "type": "lua" }
          }
        }
        """;

    [Fact]
    public void Parses_all_supported_types()
    {
        var schema = SchemaParser.Parse("demo", SchemaJson);

        var byKey = schema.Fields.ToDictionary(f => f.Key);

        Assert.Equal(FieldType.Boolean, byKey["enabled"].Type);
        Assert.Equal(true, byKey["enabled"].DefaultValue);
        Assert.Equal("Enabled", byKey["enabled"].Label);

        Assert.Equal(FieldType.Integer, byKey["count"].Type);
        Assert.Equal(3L, byKey["count"].DefaultValue);

        Assert.Equal(FieldType.Number, byKey["ratio"].Type);

        Assert.Equal(FieldType.String, byKey["name"].Type);

        Assert.Equal(FieldType.Select, byKey["style"].Type);
        Assert.Equal(3, byKey["style"].Options!.Count);

        Assert.Equal(FieldType.MultiSelect, byKey["tags"].Type);
        Assert.Equal(2, byKey["tags"].Options!.Count);

        Assert.Equal(FieldType.StringList, byKey["exts"].Type);

        var big = byKey["big"];
        Assert.Equal(FieldType.Table, big.Type);
        Assert.Equal(1, big.Children!.Count);
        Assert.Equal(10L, big.Children[0].DefaultValue);

        Assert.Equal(FieldType.Lua, byKey["hook"].Type);
    }

    [Fact]
    public void Reads_example_lua()
    {
        var schema = SchemaParser.Parse("demo", SchemaJson);
        Assert.Contains("opts = {}", schema.ExampleLua);
    }
}