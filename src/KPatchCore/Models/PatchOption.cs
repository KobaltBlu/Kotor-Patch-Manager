namespace KPatchCore.Models;

/// <summary>
/// User-configurable patch option (manifest [[patch.options]]).
/// Phase 1: integer only.
/// </summary>
public sealed class PatchOption
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public int Default { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
}
