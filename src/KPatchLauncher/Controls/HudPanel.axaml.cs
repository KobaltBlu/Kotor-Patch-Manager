using Avalonia;
using Avalonia.Controls;

namespace KPatchLauncher.Controls;

public partial class HudPanel : UserControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<HudPanel, string?>(nameof(Header));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<HudPanel, object?>(nameof(HeaderContent));

    public HudPanel()
    {
        InitializeComponent();
    }

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }
}
