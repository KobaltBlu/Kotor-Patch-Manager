namespace KPatchLauncher.ViewModels;

/// <summary>
/// Display row for the Settings theme ComboBoxes. <see cref="Id"/> is persisted per game.
/// </summary>
public sealed class UiThemeOption
{
    public UiThemeOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
