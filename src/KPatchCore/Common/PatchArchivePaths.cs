namespace KPatchCore.Common;

/// <summary>
/// Validates relative paths inside .kpatch archives for markdown assets.
/// </summary>
public static class PatchArchivePaths
{
    /// <summary>
    /// Normalizes and validates a relative archive path. Rejects traversal, absolute URIs, and schemes.
    /// </summary>
    public static bool TryNormalize(string? relativePath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var trimmed = relativePath.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
            (absolute.IsAbsoluteUri || absolute.Scheme.Length > 1))
        {
            return false;
        }

        if (Path.IsPathRooted(trimmed))
            return false;

        var parts = trimmed.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "..")
                return false;
            if (part.Contains(':', StringComparison.Ordinal))
                return false;
        }

        normalized = string.Join('/', parts);
        return normalized.Length > 0;
    }
}
