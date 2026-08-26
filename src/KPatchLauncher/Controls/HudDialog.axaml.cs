using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KPatchLauncher.Controls;

public partial class HudDialog : Window
{
    public bool Confirmed { get; private set; }

    public HudDialog()
    {
        InitializeComponent();
        HudModalChrome.Attach(this, () =>
        {
            Confirmed = false;
            Close(false);
            return true;
        });
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
            ConfirmButton.Classes.Remove("pending");
            ConfirmButton.Classes.Add("destructive");
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
