using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Models;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

public sealed partial class StoreViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<CatalogEntry> _openDetail;
    private CancellationTokenSource? _enrichCts;

    public StoreViewModel(AppServices services, Action<CatalogEntry> openDetail)
    {
        _services = services;
        _openDetail = openDetail;
        _categories = services.Catalog.AllCategories.ToList();
        _selectedCategory = "All";
        _featuredOnly = false;
        _selectedSort = SortOptions[0];
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = new();

    public IReadOnlyList<string> SortOptions { get; } = new[] { "Name", "Stars", "Category" };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _categories;
    [ObservableProperty] private string _selectedCategory;
    [ObservableProperty] private bool _featuredOnly;
    [ObservableProperty] private string _selectedSort;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private PluginCardViewModel? _selectedPlugin;
    [ObservableProperty] private string _statusText = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshIfIdle();
    partial void OnSelectedCategoryChanged(string value) => RefreshIfIdle();
    partial void OnFeaturedOnlyChanged(bool value) => RefreshIfIdle();
    partial void OnSelectedSortChanged(string value) => RefreshIfIdle();

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
            SortEntries(list);

            var byId = Plugins.ToDictionary(p => p.Entry.Id, p => p, StringComparer.OrdinalIgnoreCase);
            var desired = new List<PluginCardViewModel>(list.Count);
            foreach (var entry in list)
            {
                if (!byId.TryGetValue(entry.Id, out var card))
                {
                    card = new PluginCardViewModel(entry, _services.Store, _openDetail, SetStatus);
                    byId[entry.Id] = card;
                }
                desired.Add(card);
            }

            var keep = desired.Select(d => d.Entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var card in Plugins.Where(p => !keep.Contains(p.Entry.Id)).ToList())
                Plugins.Remove(card);

            // Restore the result order (filtered re-adds used to pile up at the end).
            // A card that is not in the collection yet is inserted at its sorted slot.
            for (int i = 0; i < desired.Count; i++)
            {
                var target = desired[i];
                int current = Plugins.IndexOf(target);
                if (current == -1)
                    Plugins.Insert(i, target);
                else if (current != i)
                    Plugins.Move(current, i);
            }
        }
        finally
        {
            IsLoading = false;
        }

        _ = EnrichStarsAsync();
    }

    private void SortEntries(List<CatalogEntry> list)
    {
        switch (SelectedSort)
        {
            case "Stars":
                list.Sort((a, b) =>
                {
                    int cmp = (b.Stars ?? -1).CompareTo(a.Stars ?? -1);
                    return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;
            case "Category":
                list.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                    return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;
            default:
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                break;
        }
    }

    /// <summary>
    /// Backfills live GitHub stars/descriptions for the visible cards.
    /// Results are marshalled onto the UI thread; the service's 12-hour
    /// disk cache keeps repeat launches and re-filters free.
    /// </summary>
    private async Task EnrichStarsAsync()
    {
        _enrichCts?.Cancel();
        var cts = new CancellationTokenSource();
        _enrichCts = cts;
        var token = cts.Token;

        var snapshot = Plugins.ToList();
        foreach (var card in snapshot)
        {
            if (token.IsCancellationRequested) return;
            CatalogEntry enriched;
            try
            {
                enriched = await _services.GitHub.EnrichAsync(card.Entry).WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue; // offline / rate-limited: keep the bundled metadata
            }

            var target = card;
            Dispatcher.UIThread.Post(() =>
            {
                target.SetStars(enriched.Stars);
                target.SetDescription(enriched.Description);
            });
        }
    }

    public void RefreshInstalledState()
    {
        _ = Task.Run(async () =>
        {
            var installed = await _services.Store.ListInstalledAsync(false);
            var set = new HashSet<string>(installed.Select(i => i.Repo), StringComparer.OrdinalIgnoreCase);
            // Property changes must happen on the UI thread (Avalonia bindings).
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var card in Plugins)
                    card.IsInstalled = set.Contains(card.Entry.Repo);
            });
        });
    }

    private void SetStatus(string message) => StatusText = message;

    /// <summary>Invoked by the view when the selected list item changes.</summary>
    public void OpenSelected()
    {
        if (SelectedPlugin is { } card)
            _openDetail(card.Entry);
    }
}
