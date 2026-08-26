using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;

namespace KPatchLauncher.Themes;

/// <summary>
/// Discovers, validates, and registers user Avalonia ResourceDictionary themes from AppData.
/// </summary>
public static class UserThemeStore
{
    private static readonly string[] RequiredKeys =
    {
        "BackgroundBrush",
        "PanelFillBrush",
        "PanelInsetBrush",
        "BorderLineBrush",
        "PrimaryBrush",
        "TextBrush",
        "CaptionBrush",
        "MutedTextBrush",
        "SelectionBackgroundBrush",
        "SelectionBorderBrush",
        "HoverBackgroundBrush",
        "ActiveBrush",
        "SuccessBrush",
        "AccentBrush",
        "WarningBrush",
        "ErrorBrush",
        "DisabledBrush",
    };

    private static readonly Regex SlugSanitize = new(@"[^a-z0-9-]+", RegexOptions.Compiled);

    public static string ThemesDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KPatchLauncher",
            "themes");

    public static void EnsureThemesDirectory()
    {
        Directory.CreateDirectory(ThemesDirectory);
    }

    public static IReadOnlyList<UserThemeInfo> ListThemes()
    {
        EnsureThemesDirectory();
        var list = new List<UserThemeInfo>();

        foreach (var path in Directory.EnumerateFiles(ThemesDirectory, "*.axaml")
                     .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase))
        {
            var slug = SanitizeSlug(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            list.Add(new UserThemeInfo(slug, Path.GetFileNameWithoutExtension(path)!, path));
        }

        return list;
    }

    public static string SanitizeSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var slug = SlugSanitize.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return slug;
    }

    /// <summary>
    /// Loads every valid user theme into the application's ThemeDictionaries.
    /// Returns themes that failed to load.
    /// </summary>
    public static IReadOnlyList<(string Path, string Error)> RegisterAll(Application app)
    {
        EnsureThemesDirectory();
        var errors = new List<(string Path, string Error)>();

        foreach (var info in ListThemes())
        {
            if (!TryLoadAndRegister(app, info.FilePath, info.Slug, out var error))
                errors.Add((info.FilePath, error ?? "Unknown error"));
        }

        return errors;
    }

    public static bool TryLoadAndRegister(Application app, string filePath, string slug, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(filePath))
            {
                error = "Theme file not found.";
                return false;
            }

            var xaml = File.ReadAllText(filePath);
            var loaded = AvaloniaRuntimeXamlLoader.Load(xaml, typeof(App).Assembly);
            if (loaded is not ResourceDictionary dict)
            {
                error = "Theme must be a ResourceDictionary root.";
                return false;
            }

            if (!TryValidate(dict, out error))
                return false;

            var variant = AppThemeVariants.ForUserSlug(slug);
            var dictionaries = GetThemeDictionaries(app);
            // Replace any prior registration for this slug (Key equality is by name).
            foreach (var existing in dictionaries.Keys.Where(k =>
                         string.Equals(k.Key?.ToString(), slug, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                dictionaries.Remove(existing);
            }

            dictionaries[variant] = dict;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryValidate(ResourceDictionary dict, out string? error)
    {
        error = null;
        var missing = new List<string>();
        foreach (var key in RequiredKeys)
        {
            if (!dict.TryGetResource(key, ThemeVariant.Default, out _))
                missing.Add(key);
        }

        if (missing.Count == 0)
            return true;

        error = "Missing required keys: " + string.Join(", ", missing);
        return false;
    }

    /// <summary>
    /// Copies a source .axaml into the themes folder under a sanitized slug name.
    /// </summary>
    public static bool TryImport(Application app, string sourcePath, out UserThemeInfo? info, out string? error)
    {
        info = null;
        error = null;

        try
        {
            EnsureThemesDirectory();
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var slug = SanitizeSlug(stem);
            if (string.IsNullOrWhiteSpace(slug))
            {
                error = "Could not derive a valid theme id from the file name.";
                return false;
            }

            // Avoid colliding with built-in variant keys / catalog ids.
            if (AppThemeVariants.ReservedSlugs.Contains(slug))
                slug += "-user";

            var dest = Path.Combine(ThemesDirectory, slug + ".axaml");
            File.Copy(sourcePath, dest, overwrite: true);

            if (!TryLoadAndRegister(app, dest, slug, out error))
            {
                try { File.Delete(dest); } catch { /* ignore */ }
                return false;
            }

            info = new UserThemeInfo(slug, stem ?? slug, dest);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static async Task ExportBuiltInAsync(string themeVariantKey, string destinationPath)
    {
        var uri = themeVariantKey switch
        {
            "Kotor2" => new Uri("avares://KPatchLauncher/Themes/Kotor2Theme.axaml"),
            "Neutral" => new Uri("avares://KPatchLauncher/Themes/NeutralTheme.axaml"),
            "Dark" => new Uri("avares://KPatchLauncher/Themes/DarkTheme.axaml"),
            "Light" => new Uri("avares://KPatchLauncher/Themes/LightTheme.axaml"),
            "HighContrast" => new Uri("avares://KPatchLauncher/Themes/HighContrastTheme.axaml"),
            "ColorBlindDeuteranopia" => new Uri("avares://KPatchLauncher/Themes/ColorBlindDeuteranopiaTheme.axaml"),
            "ColorBlindProtanopia" => new Uri("avares://KPatchLauncher/Themes/ColorBlindProtanopiaTheme.axaml"),
            "ColorBlindTritanopia" => new Uri("avares://KPatchLauncher/Themes/ColorBlindTritanopiaTheme.axaml"),
            _ => new Uri("avares://KPatchLauncher/Themes/Kotor1Theme.axaml"),
        };

        await using var stream = AssetLoader.Open(uri);
        await using var file = File.Create(destinationPath);
        await stream.CopyToAsync(file);
    }

    public static string? FindUserThemePath(string slug)
    {
        var path = Path.Combine(ThemesDirectory, slug + ".axaml");
        return File.Exists(path) ? path : null;
    }

    private static IDictionary<ThemeVariant, IThemeVariantProvider> GetThemeDictionaries(Application app)
    {
        if (app.Resources is ResourceDictionary root)
            return root.ThemeDictionaries;

        throw new InvalidOperationException("Application.Resources must be a ResourceDictionary.");
    }
}

public sealed record UserThemeInfo(string Slug, string DisplayName, string FilePath);
