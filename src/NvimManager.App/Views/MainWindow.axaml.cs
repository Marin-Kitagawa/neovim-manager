using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NvimManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel main)
                main.Dispose();
        };
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Forward sidebar clicks to the view-model. The binding above is
        // OneWay, so programmatic page changes (e.g. plugin detail) never
        // get coerced back to null by the ListBox.
        if (DataContext is ViewModels.MainWindowViewModel main
            && e.AddedItems.Count > 0
            && e.AddedItems[0] is ViewModels.NavItem item)
        {
            main.CurrentPage = item;
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
        => Close();
}