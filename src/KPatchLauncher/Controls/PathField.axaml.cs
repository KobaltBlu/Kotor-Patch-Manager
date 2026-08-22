using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace KPatchLauncher.Controls;

public partial class PathField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Label), "PATH");

    public static readonly StyledProperty<string> PathTextProperty =
        AvaloniaProperty.Register<PathField, string>(
            nameof(PathText),
            string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<ICommand?> BrowseCommandProperty =
        AvaloniaProperty.Register<PathField, ICommand?>(nameof(BrowseCommand));

    public static readonly StyledProperty<bool> IsBrowseEnabledProperty =
        AvaloniaProperty.Register<PathField, bool>(nameof(IsBrowseEnabled), true);

    public PathField()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string PathText
    {
        get => GetValue(PathTextProperty);
        set => SetValue(PathTextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public ICommand? BrowseCommand
    {
        get => GetValue(BrowseCommandProperty);
        set => SetValue(BrowseCommandProperty, value);
    }

    public bool IsBrowseEnabled
    {
        get => GetValue(IsBrowseEnabledProperty);
        set => SetValue(IsBrowseEnabledProperty, value);
    }
}
