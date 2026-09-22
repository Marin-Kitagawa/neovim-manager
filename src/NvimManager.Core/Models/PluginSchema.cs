namespace NvimManager.Core.Models;

/// <summary>A typed field definition for the schema-driven config form.</summary>
public sealed record SchemaField(
    string Key,
    string? Title,
    string? Description,
    FieldType Type,
    object? DefaultValue,
    IReadOnlyList<object?>? Options = null,
    IReadOnlyList<SchemaField>? Children = null)
{
    public string Label => string.IsNullOrWhiteSpace(Title) ? Key : Title!;
}

/// <summary>Full configuration schema for one plugin.</summary>
public sealed record PluginSchema(
    string PluginId,
    string? Version,
    IReadOnlyList<SchemaField> Fields,
    string? ExampleLua);