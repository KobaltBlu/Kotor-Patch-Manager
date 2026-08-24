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

    /// <summary>
    /// Writes loadouts to disk. Returns false and an error message on failure.
    /// </summary>
    public static bool TrySave(StoreData data, out string? error)
    {
        error = null;
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(data, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Legacy helper; prefer <see cref="TrySave"/>.</summary>
    public static void Save(StoreData data) => TrySave(data, out _);
}
