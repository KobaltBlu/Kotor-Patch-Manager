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
        var computed = new List<PatchOption>();

        foreach (var option in manifest.Options)
        {
            if (option.IsComputed)
            {
                if (string.IsNullOrWhiteSpace(option.Expression))
                    return PatchResult.Fail($"Computed option '{option.Id}' on {manifest.Id} is missing expression");
                computed.Add(option);
                continue;
            }

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

        var pending = new List<PatchOption>(computed);
        while (pending.Count > 0)
        {
            var progress = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var option = pending[i];
                if (!OptionExpression.TryEvaluate(option.Expression!, resolved, out var value, out var error))
                {
                    // Dependency not resolved yet — retry next pass unless nothing advances
                    if (!error.StartsWith("Unknown option '", StringComparison.Ordinal))
                        return PatchResult.Fail($"Computed option '{option.Id}': {error}");
                    continue;
                }

                if (option.Min.HasValue && value < option.Min.Value)
                    return PatchResult.Fail($"Option {option.Id} value {value} is below min {option.Min}");
                if (option.Max.HasValue && value > option.Max.Value)
                    return PatchResult.Fail($"Option {option.Id} value {value} is above max {option.Max}");

                resolved[option.Id] = value;
                pending.RemoveAt(i);
                progress = true;
            }

            if (!progress)
            {
                var ids = string.Join(", ", pending.Select(o => o.Id));
                return PatchResult.Fail($"Could not resolve computed options (missing deps or cycle): {ids}");
            }
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
