using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace KPatchLauncher.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    private const string RepoBase = "https://github.com/LaneDibello/Kotor-Patch-Manager";

    public AboutViewModel()
    {
        OpenUrlCommand = new SimpleCommand(p => OpenUrl(p as string));
        AppVersion = ResolveVersion();
        RuntimeSummary = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.FrameworkDescription}";
    }

    public string ProductName => "KOTOR PATCH MANAGER";

    public string AppVersion { get; }

    public string VersionDisplay => $"v{AppVersion}";

    public string Description =>
        "Non-destructive runtime patching for Knights of the Old Republic I and II. " +
        "Patches load via DLL injection — the game executables are never modified.";

    public string Copyright =>
        "Copyright © 2025 Lane Dibello and KotOR Patch Manager contributors";

    public string LicenseName => "MIT License";

    public string Disclaimer =>
        "Unofficial fan project. Not affiliated with Lucasfilm Ltd., Disney, Aspyr Media, or BioWare.";

    public string RuntimeSummary { get; }

    public string RepositoryUrl => RepoBase;

    public string ReleasesUrl => $"{RepoBase}/releases";

    public string IssuesUrl => $"{RepoBase}/issues";

    public string LicenseUrl => $"{RepoBase}/blob/master/LICENSE";

    public ICommand OpenUrlCommand { get; }

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip Source Link / commit suffix if present (e.g. "0.1.0+abc123")
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a browser is optional; ignore failures.
        }
    }
}
