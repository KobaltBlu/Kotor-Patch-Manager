namespace KPatchLauncher.Markdown;

/// <summary>
/// Context passed through markdown rendering for assets and links.
/// </summary>
public sealed class MarkdownRenderContext
{
    public Func<string, Stream?>? OpenAsset { get; init; }

    public Action<string>? OpenLink { get; init; }
}
