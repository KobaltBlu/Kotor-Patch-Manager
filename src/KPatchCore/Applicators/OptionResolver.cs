using KPatchCore.Models;

namespace KPatchCore.Applicators;

/// <summary>
/// Validates and resolves user option values; applies templates in hook bytes.
/// </summary>
public static class OptionResolver
{
    public static PatchResult ResolveOptions(
        PatchManifest manifest,
        IReadOnlyDictionary<string, int>? userValues,
        out Dictionary<string, int> resolved)
    {
        resolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in manifest.Options)
        {
            if (!string.Equals(option.Type, "integer", StringComparison.OrdinalIgnoreCase))
            {
                return PatchResult.Fail($"Unsupported option type '{option.Type}' on {manifest.Id}.{option.Id}");
            }

            var value = userValues != null && userValues.TryGetValue(option.Id, out var user)
                ? user
                : option.Default;

            if (option.Min.HasValue && value < option.Min.Value)
                return PatchResult.Fail($"Option {option.Id} value {value} is below min {option.Min}");
            if (option.Max.HasValue && value > option.Max.Value)
                return PatchResult.Fail($"Option {option.Id} value {value} is above max {option.Max}");

            resolved[option.Id] = value;
        }

        // Convenience: expose max_level_inclusive when max_level is present
        if (resolved.TryGetValue("max_level", out var maxLevel) &&
            !resolved.ContainsKey("max_level_inclusive"))
        {
            resolved["max_level_inclusive"] = maxLevel + 1;
        }

        return PatchResult.Ok("Options resolved");
    }

    /// <summary>
    /// Resolves template markers in replacement bytes. Templates array is parallel to bytes;
    /// non-null entries are option IDs whose int value (0-255) replaces that byte.
    /// </summary>
    public static PatchResult ApplyTemplates(
        Hook hook,
        IReadOnlyDictionary<string, int> resolvedOptions)
    {
        if (hook.ReplacementTemplates == null || hook.ReplacementBytes == null)
            return PatchResult.Ok();

        if (hook.ReplacementTemplates.Length != hook.ReplacementBytes.Length)
            return PatchResult.Fail("Replacement template length mismatch");

        var bytes = (byte[])hook.ReplacementBytes.Clone();
        for (var i = 0; i < hook.ReplacementTemplates.Length; i++)
        {
            var templateId = hook.ReplacementTemplates[i];
            if (string.IsNullOrEmpty(templateId))
                continue;

            if (!resolvedOptions.TryGetValue(templateId, out var value))
                return PatchResult.Fail($"Unknown option template '{{{{{templateId}}}}}'");

            if (value < 0 || value > 255)
                return PatchResult.Fail($"Option '{templateId}' value {value} does not fit in a byte");

            bytes[i] = (byte)value;
        }

        hook.ReplacementBytes = bytes;
        hook.ReplacementTemplates = null;
        return PatchResult.Ok();
    }
}
