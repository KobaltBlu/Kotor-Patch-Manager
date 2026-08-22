using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KPatchLauncher.Controls;

public partial class HudDialog : Window
{
    public bool Confirmed { get; private set; }

    public HudDialog()
    {
        InitializeComponent();
    }

    public void Configure(string title, string message, string confirmLabel, string? cancelLabel, bool isError)
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;

        if (string.IsNullOrWhiteSpace(cancelLabel))
        {
            CancelButton.IsVisible = false;
        }
        else
        {
            CancelButton.Content = cancelLabel;
            CancelButton.IsVisible = true;
        }

        if (isError)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("pending");
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}
