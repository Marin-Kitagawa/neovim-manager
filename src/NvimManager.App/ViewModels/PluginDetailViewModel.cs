using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Models;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

public sealed partial class PluginDetailViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _goBack;

    public PluginDetailViewModel(AppServices services, CatalogEntry entry, Action goBack)
    {
        _services = services;
        _goBack = goBack;
        Entry = entry;

        _name = entry.Name;
        _repo = entry.Repo;
        _description = entry.Description;
        _starsText = FormatStars(entry.Stars);
        _tagsText = string.Join(" · ", entry.Tags);
        _category = entry.Category;
        _statusText = "Ready.";

        Config.Load(entry, services.Store.TryLoadSchema(entry), services.Settings.GetOrCreate(entry.Repo));
        LoadFromSettings(services.Settings.GetOrCreate(entry.Repo));
        _ = Task.Run(RefreshInstalledAsync);
    }

    public CatalogEntry Entry { get; }
    public ConfigFormViewModel Config { get; } = new();

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _repo;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _starsText;
    [ObservableProperty] private string _tagsText;
    [ObservableProperty] private string _category;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool? _lazy;
    [ObservableProperty] private string? _version;
    [ObservableProperty] private string? _branch;
    [ObservableProperty] private int? _priority;
    [ObservableProperty] private string _events = string.Empty;
    [ObservableProperty] private string _commands = string.Empty;
    [ObservableProperty] private string _fileTypes = string.Empty;
    [ObservableProperty] private string _keys = string.Empty;
    [ObservableProperty] private string _build = string.Empty;
    [ObservableProperty] private bool _dev;

    [ObservableProperty] private string _rawConfig = string.Empty;
    [ObservableProperty] private string _rawInit = string.Empty;

    [ObservableProperty] private string _previewText = string.Empty;
    [ObservableProperty] private bool _showPreview;

    private void LoadFromSettings(PluginSettings settings)
    {
        Enabled = settings.Enabled;
        Lazy = settings.Lazy;
        Version = settings.Version;
        Branch = settings.Branch;
        Priority = settings.Priority;
        Events = string.Join('\n', settings.Events ?? new List<string>());
        Commands = string.Join('\n', settings.Commands ?? new List<string>());
        FileTypes = string.Join('\n', settings.FileTypes ?? new List<string>());
        Keys = settings.Keys ?? string.Empty;
        Build = settings.Build ?? string.Empty;
        Dev = settings.Dev;
        RawConfig = settings.Config ?? string.Empty;
        RawInit = settings.Init ?? string.Empty;
    }

    private PluginSettings CollectSettings()
    {
        var settings = _services.Settings.GetOrCreate(Entry.Repo);
        settings.Enabled = Enabled;
        settings.Lazy = Lazy;
        settings.Version = TrimOrNull(Version);
        settings.Branch = TrimOrNull(Branch);
        settings.Priority = Priority;
        settings.Events = SplitLines(Events);
        settings.Commands = SplitLines(Commands);
        settings.FileTypes = SplitLines(FileTypes);
        settings.Keys = TrimOrNull(Keys);
        settings.Build = TrimOrNull(Build);
        settings.Dev = Dev;
        settings.Opts = Config.BuildValues();
        settings.RawOptsLua = TrimOrNull(Config.RawOptsLua);
        settings.Config = TrimOrNull(RawConfig);
        settings.Init = TrimOrNull(RawInit);
        return settings;
    }

    private async Task RefreshInstalledAsync()
    {
        var installed = await _services.Store.ListInstalledAsync(false);
        IsInstalled = installed.Any(i => string.Equals(i.Repo, Entry.Repo, StringComparison.OrdinalIgnoreCase));
        if (installed.FirstOrDefault(i => string.Equals(i.Repo, Entry.Repo, StringComparison.OrdinalIgnoreCase)) is { } item)
            StatusText = $"Installed {item.ShortCommit}{(item.Tag is null ? "" : $" ({item.Tag})")}.";
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsBusy = true;
        try
        {
            SetStatus("Installing...");
            var settings = CollectSettings();
            _services.Settings.Save();
            var messages = await Task.Run(() => _services.Store.ApplyConfig());
            var result = await _services.Store.InstallAsync(Entry, settings.Branch);
            AppendStatus(messages);
            SetStatus(result.Message);
            if (result.Success)
            {
                IsInstalled = true;
                await RefreshInstalledAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus("Install failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        IsBusy = true;
        try
        {
            SetStatus("Removing...");
            var result = await _services.Store.UninstallAsync(Entry.Repo);
            SetStatus(result.Message);
            if (result.Success)
            {
                IsInstalled = false;
                var settings = _services.Settings.GetOrCreate(Entry.Repo);
                settings.Enabled = false;
                _services.Settings.Save();
                await Task.Run(() => _services.Store.ApplyConfig());
            }
        }
        catch (Exception ex)
        {
            SetStatus("Uninstall failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        IsBusy = true;
        try
        {
            SetStatus("Updating...");
            var result = await _services.Store.UpdateAsync(Entry.Repo);
            SetStatus(result.Message);
            await RefreshInstalledAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAndApplyAsync()
    {
        IsBusy = true;
        try
        {
            var settings = CollectSettings();
            _services.Settings.Save();
            var messages = await Task.Run(() => _services.Store.ApplyConfig());
            AppendStatus(messages);
            SetStatus("Configuration written; restart Neovim to see changes.");
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        IsBusy = true;
        try
        {
            var enriched = await _services.GitHub.EnrichAsync(Entry, force: true);
            StarsText = FormatStars(enriched.Stars);
            Description = enriched.Description;
            SetStatus("Metadata refreshed from GitHub.");
        }
        catch (Exception ex)
        {
            SetStatus("Metadata refresh failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GeneratePreview()
    {
        var settings = CollectSettings();
        var spec = _services.Store.BuildSpec(Entry, settings);
        PreviewText = _services.Store.Generator.RenderPluginsFile(new[] { spec });
        ShowPreview = true;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        string dir = _services.Store.InstallDirFor(Entry.Repo);
        OpenPath(dir);
    }

    private static void OpenPath(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }

    private void AppendStatus(IEnumerable<string> messages)
    {
        var text = string.Join('\n', messages ?? Array.Empty<string>());
        if (!string.IsNullOrWhiteSpace(text))
            StatusText = text + "\n" + StatusText;
    }

    private void SetStatus(string text) => StatusText = text;

    private static string FormatStars(int? stars)
        => stars is null ? "" : $"{stars:N0}";

    private static string? TrimOrNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static List<string> SplitLines(string? text)
        => (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}