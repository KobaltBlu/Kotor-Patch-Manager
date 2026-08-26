using Avalonia.Controls;
using KPatchLauncher.Controls;

namespace KPatchLauncher.ViewModels;

public sealed class WindowDialogService : IDialogService
{
    private readonly Window _owner;

    public WindowDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "CONFIRM", string cancelLabel = "CANCEL")
    {
        var dialog = new HudDialog();
        dialog.Configure(title, message, confirmLabel, cancelLabel, isError: false);
        var result = await dialog.ShowDialog<bool>(_owner);
        return result;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new HudDialog();
        dialog.Configure(title, message, "OK", null, isError: true);
        await dialog.ShowDialog<bool>(_owner);
    }

    public async Task ShowAboutAsync()
    {
        var dialog = new AboutWindow();
        await dialog.ShowDialog(_owner);
    }

    public async Task ShowLoadoutManagerAsync(MainViewModel viewModel)
    {
        var dialog = new LoadoutManagerWindow(viewModel);
        await dialog.ShowDialog(_owner);
    }

    public async Task ShowSettingsAsync(MainViewModel viewModel)
    {
        var dialog = new SettingsWindow(viewModel);
        await dialog.ShowDialog(_owner);
    }
}
