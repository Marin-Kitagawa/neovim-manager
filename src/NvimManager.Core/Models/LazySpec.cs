using NvimManager.Core.Lua;

namespace NvimManager.Core.Models;

/// <summary>
/// A lazy.nvim plugin spec that the GUI builds and the generator renders.
/// Repo-less properties (branch, events, commands, ...) map 1:1 to lazy spec keys.
/// </summary>
public sealed class LazySpec
{
    public string Repo { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public string? Branch { get; set; }
    public string? Version { get; set; }
    public int? Priority { get; set; }
    public bool? Lazy { get; set; }

    public IReadOnlyList<string> Events { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Commands { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FileTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();

    public string? Keys { get; set; }
    public string? Build { get; set; }
    public bool Dev { get; set; }

    /// <summary>Structured opts table (built from the schema form) or a RawLua override.</summary>
    public object? Opts { get; set; }

    /// <summary>Raw Lua body for the <c>config</c> function, if the user overrode it.</summary>
    public string? Config { get; set; }

    /// <summary>Raw Lua, emitted as the <c>init</c> hook (ran before the plugin loads).</summary>
    public string? Init { get; set; }
}