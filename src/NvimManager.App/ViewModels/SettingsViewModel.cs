using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _configDir = services.Paths.ConfigDir;
        _dataDir = services.Paths.DataDir;
        _lazyRoot = services.Paths.LazyRoot;
        _initLua = services.Paths.InitLuaPath;
        _gitPath = services.Git.Executable;
        if (services.Store.IsAstroNvim)
            _astroNote = "AstroNvim config detected: installs and saves write per-plugin specs to lua/plugins/ instead of the nvimmanager bundle below.";
    }

    [ObservableProperty] private string _configDir;
    [ObservableProperty] private string _dataDir;
    [ObservableProperty] private string _lazyRoot;
    [ObservableProperty] private string _initLua;
    [ObservableProperty] private string _gitPath = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _astroNote = string.Empty;

    public IReadOnlyList<string> Themes => ThemeManager.ThemeNames;

    [ObservableProperty] private string _selectedTheme = ThemeManager.CurrentName;

    partial void OnSelectedThemeChanged(string value)
    {
        if (ThemeManager.IsKnown(value))
            ThemeManager.ApplyTheme(value);
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        Directory.CreateDirectory(ConfigDir);
        OpenPath(ConfigDir);
    }

    [RelayCommand]
    private void OpenLazyRoot()
    {
        Directory.CreateDirectory(LazyRoot);
        OpenPath(LazyRoot);
    }

    [RelayCommand]
    private void OpenInitLua()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InitLua) ?? ConfigDir);
        OpenPath(InitLua);
    }

    [RelayCommand]
    private async Task ApplyConfigAsync()
    {
        IsBusy = true;
        try
        {
            var messages = await Task.Run(() => _services.Store.ApplyConfig());
            StatusText = string.Join('\n', messages);
        }
        catch (Exception ex)
        {
            StatusText = "Write failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void OpenPath(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }
}