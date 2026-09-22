using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Models;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

public sealed partial class StoreViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<CatalogEntry> _openDetail;

    public StoreViewModel(AppServices services, Action<CatalogEntry> openDetail)
    {
        _services = services;
        _openDetail = openDetail;
        _categories = services.Catalog.AllCategories.ToList();
        _selectedCategory = "All";
        _featuredOnly = false;
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _categories;
    [ObservableProperty] private string _selectedCategory;
    [ObservableProperty] private bool _featuredOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private PluginCardViewModel? _selectedPlugin;

    partial void OnSearchTextChanged(string value) => RefreshIfIdle();
    partial void OnSelectedCategoryChanged(string value) => RefreshIfIdle();
    partial void OnFeaturedOnlyChanged(bool value) => RefreshIfIdle();

    private void RefreshIfIdle()
    {
        if (!IsLoading)
            _ = ApplyFilterAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await ApplyFilterAsync();
        RefreshInstalledState();
    }

    private async Task ApplyFilterAsync()
    {
        IsLoading = true;
        try
        {
            var list = await Task.Run(() => _services.Catalog.Search(SearchText, SelectedCategory, FeaturedOnly).ToList());
            var currentIds = Plugins.Select(p => p.Entry.Id).ToHashSet();
            var nextIds = list.Select(p => p.Id).ToHashSet();

            foreach (var removed in currentIds.Except(nextIds).ToList())
            {
                var card = Plugins.FirstOrDefault(p => p.Entry.Id == removed);
                if (card is not null) Plugins.Remove(card);
            }

            foreach (var entry in list)
            {
                if (Plugins.Any(p => p.Entry.Id == entry.Id)) continue;
                Plugins.Add(new PluginCardViewModel(entry, _services.Store, _openDetail));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshInstalledState()
    {
        _ = Task.Run(async () =>
        {
            var installed = await _services.Store.ListInstalledAsync(false);
            var set = new HashSet<string>(installed.Select(i => i.Repo), StringComparer.OrdinalIgnoreCase);
            foreach (var card in Plugins)
                card.IsInstalled = set.Contains(card.Entry.Repo);
        });
    }

    /// <summary>Invoked by the view when the selected list item changes.</summary>
    public void OpenSelected()
    {
        if (SelectedPlugin is { } card)
            _openDetail(card.Entry);
    }
}