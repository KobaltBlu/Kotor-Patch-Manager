using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;

namespace KPatchLauncher.Markdown;

/// <summary>
/// Markdig pipeline aligned with GitHub Flavored Markdown.
/// </summary>
public static class GithubMarkdownPipeline
{
    private static readonly MarkdownPipeline Pipeline = Create();

    public static MarkdownPipeline Instance => Pipeline;

    public static Markdig.Syntax.MarkdownDocument Parse(string markdown) =>
        Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);

    private static MarkdownPipeline Create() => new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAlertBlocks()
        .Build();
}
