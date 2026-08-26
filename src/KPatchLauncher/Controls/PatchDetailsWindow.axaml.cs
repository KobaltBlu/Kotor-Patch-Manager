using Avalonia.Controls;
using Avalonia.Interactivity;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Controls;

public partial class PatchDetailsWindow : Window
{
    public PatchDetailsWindow()
    {
        InitializeComponent();
        HudModalChrome.Attach(this);
    }

    public PatchDetailsWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
