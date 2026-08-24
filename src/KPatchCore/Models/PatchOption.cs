namespace KPatchCore.Models;

/// <summary>
/// User-configurable or derived patch option (manifest [[patch.options]]).
/// Supported types: integer, computed.
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

    /// <summary>
    /// Expression for type=computed (e.g. "max_level + 1"). Null for integer options.
    /// </summary>
    public string? Expression { get; init; }

    public bool IsComputed =>
        string.Equals(Type, "computed", StringComparison.OrdinalIgnoreCase);
}
