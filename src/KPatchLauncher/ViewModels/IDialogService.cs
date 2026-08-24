namespace KPatchLauncher.ViewModels;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "CONFIRM", string cancelLabel = "CANCEL");
    Task ShowErrorAsync(string title, string message);
    Task ShowAboutAsync();
}
