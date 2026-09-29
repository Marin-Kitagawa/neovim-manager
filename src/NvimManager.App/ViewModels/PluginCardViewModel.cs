using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Models;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

/// <summary>One row in the store list.</summary>
public sealed partial class PluginCardViewModel : ViewModelBase
{
    private readonly StoreService _store;
    private readonly Action<CatalogEntry> _openDetail;
    private readonly Action<string> _report;

    public PluginCardViewModel(CatalogEntry entry, StoreService store, Action<CatalogEntry> openDetail, Action<string>? report = null)
    {
        Entry = entry;
        _store = store;
        _openDetail = openDetail;
        _report = report ?? (_ => { });
        _starsText = FormatStars(entry.Stars);
        _description = entry.Description;
    }

    public CatalogEntry Entry { get; }
    public string Name => Entry.Name;
    public string Repo => Entry.Repo;
    public string Category => Entry.Category;
    public string TagsText => string.Join(" · ", Entry.Tags);

    [ObservableProperty] private string _starsText;
    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isBusy;

    public string StatusText => IsInstalled ? "Installed" : "Not installed";

    public void SetStars(int? stars) => StarsText = FormatStars(stars);

    public void SetDescription(string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) && description != Description)
            Description = description;
    }

    private static string FormatStars(int? stars)
        => stars is null ? "" : $"{stars:N0}";

    [RelayCommand]
    private void OpenDetails() => _openDetail(Entry);

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _store.InstallAsync(Entry);
            if (result.Success)
            {
                IsInstalled = true;
                _report(result.Message);
            }
            else
            {
                _report("Install failed: " + result.Message);
            }
        }
        catch (Exception ex)
        {
            _report("Install failed: " + ex.Message);
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
            var result = await _store.UninstallAsync(Entry.Repo);
            if (result.Success)
            {
                IsInstalled = false;
                _report(result.Message);
            }
            else
            {
                _report("Uninstall failed: " + result.Message);
            }
        }
        catch (Exception ex)
        {
            _report("Uninstall failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}