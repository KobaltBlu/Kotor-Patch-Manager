using Avalonia;
using Avalonia.Controls;

namespace KPatchLauncher.Controls;

/// <summary>
/// Framed HUD section. Must be a ContentControl (not UserControl) so child XAML
/// becomes Content inside the chrome template instead of replacing it.
/// </summary>
public class HudPanel : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<HudPanel, string?>(nameof(Header));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<HudPanel, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<HudPanel, object?>(nameof(HeaderContent));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }
}
