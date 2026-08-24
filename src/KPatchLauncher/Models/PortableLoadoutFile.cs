using System.Text.Json;

namespace KPatchLauncher.Models;

/// <summary>
/// Shareable single-loadout file (*.kploadout). Does not include machine paths.
/// </summary>
public sealed class PortableLoadoutFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "Exported";
    public string GameKey { get; set; } = "unknown";
    public List<string> PatchIds { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(PortableLoadoutFile file) =>
        JsonSerializer.Serialize(file, JsonOptions);

    public static PortableLoadoutFile? TryDeserialize(string json, out string? error)
    {
        error = null;
        try
        {
            var file = JsonSerializer.Deserialize<PortableLoadoutFile>(json, JsonOptions);
            if (file == null)
            {
                error = "File is empty or invalid.";
                return null;
            }

            if (file.SchemaVersion < 1 || file.SchemaVersion > CurrentSchemaVersion)
            {
                error = $"Unsupported loadout schema version {file.SchemaVersion}.";
                return null;
            }

            if (file.PatchIds == null)
            {
                error = "Missing PatchIds.";
                return null;
            }

            file.Name = string.IsNullOrWhiteSpace(file.Name) ? "Imported" : file.Name.Trim();
            file.GameKey = string.IsNullOrWhiteSpace(file.GameKey) ? "unknown" : file.GameKey.Trim();
            return file;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static PortableLoadoutFile FromLoadout(PatchLoadout loadout) =>
        new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = loadout.Name,
            GameKey = loadout.GameKey,
            PatchIds = loadout.PatchIds.ToList()
        };

    public PatchLoadout ToLoadout() =>
        new()
        {
            Name = Name,
            GameKey = GameKey,
            PatchIds = PatchIds.ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
