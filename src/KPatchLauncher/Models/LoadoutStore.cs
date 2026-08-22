using System.Text.Json;

namespace KPatchLauncher.Models;

/// <summary>
/// Persists named loadouts under %AppData%/KPatchLauncher/loadouts.json
/// </summary>
public static class LoadoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KPatchLauncher",
            "loadouts.json");

    public sealed class StoreData
    {
        public List<PatchLoadout> Loadouts { get; set; } = new();
    }

    public static StoreData Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new StoreData();

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<StoreData>(json, JsonOptions) ?? new StoreData();
        }
        catch
        {
            return new StoreData();
        }
    }

    public static void Save(StoreData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }
}
