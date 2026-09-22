namespace NvimManager.Core.Models;

public enum PluginStatus
{
    NotInstalled,
    Installed,
    CheckAvailable,
}

public enum FieldType
{
    Boolean,
    Integer,
    Number,
    String,
    Select,
    MultiSelect,
    StringList,
    NumberList,
    Table,
    Lua,
}