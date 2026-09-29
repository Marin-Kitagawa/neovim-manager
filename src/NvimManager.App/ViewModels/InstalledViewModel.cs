using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvimManager.Core.Services;

namespace NvimManager.App.ViewModels;

public sealed partial class InstalledViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstalledViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<InstalledItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _services.Store.ListInstalledAsync(checkUpdates: false);
            SyncItems(list);

            StatusText = Describe(list, _services.Store.IsAstroNvim);
        }
        catch (Exception ex)
        {
            StatusText = "Failed to list installed plugins: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SyncItems(List<InstalledItem> list)
    {
        var current = Items.Select(i => i.Dir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = list.Select(i => i.Dir).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removed in current.Except(next, StringComparer.OrdinalIgnoreCase).ToList())
        {
            var item = Items.FirstOrDefault(i => string.Equals(i.Dir, removed, StringComparison.OrdinalIgnoreCase));
            if (item is not null) Items.Remove(item);
        }

        foreach (var installed in list)
        {
            var item = Items.FirstOrDefault(i => string.Equals(i.Dir, installed.Dir, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                Items.Add(new InstalledItemViewModel(installed, _services.Store, RemoveItem, ReportStatus));
            }
            else
            {
                item.Update(installed);
            }
        }
    }

    private void ReportStatus(string message) => StatusText = message;

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            StatusText = "Fetching latest commits from GitHub...";
            var list = await _services.Store.CheckForUpdatesAsync();
            SyncItems(list);

            var withUpdates = Items.Count(i => i.HasUpdate);
            StatusText = withUpdates > 0 ? $"{withUpdates} plugin(s) have updates." : "All plugins up to date.";
        }
        catch (Exception ex)
        {
            StatusText = "Update check failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _services.Store.UpdateAllAsync();
            StatusText = result.Message;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Update all failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveItem()
        => _ = RefreshAsync();

    private static string Describe(IReadOnlyList<InstalledItem> list, bool isAstroNvim)
    {
        string framework = isAstroNvim ? " · AstroNvim config detected" : string.Empty;
        if (list.Count == 0)
            return "No plugins found. Supported: lazy.nvim (incl. AstroNvim, LazyVim, NvChad), packer.nvim, paq-nvim, pckr.nvim, mini.deps, vim.pack, native pack, vim-plug, dein.vim, LunarVim, rocks.nvim, vim bundles." + framework;
        var breakdown = list.GroupBy(i => i.Manager, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} {g.Count()}");
        return $"{list.Count} plugin(s) found · " + string.Join(" · ", breakdown) + framework;
    }
}

public sealed partial class InstalledItemViewModel : ViewModelBase
{
    private readonly StoreService _store;
    private readonly Action _onRemoved;
    private readonly Action<string> _report;

    public InstalledItemViewModel(InstalledItem item, StoreService store, Action onRemoved, Action<string> report)
    {
        _store = store;
        _onRemoved = onRemoved;
        _report = report;
        _repo = item.Repo;
        _name = item.Name;
        _dir = item.Dir;
        _manager = item.Manager;
        _sourceLabel = SourceLabelFor(item.Manager, item.Kind);
        _commit = item.ShortCommit ?? item.Tag ?? "?";
        _description = item.Entry is null ? "Not in the community store." : item.Entry.Description;
        _hasUpdate = item.HasUpdate;
    }

    [ObservableProperty] private string _repo;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _dir;
    [ObservableProperty] private string _manager;
    [ObservableProperty] private string _sourceLabel;
    [ObservableProperty] private string _commit;
    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private bool _isBusy;

    private static string SourceLabelFor(string manager, string kind)
        => string.IsNullOrEmpty(kind) ? manager : $"{manager} · {kind}";

    public void Update(InstalledItem item)
    {
        Repo = item.Repo;
        Name = item.Name;
        Dir = item.Dir;
        Manager = item.Manager;
        SourceLabel = SourceLabelFor(item.Manager, item.Kind);
        HasUpdate = item.HasUpdate;
        Commit = item.HasUpdate
            ? $"{item.ShortCommit} \u2192 update available"
            : item.ShortCommit ?? item.Tag ?? "?";
        Description = item.Entry is null ? "Not in the community store." : item.Entry.Description;
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _store.UpdateDirAsync(Dir);
            _report(result.Message);
            if (result.Success)
                _onRemoved();
        }
        catch (Exception ex)
        {
            _report($"Update of {Name} failed: {ex.Message}");
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
            var result = await _store.UninstallDirAsync(Dir);
            _report(result.Message);
            if (result.Success)
                _onRemoved();
        }
        catch (Exception ex)
        {
            _report($"Failed to remove {Name}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!Directory.Exists(Dir)) return;
        var psi = new System.Diagnostics.ProcessStartInfo { FileName = Dir, UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }

    internal static string LatestShort(string? commit)
    {
        if (string.IsNullOrWhiteSpace(commit)) return "?";
        return commit!.Length <= 7 ? commit : commit[..7];
    }
}