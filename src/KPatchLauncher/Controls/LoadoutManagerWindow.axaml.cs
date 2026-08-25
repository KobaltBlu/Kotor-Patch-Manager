using Avalonia.Controls;
using Avalonia.Interactivity;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Controls;

public partial class LoadoutManagerWindow : Window
{
    public LoadoutManagerWindow()
    {
        InitializeComponent();
    }

    public LoadoutManagerWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
