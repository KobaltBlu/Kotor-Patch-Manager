using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using KPatchLauncher.Markdown;

namespace KPatchLauncher.Controls;

public partial class PatchMarkdownViewer : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<PatchMarkdownViewer, string?>(nameof(Markdown));

    public static readonly StyledProperty<Func<string, Stream?>?> OpenAssetProperty =
        AvaloniaProperty.Register<PatchMarkdownViewer, Func<string, Stream?>?>(nameof(OpenAsset));

    public static readonly StyledProperty<ICommand?> OpenLinkCommandProperty =
        AvaloniaProperty.Register<PatchMarkdownViewer, ICommand?>(nameof(OpenLinkCommand));

    private readonly ContentControl _host;

    static PatchMarkdownViewer()
    {
        MarkdownProperty.Changed.AddClassHandler<PatchMarkdownViewer>((viewer, _) => viewer.RenderMarkdown());
        OpenAssetProperty.Changed.AddClassHandler<PatchMarkdownViewer>((viewer, _) => viewer.RenderMarkdown());
        OpenLinkCommandProperty.Changed.AddClassHandler<PatchMarkdownViewer>((viewer, _) => viewer.RenderMarkdown());
    }

    public PatchMarkdownViewer()
    {
        _host = new ContentControl();
        Content = _host;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Func<string, Stream?>? OpenAsset
    {
        get => GetValue(OpenAssetProperty);
        set => SetValue(OpenAssetProperty, value);
    }

    public ICommand? OpenLinkCommand
    {
        get => GetValue(OpenLinkCommandProperty);
        set => SetValue(OpenLinkCommandProperty, value);
    }

    private void RenderMarkdown()
    {
        var markdown = Markdown ?? string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            _host.Content = null;
            return;
        }

        var document = GithubMarkdownPipeline.Parse(markdown);
        var context = new MarkdownRenderContext
        {
            OpenAsset = OpenAsset,
            OpenLink = url =>
            {
                if (OpenLinkCommand?.CanExecute(url) == true)
                    OpenLinkCommand.Execute(url);
            }
        };

        _host.Content = AvaloniaMarkdownRenderer.Render(document, context);
    }
}
