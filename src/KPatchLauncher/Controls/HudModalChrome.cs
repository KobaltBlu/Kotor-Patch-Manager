using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KPatchLauncher.Controls;

/// <summary>
/// Shared Escape-to-close and title-strip drag for undecorated HUD modals.
/// </summary>
internal static class HudModalChrome
{
    public static void Attach(Window window, Func<bool>? onEscape = null)
    {
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            if (onEscape != null)
            {
                if (onEscape())
                    e.Handled = true;
                return;
            }

            window.Close();
            e.Handled = true;
        };

        window.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                return;

            // Drag from the top chrome strip (header), not from body controls.
            if (e.GetPosition(window).Y > 44)
                return;

            if (e.Source is Control control)
            {
                for (var c = control; c != null; c = c.Parent as Control)
                {
                    if (c is Button or TextBox or ComboBox or CheckBox or ListBox)
                        return;
                }
            }

            window.BeginMoveDrag(e);
        };
    }
}
