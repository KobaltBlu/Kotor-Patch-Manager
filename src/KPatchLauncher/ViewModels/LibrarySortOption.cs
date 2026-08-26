namespace KPatchLauncher.ViewModels;

/// <summary>
/// Display row for the library sort ComboBox. <see cref="Id"/> is persisted.
/// </summary>
public sealed class LibrarySortOption
{
    public LibrarySortOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
