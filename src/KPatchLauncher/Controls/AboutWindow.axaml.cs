using Avalonia.Controls;
using Avalonia.Interactivity;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Controls;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
