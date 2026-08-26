using KPatchLauncher.Models;

namespace KPatchLauncher.ViewModels;

/// <summary>
/// ComboBox row for loadout selection. Always includes a None sentinel.
/// </summary>
public sealed class LoadoutPickerItem : IEquatable<LoadoutPickerItem>
{
    public static LoadoutPickerItem None { get; } = new(null);

    public LoadoutPickerItem(PatchLoadout? loadout)
    {
        Loadout = loadout;
    }

    public PatchLoadout? Loadout { get; }

    public bool IsNone => Loadout == null;

    public string DisplayName => Loadout?.Name ?? "None";

    public bool Equals(LoadoutPickerItem? other)
    {
        if (other is null)
            return false;
        if (IsNone)
            return other.IsNone;
        return other.Loadout != null
               && string.Equals(Loadout!.Id, other.Loadout.Id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as LoadoutPickerItem);

    public override int GetHashCode() =>
        IsNone ? 0 : StringComparer.Ordinal.GetHashCode(Loadout!.Id);
}
