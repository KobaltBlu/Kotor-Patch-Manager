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
    public Dictionary<string, Dictionary<string, int>> OptionValues { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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
            file.OptionValues ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
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
            PatchIds = loadout.PatchIds.ToList(),
            OptionValues = CloneOptionValues(loadout.OptionValues)
        };

    public PatchLoadout ToLoadout() =>
        new()
        {
            Name = Name,
            GameKey = GameKey,
            PatchIds = PatchIds.ToList(),
            OptionValues = CloneOptionValues(OptionValues),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static Dictionary<string, Dictionary<string, int>> CloneOptionValues(
        Dictionary<string, Dictionary<string, int>> source)
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (patchId, opts) in source)
            map[patchId] = new Dictionary<string, int>(opts, StringComparer.OrdinalIgnoreCase);
        return map;
    }
}
