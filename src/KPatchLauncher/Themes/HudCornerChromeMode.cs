namespace KPatchLauncher.Themes;

/// <summary>
/// Intensity for HudPanel L-bracket corner accents. Persisted as a lowercase string id.
/// </summary>
public enum HudCornerChromeMode
{
    Full,
    Muted,
    Off,
}

public static class HudCornerChromeModes
{
    public const string FullId = "full";
    public const string MutedId = "muted";
    public const string OffId = "off";

    public static string ToId(HudCornerChromeMode mode) => mode switch
    {
        HudCornerChromeMode.Muted => MutedId,
        HudCornerChromeMode.Off => OffId,
        _ => FullId,
    };

    public static HudCornerChromeMode Parse(string? id)
    {
        if (string.Equals(id, MutedId, StringComparison.OrdinalIgnoreCase))
            return HudCornerChromeMode.Muted;
        if (string.Equals(id, OffId, StringComparison.OrdinalIgnoreCase))
            return HudCornerChromeMode.Off;
        return HudCornerChromeMode.Full;
    }
}
