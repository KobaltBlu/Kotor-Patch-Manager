namespace KPatchLauncher.ViewModels;

/// <summary>
/// Display row for the Settings corner-chrome ComboBox. <see cref="Id"/> is persisted globally.
/// </summary>
public sealed class HudCornerChromeOption
{
    public HudCornerChromeOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
