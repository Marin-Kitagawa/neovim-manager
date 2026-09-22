using NvimManager.Core.Lua;
using Xunit;

namespace NvimManager.Core.Tests;

public class LuaWriterTests
{
    [Fact]
    public void Serializes_primitive_values()
    {
        Assert.Equal("true", LuaWriter.Serialize(true));
        Assert.Equal("42", LuaWriter.Serialize(42L));
        Assert.Equal("42.5", LuaWriter.Serialize(42.5));
        Assert.Equal("\"hello\"", LuaWriter.Serialize("hello"));
        Assert.Equal("nil", LuaWriter.Serialize(null));
    }

    [Fact]
    public void Escapes_strings()
    {
        Assert.Equal("\"a\\\"b\"", LuaWriter.Serialize("a\"b"));
        Assert.Equal("\"a\\nb\"", LuaWriter.Serialize("a\nb"));
    }

    [Fact]
    public void Emits_identifier_keys_directly()
    {
        var table = new LuaTable().Set("theme", "night").Set("icons_enabled", true);
        string text = LuaWriter.Serialize(table);

        Assert.Contains("theme = \"night\"", text);
        Assert.Contains("icons_enabled = true", text);
    }

    [Fact]
    public void Brackets_non_identifier_keys()
    {
        var table = new LuaTable();
        table["key with space"] = true;
        string text = LuaWriter.Serialize(table);

        Assert.Contains("[\"key with space\"] = true", text);
    }

    [Fact]
    public void Empty_table_is_inline()
    {
        Assert.Equal("{}", LuaWriter.Serialize(new LuaTable()));
    }

    [Fact]
    public void Nested_table_and_array_render()
    {
        var inner = new LuaTable().Set("enable", true);
        var outer = new LuaTable().Set("highlight", inner).Set("ignore", new List<object?> { "lua", "js" });

        string text = LuaWriter.Serialize(outer);

        Assert.Contains("highlight = {", text);
        Assert.Contains("enable = true", text);
        Assert.Contains("ignore = {", text);
        Assert.Contains("\"lua\"", text);
        Assert.Contains("\"js\"", text);
    }

    [Fact]
    public void RawLua_passes_through()
    {
        Assert.Equal("vim.g.markdown_recommended_style = 0", LuaWriter.Serialize(new RawLua("vim.g.markdown_recommended_style = 0")));
    }
}