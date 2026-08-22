using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KPatchCore.Models;
using KPatchLauncher.ViewModels;
using KPatchLauncher.Views;
using System;
using System.Linq;

namespace KPatchLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel
            };
            viewModel.Dialogs = new WindowDialogService(window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Dynamically switches the application theme based on game title
    /// </summary>
    public void LoadTheme(GameTitle gameTitle)
    {
        var themeUri = gameTitle switch
        {
            GameTitle.KOTOR2 => new Uri("avares://KPatchLauncher/Themes/Kotor2Theme.axaml"),
            _ => new Uri("avares://KPatchLauncher/Themes/Kotor1Theme.axaml")
        };

        try
        {
            var newTheme = (ResourceDictionary)AvaloniaXamlLoader.Load(themeUri);

            var existingThemes = Resources.MergedDictionaries
                .Where(dict => dict.TryGetResource("PrimaryBrush", null, out _))
                .ToList();

            foreach (var theme in existingThemes)
            {
                Resources.MergedDictionaries.Remove(theme);
            }

            Resources.MergedDictionaries.Add(newTheme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load theme: {ex.Message}");
        }
    }
}
