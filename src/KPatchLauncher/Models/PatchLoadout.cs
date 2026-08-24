namespace KPatchLauncher.Models;

/// <summary>
/// A named set of patches for a game target.
/// </summary>
public sealed class PatchLoadout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    /// <summary>kotor1, kotor2, or unknown</summary>
    public string GameKey { get; set; } = "unknown";
    public List<string> PatchIds { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Remembered paths for a game title slot.
/// </summary>
public sealed class GameTargetMemory
{
    public string GamePath { get; set; } = string.Empty;
    public string PatchesPath { get; set; } = string.Empty;
    public string? ActiveLoadoutId { get; set; }
}
