using Avalonia.Styling;

namespace KPatchLauncher.Themes;

/// <summary>
/// Custom Avalonia ThemeVariants for HUD palettes. Game and accessibility variants fall back
/// to Dark (or Light for the light theme) so FluentTheme resources resolve when a key is absent.
/// </summary>
public static class AppThemeVariants
{
    public const string AutoId = "auto";
    public const string Kotor1Id = "kotor1";
    public const string Kotor2Id = "kotor2";
    public const string NeutralId = "neutral";
    public const string DarkId = "dark";
    public const string LightId = "light";
    public const string HighContrastId = "highcontrast";
    public const string ColorBlindDeuteranopiaId = "colorblind-deuteranopia";
    public const string ColorBlindProtanopiaId = "colorblind-protanopia";
    public const string ColorBlindTritanopiaId = "colorblind-tritanopia";
    public const string UserIdPrefix = "user:";

    public static readonly ThemeVariant Kotor1 = new("Kotor1", ThemeVariant.Dark);
    public static readonly ThemeVariant Kotor2 = new("Kotor2", ThemeVariant.Dark);
    public static readonly ThemeVariant Neutral = new("Neutral", ThemeVariant.Dark);
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
    public static readonly ThemeVariant ColorBlindDeuteranopia = new("ColorBlindDeuteranopia", ThemeVariant.Dark);
    public static readonly ThemeVariant ColorBlindProtanopia = new("ColorBlindProtanopia", ThemeVariant.Dark);
    public static readonly ThemeVariant ColorBlindTritanopia = new("ColorBlindTritanopia", ThemeVariant.Dark);

    /// <summary>Uses Avalonia's built-in Dark variant so Fluent chrome matches.</summary>
    public static ThemeVariant Dark => ThemeVariant.Dark;

    /// <summary>Uses Avalonia's built-in Light variant so Fluent chrome matches.</summary>
    public static ThemeVariant Light => ThemeVariant.Light;

    public static ThemeVariant ForUserSlug(string slug) => new(slug, ThemeVariant.Dark);

    public static bool IsUserThemeId(string? themeId) =>
        !string.IsNullOrWhiteSpace(themeId)
        && themeId.StartsWith(UserIdPrefix, StringComparison.OrdinalIgnoreCase);

    public static string? TryGetUserSlug(string themeId)
    {
        if (!IsUserThemeId(themeId))
            return null;

        var slug = themeId[UserIdPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(slug) ? null : slug;
    }

    public static string ToUserThemeId(string slug) => UserIdPrefix + slug;

    /// <summary>Reserved file stems that must not collide with built-in catalog ids / variant keys.</summary>
    public static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "kotor1", "kotor2", "neutral", "dark", "light", "default",
        "highcontrast", "colorblind-deuteranopia", "colorblind-protanopia", "colorblind-tritanopia",
        "high-contrast", "colorblinddeuteranopia", "colorblindprotanopia", "colorblindtritanopia",
    };
}
