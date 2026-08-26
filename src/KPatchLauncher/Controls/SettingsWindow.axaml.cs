using Avalonia.Controls;
using Avalonia.Interactivity;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Controls;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
