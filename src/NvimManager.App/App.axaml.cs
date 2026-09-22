using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NvimManager.App.ViewModels;
using NvimManager.App.Views;

namespace NvimManager.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ThemeManager.Initialize(this);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }
    }
}