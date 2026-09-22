using Avalonia.Controls;
using Avalonia.Interactivity;
using NvimManager.App.ViewModels;

namespace NvimManager.App.Views;

public partial class StoreView : UserControl
{
    public StoreView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is StoreViewModel vm)
            vm.OpenSelected();
    }
}