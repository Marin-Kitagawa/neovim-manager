using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Models;

namespace NvimManager.App.ViewModels;

public sealed record NavItem(string Title, StreamGeometry Icon, ViewModelBase ViewModel)
{
    public override string ToString() => Title;
}

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainWindowViewModel()
    {
        _services = new AppServices();

        var storeVm = new StoreViewModel(_services, OpenPlugin);
        _storePage = new NavItem("Store", Icons.Star, storeVm);
        _installedPage = new NavItem("Installed", Icons.BoxChecked, new InstalledViewModel(_services));
        _settingsPage = new NavItem("Settings", Icons.Sliders, new SettingsViewModel(_services));

        Pages.Add(StorePage);
        Pages.Add(InstalledPage);
        Pages.Add(SettingsPage);
        _currentPage = StorePage;

        LoadAsync();
    }

    public ObservableCollection<NavItem> Pages { get; } = new();

    [ObservableProperty] private NavItem _storePage;
    [ObservableProperty] private NavItem _installedPage;
    [ObservableProperty] private NavItem _settingsPage;
    [ObservableProperty] private NavItem _currentPage;

    private async void LoadAsync()
    {
        if (StorePage.ViewModel is StoreViewModel store)
            await store.LoadAsync();
    }

    public void OpenPlugin(CatalogEntry entry)
    {
        var detail = new PluginDetailViewModel(_services, entry, () => CurrentPage = StorePage);
        // Reuse the plugin's own nav entry title so the sidebar stays meaningful.
        CurrentPage = new NavItem(entry.Name, Icons.Heart, detail);
        RefreshInstalled();
    }

    private void RefreshInstalled()
    {
        if (StorePage.ViewModel is StoreViewModel store)
            store.RefreshInstalledState();
    }

    public AppServices Services => _services;

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }
}