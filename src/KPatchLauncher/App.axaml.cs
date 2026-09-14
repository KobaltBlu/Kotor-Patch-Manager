using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using KPatchCore.Models;
using KPatchLauncher.Themes;
using KPatchLauncher.ViewModels;
using KPatchLauncher.Views;
using System;

namespace KPatchLauncher;

public partial class App : Application
{
    private string _cornerChromeMode = HudCornerChromeModes.FullId;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            UserThemeStore.RegisterAll(this);

            var viewModel = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel
            };
            viewModel.Dialogs = new WindowDialogService(window);
            desktop.MainWindow = window;

            // Seed intensity before theme apply so ApplyUiTheme's trailing refresh uses the right mode.
            ApplyCornerChromeIntensity(viewModel.HudCornerChromeId);
            ApplyUiTheme(viewModel.UiThemeId, gameForAuto: null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Sets <see cref="Application.RequestedThemeVariant"/> from a catalog id.
    /// Auto maps KotOR 1 / unknown → Kotor1 and KotOR 2 → Kotor2.
    /// </summary>
    public void ApplyUiTheme(string themeId, GameTitle? gameForAuto = null)
    {
        try
        {
            var variant = ResolveVariant(themeId, gameForAuto);
            RequestedThemeVariant = variant;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme '{themeId}': {ex.Message}");
            RequestedThemeVariant = AppThemeVariants.Kotor1;
        }

        // Theme swap changes AccentBrush / BorderLineBrush; refresh bracket resources.
        ApplyCornerChromeIntensity(_cornerChromeMode);
    }

    /// <summary>
    /// Updates HudPanel corner-bracket resources for full / muted / off.
    /// Resolves brushes from the active theme so muted tracks the current palette.
    /// </summary>
    public void ApplyCornerChromeIntensity(string? modeId)
    {
        var mode = HudCornerChromeModes.Parse(modeId);
        _cornerChromeMode = HudCornerChromeModes.ToId(mode);

        var accent = ResolveThemeBrush("AccentBrush") ?? new SolidColorBrush(Color.Parse("#D6AE55"));

        switch (mode)
        {
            case HudCornerChromeMode.Muted:
                // Soft accent (not BorderLineBrush — that matches the panel frame and vanishes).
                Resources["HudBracketBrush"] = accent;
                Resources["HudBracketStrokeThickness"] = 1.0;
                Resources["HudBracketOpacity"] = 0.55;
                Resources["HudBracketVisible"] = true;
                break;
            case HudCornerChromeMode.Off:
                Resources["HudBracketBrush"] = accent;
                Resources["HudBracketStrokeThickness"] = 2.0;
                Resources["HudBracketOpacity"] = 0.0;
                Resources["HudBracketVisible"] = false;
                break;
            default:
                Resources["HudBracketBrush"] = accent;
                Resources["HudBracketStrokeThickness"] = 2.0;
                Resources["HudBracketOpacity"] = 1.0;
                Resources["HudBracketVisible"] = true;
                break;
        }
    }

    private IBrush? ResolveThemeBrush(string key)
    {
        if (TryGetResource(key, ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;

        if (TryGetResource(key, RequestedThemeVariant, out value) && value is IBrush requested)
            return requested;

        return null;
    }

    public ThemeVariant ResolveVariant(string themeId, GameTitle? gameForAuto)
    {
        var id = string.IsNullOrWhiteSpace(themeId) ? AppThemeVariants.AutoId : themeId.Trim();

        if (string.Equals(id, AppThemeVariants.AutoId, StringComparison.OrdinalIgnoreCase))
        {
            return gameForAuto == GameTitle.KOTOR2
                ? AppThemeVariants.Kotor2
                : AppThemeVariants.Kotor1;
        }

        if (string.Equals(id, AppThemeVariants.Kotor1Id, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.Kotor1;

        if (string.Equals(id, AppThemeVariants.Kotor2Id, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.Kotor2;

        if (string.Equals(id, AppThemeVariants.NeutralId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.Neutral;

        if (string.Equals(id, AppThemeVariants.DarkId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.Dark;

        if (string.Equals(id, AppThemeVariants.LightId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.Light;

        if (string.Equals(id, AppThemeVariants.HighContrastId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.HighContrast;

        if (string.Equals(id, AppThemeVariants.ColorBlindDeuteranopiaId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.ColorBlindDeuteranopia;

        if (string.Equals(id, AppThemeVariants.ColorBlindProtanopiaId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.ColorBlindProtanopia;

        if (string.Equals(id, AppThemeVariants.ColorBlindTritanopiaId, StringComparison.OrdinalIgnoreCase))
            return AppThemeVariants.ColorBlindTritanopia;

        var slug = AppThemeVariants.TryGetUserSlug(id);
        if (slug != null)
        {
            if (UserThemeStore.FindUserThemePath(slug) == null)
                return AppThemeVariants.Kotor1;

            return AppThemeVariants.ForUserSlug(slug);
        }

        return AppThemeVariants.Kotor1;
    }

    /// <summary>
    /// Variant key used when exporting the currently effective built-in palette.
    /// </summary>
    public string GetEffectiveBuiltInExportKey()
    {
        var key = ActualThemeVariant?.Key?.ToString();
        return key switch
        {
            "Kotor2" => "Kotor2",
            "Neutral" => "Neutral",
            "Dark" => "Dark",
            "Light" => "Light",
            "HighContrast" => "HighContrast",
            "ColorBlindDeuteranopia" => "ColorBlindDeuteranopia",
            "ColorBlindProtanopia" => "ColorBlindProtanopia",
            "ColorBlindTritanopia" => "ColorBlindTritanopia",
            "Kotor1" => "Kotor1",
            _ => "Kotor1",
        };
    }
}
