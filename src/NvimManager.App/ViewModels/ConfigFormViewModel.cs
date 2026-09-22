using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NvimManager.Core.Models;

namespace NvimManager.App.ViewModels;

/// <summary>
/// Renders a plugin's schema into an editable form and (re)builds the typed
/// opts dictionary that the config generator consumes.
/// </summary>
public sealed partial class ConfigFormViewModel : ViewModelBase
{
    [ObservableProperty] private string _repo = string.Empty;
    [ObservableProperty] private string _formHint = string.Empty;
    [ObservableProperty] private bool _hasSchema;
    [ObservableProperty] private string _rawOptsLua = string.Empty;

    public ObservableCollection<FieldViewModel> Fields { get; } = new();

    public void Load(CatalogEntry entry, PluginSchema? schema, PluginSettings? settings)
    {
        Repo = entry.Repo;
        Fields.Clear();

        if (schema is not null && schema.Fields.Count > 0)
        {
            HasSchema = true;
            FormHint = $"Configured via the bundled schema for {entry.Name}. Every value maps into the plugin's opts table.";
            settings ??= new PluginSettings();
            settings.Opts ??= new Dictionary<string, object?>();
            var opts = settings.Opts;
            foreach (var field in schema.Fields)
            {
                var vm = Factory.Create(field);
                if (opts.TryGetValue(field.Key, out var persisted))
                    vm.SetInitialValue(persisted);
                else
                    vm.SetInitialValue(field.DefaultValue);
                Fields.Add(vm);
            }
        }
        else
        {
            HasSchema = false;
            FormHint = $"No bundled schema for {entry.Name}. Use the raw Lua editor below to configure its opts table.";
        }

        RawOptsLua = string.IsNullOrWhiteSpace(settings?.RawOptsLua) ? string.Empty : settings!.RawOptsLua!;
    }

    public void ApplyRaw(string? raw)
    {
        if (raw is not null)
            RawOptsLua = raw;
    }

    /// <summary>Builds the typed value tree from the form state.</summary>
    public Dictionary<string, object?> BuildValues()
    {
        var values = new Dictionary<string, object?>();
        foreach (var field in Fields)
        {
            var value = field.ToConfigValue();
            if (value is not null)
                values[field.Field.Key] = value;
        }
        return values;
    }
}