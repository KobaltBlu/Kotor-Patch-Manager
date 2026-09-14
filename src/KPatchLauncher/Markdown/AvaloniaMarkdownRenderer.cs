using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace KPatchLauncher.Markdown;

/// <summary>
/// Renders a Markdig AST into native Avalonia controls.
/// </summary>
public static class AvaloniaMarkdownRenderer
{
    private const string MonoClass = "mono";
    private const string LinkClass = "link";

    public static Control Render(MarkdownDocument document, MarkdownRenderContext context)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in document)
        {
            var rendered = RenderBlock(block, context);
            if (rendered != null)
                panel.Children.Add(rendered);
        }

        return panel;
    }

    private static Control? RenderBlock(Block block, MarkdownRenderContext context)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading, context),
            ParagraphBlock paragraph => RenderParagraph(paragraph, context),
            FencedCodeBlock code => RenderCodeBlock(code),
            AlertBlock alert => RenderAlert(alert, context),
            QuoteBlock quote => RenderQuote(quote, context),
            ThematicBreakBlock => RenderRule(),
            ListBlock list => RenderList(list, context, 0),
            Table table => RenderTable(table, context),
            _ => null
        };
    }

    private static Control RenderHeading(HeadingBlock heading, MarkdownRenderContext context)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Classes = { "display" },
            FontSize = heading.Level switch
            {
                1 => 20,
                2 => 18,
                3 => 16,
                _ => 14
            },
            Margin = new Thickness(0, heading.Level <= 2 ? 4 : 2, 0, 0)
        };

        if (heading.Inline != null)
            PopulateInlines(textBlock.Inlines!, heading.Inline, context, FontWeight.SemiBold);

        return textBlock;
    }

    private static Control? RenderParagraph(ParagraphBlock paragraph, MarkdownRenderContext context)
    {
        if (paragraph.Inline == null)
            return null;

        if (TryRenderStandaloneImage(paragraph, context, out var imageControl))
            return imageControl;

        if (ContainsInteractiveInline(paragraph.Inline))
            return RenderInteractiveInlines(paragraph.Inline, context, FontWeight.Normal);

        var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        PopulateInlines(textBlock.Inlines!, paragraph.Inline, context, FontWeight.Normal);
        return textBlock;
    }

    private static bool ContainsInteractiveInline(ContainerInline container)
    {
        foreach (var inline in container)
        {
            if (inline is LinkInline { IsImage: false } or AutolinkInline)
                return true;
            if (inline is ContainerInline nested && ContainsInteractiveInline(nested))
                return true;
        }

        return false;
    }

    private static Control RenderInteractiveInlines(
        ContainerInline container,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        var wrap = new WrapPanel();
        foreach (var inline in container)
            AppendInteractiveInline(wrap, inline, context, baseWeight);
        return wrap;
    }

    private static void AppendInteractiveInline(
        Panel panel,
        Markdig.Syntax.Inlines.Inline inline,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        switch (inline)
        {
            case LiteralInline literal:
                panel.Children.Add(new TextBlock
                {
                    Text = literal.Content.ToString(),
                    FontWeight = baseWeight,
                    TextWrapping = TextWrapping.Wrap
                });
                break;

            case LineBreakInline:
                panel.Children.Add(new TextBlock { Text = " " });
                break;

            case CodeInline code:
                panel.Children.Add(new TextBlock
                {
                    Text = code.Content,
                    Classes = { MonoClass },
                    FontSize = 12
                });
                break;

            case EmphasisInline emphasis:
                panel.Children.Add(new TextBlock
                {
                    Text = GetInlineText(emphasis),
                    FontWeight = emphasis.DelimiterCount >= 2 ? FontWeight.Bold : baseWeight,
                    FontStyle = emphasis.DelimiterCount == 1 ? FontStyle.Italic : FontStyle.Normal,
                    TextDecorations = emphasis.DelimiterChar == '~' ? TextDecorations.Strikethrough : null
                });
                break;

            case LinkInline link when !link.IsImage:
                var button = new Button
                {
                    Content = GetInlineText(link),
                    Classes = { LinkClass },
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                var url = link.Url ?? string.Empty;
                if (context.OpenLink != null && !string.IsNullOrWhiteSpace(url))
                    button.Click += (_, _) => context.OpenLink(url);
                panel.Children.Add(button);
                break;

            case AutolinkInline autolink:
                var autoUrl = autolink.Url ?? autolink.ToString() ?? string.Empty;
                var autoButton = new Button
                {
                    Content = autoUrl,
                    Classes = { LinkClass },
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                if (context.OpenLink != null)
                    autoButton.Click += (_, _) => context.OpenLink(autoUrl);
                panel.Children.Add(autoButton);
                break;

            case ContainerInline nested:
                foreach (var child in nested)
                    AppendInteractiveInline(panel, child, context, baseWeight);
                break;
        }
    }

    private static bool TryRenderStandaloneImage(
        ParagraphBlock paragraph,
        MarkdownRenderContext context,
        out Control? imageControl)
    {
        imageControl = null;
        if (paragraph.Inline == null)
            return false;

        LinkInline? imageLink = null;
        var otherContent = false;

        foreach (var inline in paragraph.Inline)
        {
            if (inline is LinkInline link && link.IsImage)
                imageLink = link;
            else if (inline is LiteralInline literal && string.IsNullOrWhiteSpace(literal.Content.ToString()))
                continue;
            else
                otherContent = true;
        }

        if (imageLink == null || otherContent)
            return false;

        imageControl = CreateImage(imageLink, context);
        return true;
    }

    private static Control RenderCodeBlock(FencedCodeBlock code)
    {
        var text = code.Lines.ToString();
        var language = string.IsNullOrWhiteSpace(code.Info) ? null : code.Info.Trim();

        var stack = new StackPanel { Spacing = 4 };

        if (!string.IsNullOrEmpty(language))
        {
            stack.Children.Add(new TextBlock
            {
                Text = language,
                Classes = { MonoClass },
                FontSize = 11,
                Foreground = Brushes.Gray
            });
        }

        stack.Children.Add(new Border
        {
            Classes = { "md-code-block" },
            Padding = new Thickness(10, 8),
            Child = new TextBlock
            {
                Text = text.TrimEnd(),
                Classes = { MonoClass },
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            }
        });

        return stack;
    }

    private static Control RenderQuote(QuoteBlock quote, MarkdownRenderContext context)
    {
        var inner = new StackPanel { Spacing = 6 };
        foreach (var child in quote)
        {
            var rendered = RenderBlock(child, context);
            if (rendered != null)
                inner.Children.Add(rendered);
        }

        return new Border
        {
            Classes = { "md-blockquote" },
            Padding = new Thickness(12, 4, 0, 4),
            Child = inner
        };
    }

    private static Control RenderAlert(AlertBlock alert, MarkdownRenderContext context)
    {
        var inner = new StackPanel { Spacing = 6 };
        foreach (var child in alert)
        {
            var rendered = RenderBlock(child, context);
            if (rendered != null)
                inner.Children.Add(rendered);
        }

        return new Border
        {
            Classes = { "md-alert", $"md-alert-{alert.Kind.ToString().ToLowerInvariant()}" },
            Padding = new Thickness(12, 8),
            Child = inner
        };
    }

    private static Control RenderRule() =>
        new Border
        {
            Classes = { "md-rule" },
            Height = 1,
            Margin = new Thickness(0, 6)
        };

    private static Control RenderList(ListBlock list, MarkdownRenderContext context, int depth)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(depth * 16, 0, 0, 0) };
        var index = 1;

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
                continue;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            var taskList = TryGetTaskList(listItem, out var isChecked);
            if (taskList)
            {
                row.Children.Add(new CheckBox
                {
                    IsChecked = isChecked,
                    IsEnabled = false,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            else
            {
                var marker = list.IsOrdered ? $"{index}." : "•";
                row.Children.Add(new TextBlock
                {
                    Text = marker,
                    MinWidth = 18,
                    VerticalAlignment = VerticalAlignment.Top,
                    Classes = { MonoClass },
                    FontSize = 12
                });
                if (list.IsOrdered)
                    index++;
            }

            var content = new StackPanel { Spacing = 4 };
            foreach (var child in listItem)
            {
                if (child is ListBlock nestedList)
                    content.Children.Add(RenderList(nestedList, context, depth + 1));
                else
                {
                    var rendered = RenderBlock(child, context);
                    if (rendered != null)
                        content.Children.Add(rendered);
                }
            }

            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control RenderTable(Table table, MarkdownRenderContext context)
    {
        var grid = new Grid
        {
            Classes = { "md-table" },
            ColumnDefinitions = new ColumnDefinitions(
                string.Join(",", Enumerable.Repeat("*", table.ColumnDefinitions.Count)))
        };

        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var columnIndex = 0;

            foreach (var cell in row.OfType<TableCell>())
            {
                var cellContent = new StackPanel { Spacing = 4 };
                foreach (var child in cell)
                {
                    var rendered = RenderBlock(child, context);
                    if (rendered != null)
                        cellContent.Children.Add(rendered);
                }

                var border = new Border
                {
                    Classes = { row.IsHeader ? "md-table-header" : "md-table-cell" },
                    Padding = new Thickness(8, 6),
                    Child = cellContent
                };

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                grid.Children.Add(border);
                columnIndex++;
            }

            rowIndex++;
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = grid
        };
    }

    private static void PopulateInlines(
        InlineCollection inlines,
        ContainerInline container,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        foreach (var inline in container)
            AppendInline(inlines, inline, context, baseWeight);
    }

    private static void AppendInline(
        InlineCollection inlines,
        Markdig.Syntax.Inlines.Inline inline,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        switch (inline)
        {
            case LiteralInline literal:
                inlines.Add(new Run(literal.Content.ToString()) { FontWeight = baseWeight });
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case CodeInline code:
                inlines.Add(new Run(code.Content)
                {
                    FontFamily = Application.Current?.Resources["HudMonoFont"] as FontFamily ?? FontFamily.Default,
                    FontSize = 12,
                    Background = Brushes.Transparent
                });
                break;

            case EmphasisInline emphasis:
                AppendEmphasis(inlines, emphasis, context, baseWeight);
                break;

            case LinkInline link when link.IsImage:
                // Inline images are handled at block level when standalone; skip in flow.
                break;

            case TaskList:
                break;

            case LinkInline link:
                AppendLink(inlines, link, context, baseWeight);
                break;

            case AutolinkInline autolink:
                AppendAutoLink(inlines, autolink, context, baseWeight);
                break;
        }
    }

    private static void AppendEmphasis(
        InlineCollection inlines,
        EmphasisInline emphasis,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        var run = new Run(GetInlineText(emphasis))
        {
            FontWeight = emphasis.DelimiterCount >= 2 ? FontWeight.Bold : baseWeight,
            FontStyle = emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_'
                ? emphasis.DelimiterCount == 1 ? FontStyle.Italic : FontStyle.Normal
                : FontStyle.Normal
        };

        if (emphasis.DelimiterChar == '~')
            run.TextDecorations = TextDecorations.Strikethrough;

        inlines.Add(run);
    }

    private static void AppendLink(
        InlineCollection inlines,
        LinkInline link,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        var text = GetInlineText(link);
        inlines.Add(new Run(text)
        {
            FontWeight = baseWeight,
            Foreground = Application.Current?.FindResource("AccentBrush") as IBrush,
            TextDecorations = TextDecorations.Underline
        });
    }

    private static void AppendAutoLink(
        InlineCollection inlines,
        AutolinkInline autolink,
        MarkdownRenderContext context,
        FontWeight baseWeight)
    {
        var url = autolink.Url ?? autolink.ToString() ?? string.Empty;
        inlines.Add(new Run(url)
        {
            FontWeight = baseWeight,
            Foreground = Application.Current?.FindResource("AccentBrush") as IBrush,
            TextDecorations = TextDecorations.Underline
        });
    }

    private static Control CreateImage(LinkInline link, MarkdownRenderContext context)
    {
        var image = new Image
        {
            Stretch = Stretch.None,
            MaxWidth = 480,
            Margin = new Thickness(0, 4)
        };

        var bitmap = KPatchImageLoader.TryLoad(link.Url ?? string.Empty, context.OpenAsset);
        if (bitmap != null)
            image.Source = bitmap;
        else
        {
            image.Source = null;
            return new TextBlock
            {
                Text = $"[image: {link.Url}]",
                Classes = { MonoClass },
                FontSize = 12,
                Foreground = Brushes.Gray
            };
        }

        if (!string.IsNullOrWhiteSpace(link.Title))
            ToolTip.SetTip(image, link.Title);

        return image;
    }

    private static bool TryGetTaskList(ListItemBlock listItem, out bool isChecked)
    {
        isChecked = false;
        foreach (var block in listItem)
        {
            if (block is not ParagraphBlock { Inline: not null } paragraph)
                continue;

            foreach (var inline in paragraph.Inline)
            {
                if (inline is TaskList task)
                {
                    isChecked = task.Checked;
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetInlineText(ContainerInline container)
    {
        var writer = new System.IO.StringWriter();
        foreach (var child in container)
        {
            if (child is LiteralInline literal)
                writer.Write(literal.Content);
            else if (child is ContainerInline nested)
                writer.Write(GetInlineText(nested));
        }

        return writer.ToString();
    }
}
