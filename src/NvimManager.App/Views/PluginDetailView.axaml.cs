using Avalonia.Controls;
using Avalonia.Interactivity;
using NvimManager.App.ViewModels;

namespace NvimManager.App.Views;

public partial class PluginDetailView : UserControl
{
    public PluginDetailView()
    {
        InitializeComponent();
    }

    private async void OnCopyPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginDetailViewModel vm || string.IsNullOrEmpty(vm.PreviewText))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(vm.PreviewText);
        vm.ShowCopyConfirmation();
    }
}
