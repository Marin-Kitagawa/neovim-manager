using NvimManager.Core.Models;
using NvimManager.Core.Services;

namespace NvimManager.App;

/// <summary>Shared service graph, composed once in <see cref="NvimManager.App.ViewModels.MainWindowViewModel"/>.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Paths = NeovimLocator.Locate();
        Catalog = new CatalogService();
        Git = new GitService();
        Settings = new SettingsStore();
        GitHub = new GitHubEnrichmentService();
        Store = new StoreService(Catalog, Git, Settings, Paths);
    }

    public NvimPaths Paths { get; }
    public CatalogService Catalog { get; }
    public GitService Git { get; }
    public SettingsStore Settings { get; }
    public GitHubEnrichmentService GitHub { get; }
    public StoreService Store { get; }

    public void Dispose() => GitHub.Dispose();
}