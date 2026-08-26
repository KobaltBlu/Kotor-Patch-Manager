using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KPatchCore.Managers;
using KPatchCore.Models;
using KPatchCore.Applicators;
using KPatchCore.Detectors;
using KPatchCore.Launcher;
using KPatchCore.Validators;
using KPatchLauncher.Models;

namespace KPatchLauncher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private string _gamePath = string.Empty;
    private string _patchesPath = string.Empty;
    private string _statusMessage = "Ready";
    private string _kotorVersion = "Unknown";
    private GameVersion? _detectedGameVersion;
    private PatchItemViewModel? _selectedPatch;
    private PatchRepository? _repository;
    private readonly HashSet<string> _installedPatchIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;
    private bool _hasInstalledPatches;
    private bool _isOperationInProgress;
    private double _progressValue = 0;
    private bool? _selectAllPatches = false;
    private bool _isUpdatingSelectAllState;
    private bool _isBulkUpdatingPatchChecks;
    private int _patchStatusRequestVersion;
    private int _patchLoadRequestVersion;
    private bool _useCustomLaunch;
    private string _customLaunchCommand = string.Empty;
    private string _searchText = string.Empty;
    private bool _showIncompatible;
    private string _dependencyWarning = string.Empty;
    private readonly List<string> _preferredInstallOrder = new();
    private bool _isWorkspaceNarrow;
    private LibrarySortOption? _selectedLibrarySortOption;

    public MainViewModel()
    {
        AllPatches = new ObservableCollection<PatchItemViewModel>();

        // Every structural change to AllPatches (load, orphan insert/remove, move-to-top,
        // manual reorder) has to reach the list control, so mirror them into VisiblePatches.
        // Compatibility changes are not collection changes, so UpdatePatchCompatibility()
        // syncs explicitly as well.
        AllPatches.CollectionChanged += (_, _) => SyncVisiblePatches();

        // Load settings. Pending patch selections are intentionally not restored at startup;
        // installed patch state is reloaded from the selected game's patch_config.toml.
        _settings = AppSettings.Load();
        _gamePath = _settings.GamePath;
        _patchesPath = _settings.PatchesPath;
        _useCustomLaunch = _settings.LaunchMethod == LaunchMethod.Custom;
        _customLaunchCommand = _settings.CustomLaunchCommand;
        _showIncompatible = _settings.ShowIncompatible;
        ClearPersistedPatchSelection();

        BrowseGameCommand = new SimpleCommand(async () => await BrowseGame(), () => CanEditPaths);
        BrowsePatchesCommand = new SimpleCommand(async () => await BrowsePatches(), () => CanEditPaths);
        AddPatchCommand = new SimpleCommand(async () => await AddPatchViaPickerAsync(), () => CanEditPaths);
        RefreshCommand = new SimpleCommand(async () => await Refresh(), () => CanEditPaths);
        MoveUpCommand = new SimpleCommand(p => MovePatch(p, -1), p => CanMovePatch(p, -1));
        MoveDownCommand = new SimpleCommand(p => MovePatch(p, 1), p => CanMovePatch(p, 1));
        ApplyPatchesCommand = new SimpleCommand(async () => await ApplyPatches(), () => CanEditPaths && HasValidGamePath);
        UninstallAllCommand = new SimpleCommand(async () => await UninstallAll(), () => HasInstalledPatches && CanEditPaths);
        LaunchGameCommand = new SimpleCommand(async () => await LaunchGame(), () => HasValidGamePath);
        SelectPatchCommand = new SimpleCommand(p => SelectPatchById(p as string));
        OpenUrlCommand = new SimpleCommand(p => OpenUrl(p as string));
        OpenAboutCommand = new SimpleCommand(async () => await OpenAboutAsync());

        InitSystemsConsole();
        InitLibrarySortOptions();

        // Load patches if path is set
        if (!string.IsNullOrWhiteSpace(_patchesPath))
        {
            _ = LoadPatchesFromDirectoryAsync(_patchesPath);
        }
    }

    public ObservableCollection<PatchItemViewModel> AllPatches { get; }

    /// <summary>
    /// The compatible subset of <see cref="AllPatches"/>, in the same order, as bound by the
    /// patch list. This is a single long-lived collection that is reconciled in place: the
    /// list control must never have its ItemsSource swapped for a differently ordered
    /// snapshot, which leaves recycled rows showing another patch's name and checkbox.
    /// </summary>
    public ObservableCollection<PatchItemViewModel> VisiblePatches { get; } = new();

    public IDialogService? Dialogs { get; set; }

    public bool HasVisiblePatches => VisiblePatches.Count > 0;

    public bool HasValidGamePath => !string.IsNullOrWhiteSpace(_gamePath) && File.Exists(_gamePath);

    public bool IsFirstRun => !HasValidGamePath;

    public bool HasConfiguredPaths =>
        HasValidGamePath
        && !string.IsNullOrWhiteSpace(_patchesPath)
        && Directory.Exists(_patchesPath);

    public string GamePathDisplay =>
        HasValidGamePath ? Path.GetFileName(_gamePath) : string.Empty;

    public string PatchesPathDisplay =>
        !string.IsNullOrWhiteSpace(_patchesPath) && Directory.Exists(_patchesPath)
            ? Path.GetFileName(_patchesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : string.Empty;

    public bool CanEditPaths => !IsOperationInProgress;

    public bool HasPendingChanges => PendingChangesCount > 0;

    public bool IsWorkspaceNarrow
    {
        get => _isWorkspaceNarrow;
        private set
        {
            if (SetProperty(ref _isWorkspaceNarrow, value))
                OnPropertyChanged(nameof(PatchesColumnSpan));
        }
    }

    /// <summary>
    /// When narrow, the patches list spans both workspace columns; when wide, it uses column 0 only.
    /// </summary>
    public int PatchesColumnSpan => IsWorkspaceNarrow ? 2 : 1;

    /// <summary>
    /// Compact health strip when staging is healthy (detail reserved for problems).
    /// </summary>
    public bool ShowCompactHealth => HealthOk;

    public bool ShowExpandedHealth => !HealthOk;

    public ObservableCollection<LibrarySortOption> LibrarySortOptions { get; } = new();

    public LibrarySortOption? SelectedLibrarySortOption
    {
        get => _selectedLibrarySortOption;
        set
        {
            if (SetProperty(ref _selectedLibrarySortOption, value) && value != null)
                LibrarySortMode = value.Id;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                SyncVisiblePatches();
            }
        }
    }

    public bool ShowIncompatible
    {
        get => _showIncompatible;
        set
        {
            if (SetProperty(ref _showIncompatible, value))
            {
                _settings.ShowIncompatible = value;
                _settings.Save();
                SyncVisiblePatches();
            }
        }
    }

    public string DependencyWarning
    {
        get => _dependencyWarning;
        private set
        {
            if (SetProperty(ref _dependencyWarning, value))
            {
                OnPropertyChanged(nameof(HasDependencyWarning));
            }
        }
    }

    public bool HasDependencyWarning => !string.IsNullOrWhiteSpace(DependencyWarning);

    public bool? SelectAllPatches
    {
        get => _selectAllPatches;
        set
        {
            if (_isUpdatingSelectAllState)
            {
                SetProperty(ref _selectAllPatches, value);
                return;
            }

            var requestedState = value == true;
            if (SetProperty(ref _selectAllPatches, (bool?)requestedState))
            {
                SetPatchSelectionFromSelectAll(requestedState);
            }
        }
    }

    public PatchItemViewModel? SelectedPatch
    {
        get => _selectedPatch;
        set
        {
            if (SetProperty(ref _selectedPatch, value))
            {
                UpdateDependencyWarning();
                RaiseMoveCommandsCanExecute();
            }
        }
    }

    public bool HasInstalledPatches
    {
        get => _hasInstalledPatches;
        private set
        {
            if (SetProperty(ref _hasInstalledPatches, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        private set
        {
            if (SetProperty(ref _isOperationInProgress, value))
            {
                OnPropertyChanged(nameof(CanEditPaths));
                RefreshCommandStates();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string GamePath
    {
        get => _gamePath;
        set
        {
            if (SetProperty(ref _gamePath, value))
            {
                _settings.GamePath = value;
                UpdateGameBrowseDirectory(value);
                _settings.Save();
                OnPropertyChanged(nameof(HasValidGamePath));
                OnPropertyChanged(nameof(IsFirstRun));
                NotifyConfiguredPathsChanged();
                RefreshCommandStates();

                InvalidatePatchStateForGamePathChange();

                // Check patch status when game path is set
                if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                {
                    _ = CheckPatchStatusAsync(value);
                }
            }
        }
    }

    public string PatchesPath
    {
        get => _patchesPath;
        set
        {
            if (SetProperty(ref _patchesPath, value))
            {
                _settings.PatchesPath = value;
                UpdatePatchesBrowseDirectory(value);
                _settings.Save();

                ClearAllPatchSelections(clearInstalledState: true, removeOrphanedPatches: true);

                // Load patches from new directory
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _ = LoadPatchesFromDirectoryAsync(value);
                }
                else
                {
                    _repository = null;
                    _patchLoadRequestVersion++;
                    ClearPatchViewModels();
                    SelectedPatch = null;
                    UpdateSelectAllState();
                    UpdatePendingChanges();
                }

                NotifyConfiguredPathsChanged();
            }
        }
    }

    private void NotifyConfiguredPathsChanged()
    {
        OnPropertyChanged(nameof(HasConfiguredPaths));
        OnPropertyChanged(nameof(GamePathDisplay));
        OnPropertyChanged(nameof(PatchesPathDisplay));
    }

    /// <summary>
    /// Whether the launch-method controls apply. Only the proxy deployment method
    /// reads them (Steam or a custom command); the injection method ignores launch
    /// config, so they stay hidden there.
    /// </summary>
    public bool ShowLaunchSettings =>
        DeploymentPolicy.ForCurrentPlatform() == DeploymentMethod.Proxy;

    /// <summary>
    /// When true, the game is launched with <see cref="CustomLaunchCommand"/>
    /// instead of through Steam. Applies to the proxy deployment method.
    /// </summary>
    public bool UseCustomLaunch
    {
        get => _useCustomLaunch;
        set
        {
            if (SetProperty(ref _useCustomLaunch, value))
            {
                _settings.LaunchMethod = value ? LaunchMethod.Custom : LaunchMethod.Steam;
                _settings.Save();
            }
        }
    }

    /// <summary>
    /// Command used when <see cref="UseCustomLaunch"/> is set. "{exe}" is replaced
    /// with the game executable path.
    /// </summary>
    public string CustomLaunchCommand
    {
        get => _customLaunchCommand;
        set
        {
            if (SetProperty(ref _customLaunchCommand, value))
            {
                _settings.CustomLaunchCommand = value;
                _settings.Save();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string KotorVersion
    {
        get => _kotorVersion;
        set => SetProperty(ref _kotorVersion, value);
    }

    public int PendingChangesCount
    {
        get
        {
            var checkedPatches = AllPatches
                .Where(p => p.IsChecked && !p.IsOrphaned)
                .Select(p => p.Id)
                .OrderBy(x => x)
                .ToList();

            var installedPatches = AllPatches
                .Where(p => !p.IsOrphaned && IsInstalled(p.Id))
                .Select(p => p.Id)
                .OrderBy(x => x)
                .ToList();

            if (checkedPatches.SequenceEqual(installedPatches))
                return 0;

            return checkedPatches.Union(installedPatches).Except(checkedPatches.Intersect(installedPatches)).Count();
        }
    }

    public string PendingChangesMessage => PendingChangesCount == 0
        ? "No pending changes"
        : $"{PendingChangesCount} patch{(PendingChangesCount == 1 ? "" : "es")} pending";

    public ICommand BrowseGameCommand { get; }
    public ICommand BrowsePatchesCommand { get; }
    public ICommand AddPatchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ApplyPatchesCommand { get; }
    public ICommand UninstallAllCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand SelectPatchCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand OpenAboutCommand { get; }

    /// <summary>
    /// Updates narrow/wide workspace flag from the main window width (no side effects).
    /// </summary>
    public void NotifyWindowWidth(double width)
    {
        IsWorkspaceNarrow = width < 900;
    }

    private void InitLibrarySortOptions()
    {
        LibrarySortOptions.Add(new LibrarySortOption("name", "Name"));
        LibrarySortOptions.Add(new LibrarySortOption("author", "Author"));
        LibrarySortOptions.Add(new LibrarySortOption("installed", "Installed"));
        LibrarySortOptions.Add(new LibrarySortOption("pending", "Pending"));

        var mode = string.IsNullOrWhiteSpace(_librarySortMode) ? "name" : _librarySortMode;
        _selectedLibrarySortOption = LibrarySortOptions.FirstOrDefault(o =>
                                       string.Equals(o.Id, mode, StringComparison.OrdinalIgnoreCase))
                                   ?? LibrarySortOptions[0];
        _librarySortMode = _selectedLibrarySortOption.Id;
    }

    private bool IsInstalled(string patchId) => _installedPatchIds.Contains(patchId);

    private async Task OpenAboutAsync()
    {
        if (Dialogs == null)
            return;

        await Dialogs.ShowAboutAsync();
    }

    private async Task BrowseGame()
    {
        try
        {
            var window = GetMainWindow();
            if (window == null)
            {
                StatusMessage = "Error: Could not access window";
                return;
            }

            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game Executable",
                AllowMultiple = false,
                SuggestedStartLocation = await TryGetSuggestedStartFolderAsync(window, GetGameBrowseStartDirectory()),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Game Executables")
                    {
                        // swkotor.exe / swkotor2.exe on Windows and under Wine/Proton; the
                        // native Linux KOTOR II build is an extensionless "KOTOR2" ELF.
                        Patterns = new[] { "*.exe", "KOTOR2" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (result.Count > 0)
            {
                GamePath = result[0].Path.LocalPath;
                StatusMessage = $"Selected game: {Path.GetFileName(GamePath)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error browsing: {ex.Message}";
        }
    }

    private async Task BrowsePatches()
    {
        try
        {
            var window = GetMainWindow();
            if (window == null)
            {
                StatusMessage = "Error: Could not access window";
                return;
            }

            var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Patches Directory",
                AllowMultiple = false,
                SuggestedStartLocation = await TryGetSuggestedStartFolderAsync(window, GetPatchesBrowseStartDirectory())
            });

            if (result.Count > 0)
            {
                PatchesPath = result[0].Path.LocalPath;
                StatusMessage = $"Selected patches directory: {Path.GetFileName(PatchesPath)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error browsing: {ex.Message}";
        }
    }

    private async Task AddPatchViaPickerAsync()
    {
        try
        {
            var window = GetMainWindow();
            if (window == null)
            {
                StatusMessage = "Error: Could not access window";
                return;
            }

            if (string.IsNullOrWhiteSpace(PatchesPath) || !Directory.Exists(PatchesPath))
            {
                StatusMessage = "Set a patches folder first (TARGET → PATCHES).";
                return;
            }

            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add .kpatch file(s)",
                AllowMultiple = true,
                SuggestedStartLocation = await TryGetSuggestedStartFolderAsync(window, GetPatchesBrowseStartDirectory()),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KotOR Patch")
                    {
                        Patterns = new[] { "*.kpatch" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (result.Count == 0)
                return;

            var paths = result
                .Select(f => f.Path.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            await AddKpatchFilesAsync(paths);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding patch: {ex.Message}";
        }
    }

    /// <summary>
    /// Copies .kpatch files into <see cref="PatchesPath"/> and reloads the library.
    /// Used by the ADD picker and drag-drop.
    /// </summary>
    public async Task AddKpatchFilesAsync(IReadOnlyList<string> paths)
    {
        if (string.IsNullOrWhiteSpace(PatchesPath) || !Directory.Exists(PatchesPath))
        {
            StatusMessage = "Set a patches folder first (TARGET → PATCHES).";
            return;
        }

        var candidates = paths
            .Where(p => !string.IsNullOrWhiteSpace(p)
                        && File.Exists(p)
                        && string.Equals(Path.GetExtension(p), ".kpatch", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            StatusMessage = "No .kpatch files to add.";
            return;
        }

        var validator = new PatchRepository(PatchesPath);
        var added = 0;
        var overwritten = 0;
        var errors = new List<string>();

        foreach (var sourcePath in candidates)
        {
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(PatchesPath, fileName);

            // Skip no-op when dropping a file that is already the library copy.
            if (string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(destPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var loadResult = validator.LoadPatch(sourcePath);
            if (!loadResult.Success)
            {
                errors.Add($"{fileName}: {loadResult.Error}");
                continue;
            }

            try
            {
                var existed = File.Exists(destPath);
                await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));
                if (existed)
                    overwritten++;
                else
                    added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{fileName}: {ex.Message}");
            }
        }

        if (added + overwritten > 0)
            await LoadPatchesFromDirectoryAsync(PatchesPath);

        var parts = new List<string>();
        if (added > 0)
            parts.Add($"Added {added}");
        if (overwritten > 0)
            parts.Add($"overwrote {overwritten}");
        if (errors.Count > 0)
            parts.Add($"{errors.Count} failed");

        StatusMessage = parts.Count > 0
            ? string.Join("; ", parts) + (errors.Count > 0 ? $" — {errors[0]}" : string.Empty)
            : errors.Count > 0
                ? $"Failed to add patches: {errors[0]}"
                : "No patches added.";
    }

    private async Task Refresh()
    {
        try
        {
            SetOperationInProgress(true, "Refreshing...");

            // Reload patches from directory if path is set
            if (!string.IsNullOrWhiteSpace(PatchesPath))
            {
                await LoadPatchesFromDirectoryAsync(PatchesPath);
            }

            SetOperationInProgress(false, "Refresh complete");
        }
        catch (Exception ex)
        {
            SetOperationInProgress(false, $"Error refreshing: {ex.Message}");
        }
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private string? GetGameBrowseStartDirectory()
    {
        return GetDirectoryForExistingFile(GamePath)
            ?? GetExistingDirectory(_settings.LastGameBrowseDirectory);
    }

    private string? GetPatchesBrowseStartDirectory()
    {
        return GetExistingDirectory(PatchesPath)
            ?? GetExistingDirectory(_settings.LastPatchesBrowseDirectory);
    }

    private async Task<IStorageFolder?> TryGetSuggestedStartFolderAsync(Window window, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            return await window.StorageProvider.TryGetFolderFromPathAsync(directory);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetDirectoryForExistingFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        return GetExistingDirectory(Path.GetDirectoryName(filePath));
    }

    private static string? GetExistingDirectory(string? directory)
    {
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : null;
    }

    private void UpdateGameBrowseDirectory(string gamePath)
    {
        var directory = GetDirectoryForExistingFile(gamePath);
        if (directory != null)
        {
            _settings.LastGameBrowseDirectory = directory;
        }
    }

    private void UpdatePatchesBrowseDirectory(string patchesPath)
    {
        var directory = GetExistingDirectory(patchesPath);
        if (directory != null)
        {
            _settings.LastPatchesBrowseDirectory = directory;
        }
    }

    private void OnPatchCheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not PatchItemViewModel patch)
            return;

        if (_isBulkUpdatingPatchChecks)
            return;

        if (patch.IsOrphaned)
        {
            RemoveFromPreferredInstallOrder(patch.Id);
        }
        else if (patch.IsChecked)
        {
            AppendToPreferredInstallOrder(patch.Id);
        }
        else
        {
            RemoveFromPreferredInstallOrder(patch.Id);
        }

        RefreshInstallOrderIndicators();
        SaveCheckedPatches();
        UpdatePendingChanges();
        UpdateSelectAllState();
        UpdateDependencyWarning();
        RaiseMoveCommandsCanExecute();
    }

    private void SetPatchSelectionFromSelectAll(bool isChecked)
    {
        var targetPatches = isChecked
            ? VisiblePatches.Where(p => !p.IsOrphaned).ToList()
            : AllPatches.ToList();

        if (targetPatches.Count == 0)
        {
            UpdateSelectAllState();
            return;
        }

        _isBulkUpdatingPatchChecks = true;
        try
        {
            foreach (var patch in targetPatches)
            {
                patch.IsChecked = isChecked;
            }
        }
        finally
        {
            _isBulkUpdatingPatchChecks = false;
        }

        ReconcilePreferredInstallOrder();
        SaveCheckedPatches();
        UpdatePendingChanges();
        UpdateSelectAllState();
        StatusMessage = isChecked
            ? $"Selected {targetPatches.Count} visible patch{(targetPatches.Count == 1 ? "" : "es")}"
            : "Cleared all patch selections";
    }

    private void UpdateSelectAllState()
    {
        var visiblePatches = VisiblePatches.Where(p => !p.IsOrphaned).ToList();
        bool? newState;

        if (visiblePatches.Count == 0)
        {
            newState = false;
        }
        else
        {
            var checkedCount = visiblePatches.Count(p => p.IsChecked);
            newState = checkedCount == 0
                ? false
                : checkedCount == visiblePatches.Count
                    ? true
                    : null;
        }

        _isUpdatingSelectAllState = true;
        try
        {
            SelectAllPatches = newState;
        }
        finally
        {
            _isUpdatingSelectAllState = false;
        }

        OnPropertyChanged(nameof(HasVisiblePatches));
    }

    private bool CanMovePatch(object? parameter, int delta)
    {
        var patch = parameter as PatchItemViewModel ?? SelectedPatch;
        if (patch == null || !patch.IsChecked || patch.IsOrphaned)
            return false;

        var index = IndexInPreferredInstallOrder(patch.Id);
        var target = index + delta;
        return index >= 0 && target >= 0 && target < _preferredInstallOrder.Count;
    }

    private void MovePatch(object? parameter, int delta)
    {
        var patch = parameter as PatchItemViewModel ?? SelectedPatch;
        if (patch == null || !CanMovePatch(patch, delta))
            return;

        var index = IndexInPreferredInstallOrder(patch.Id);
        var target = index + delta;
        (_preferredInstallOrder[index], _preferredInstallOrder[target]) =
            (_preferredInstallOrder[target], _preferredInstallOrder[index]);

        RefreshInstallOrderIndicators();
        StatusMessage = $"Install order: {patch.Name} {(delta < 0 ? "earlier" : "later")}";
        RecomputeLoadoutDirty();
        RaiseMoveCommandsCanExecute();
    }

    private void RaiseMoveCommandsCanExecute()
    {
        if (MoveUpCommand is SimpleCommand up)
            up.RaiseCanExecuteChanged();
        if (MoveDownCommand is SimpleCommand down)
            down.RaiseCanExecuteChanged();
    }

    private int IndexInPreferredInstallOrder(string patchId)
    {
        for (var i = 0; i < _preferredInstallOrder.Count; i++)
        {
            if (string.Equals(_preferredInstallOrder[i], patchId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void AppendToPreferredInstallOrder(string patchId)
    {
        if (string.IsNullOrWhiteSpace(patchId))
            return;
        if (IndexInPreferredInstallOrder(patchId) >= 0)
            return;
        _preferredInstallOrder.Add(patchId);
    }

    private void RemoveFromPreferredInstallOrder(string patchId)
    {
        var index = IndexInPreferredInstallOrder(patchId);
        if (index >= 0)
            _preferredInstallOrder.RemoveAt(index);
    }

    /// <summary>
    /// Sets preferred install order from an ordered id list, keeping only ids present
    /// in the library (non-orphaned). Does not change checkboxes.
    /// </summary>
    private void SetPreferredInstallOrder(IEnumerable<string> preferredIds)
    {
        var known = AllPatches
            .Where(p => !p.IsOrphaned)
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        _preferredInstallOrder.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in preferredIds)
        {
            if (!known.TryGetValue(id, out var patch))
                continue;
            if (!seen.Add(patch.Id))
                continue;
            _preferredInstallOrder.Add(patch.Id);
        }

        RefreshInstallOrderIndicators();
        RaiseMoveCommandsCanExecute();
    }

    /// <summary>
    /// Keeps preferred order for still-checked patches; appends newly checked ids.
    /// </summary>
    private void ReconcilePreferredInstallOrder()
    {
        var checkedPatches = AllPatches
            .Where(p => p.IsChecked && !p.IsOrphaned)
            .ToList();
        var checkedIds = checkedPatches
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _preferredInstallOrder.RemoveAll(id => !checkedIds.Contains(id));

        var present = _preferredInstallOrder
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var patch in checkedPatches)
        {
            if (present.Add(patch.Id))
                _preferredInstallOrder.Add(patch.Id);
        }

        RefreshInstallOrderIndicators();
        RecomputeLoadoutDirty();
        RaiseMoveCommandsCanExecute();
    }

    private void RefreshInstallOrderIndicators()
    {
        var orderById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _preferredInstallOrder.Count; i++)
            orderById[_preferredInstallOrder[i]] = i + 1;

        foreach (var patch in AllPatches)
        {
            if (patch.IsChecked && !patch.IsOrphaned && orderById.TryGetValue(patch.Id, out var order))
                patch.DisplayOrder = order;
            else
                patch.DisplayOrder = 0;
        }
    }

    private List<string> GetPreferredCheckedPatchIds(bool compatibleOnly)
    {
        var checkedIds = AllPatches
            .Where(p => p.IsChecked && !p.IsOrphaned && (!compatibleOnly || p.IsCompatible))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        foreach (var id in _preferredInstallOrder)
        {
            if (checkedIds.Remove(id))
                ordered.Add(id);
        }

        // Any checked ids missing from preferred order (should be rare) append in library order.
        foreach (var patch in AllPatches)
        {
            if (checkedIds.Remove(patch.Id))
                ordered.Add(patch.Id);
        }

        return ordered;
    }

    private void SelectPatchById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        var match = AllPatches.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return;

        if (!VisiblePatches.Contains(match) && !match.IsCompatible && !match.IsOrphaned)
        {
            ShowIncompatible = true;
        }

        SelectedPatch = match;
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

    private void InvalidatePatchStateForGamePathChange()
    {
        ClearAllPatchSelections(clearInstalledState: true, removeOrphanedPatches: true);
        _detectedGameVersion = null;
        KotorVersion = "Unknown";
        UpdatePatchCompatibility();
    }

    private void ClearAllPatchSelections(bool clearInstalledState, bool removeOrphanedPatches)
    {
        _patchStatusRequestVersion++;

        if (clearInstalledState)
        {
            _installedPatchIds.Clear();
            HasInstalledPatches = false;
        }

        SelectedPatch = null;

        if (removeOrphanedPatches)
        {
            RemoveOrphanedPatches();
        }

        _isBulkUpdatingPatchChecks = true;
        try
        {
            foreach (var patch in AllPatches)
            {
                patch.IsChecked = false;
            }
        }
        finally
        {
            _isBulkUpdatingPatchChecks = false;
        }

        ClearPersistedPatchSelection();
        _preferredInstallOrder.Clear();
        RefreshInstallOrderIndicators();
        UpdateSelectAllState();
        UpdatePendingChanges();
        RaiseMoveCommandsCanExecute();
    }

    private void SyncPatchSelectionWithInstalledPatches(IEnumerable<string> installedPatchIds)
    {
        var normalizedInstalledIds = installedPatchIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _installedPatchIds.Clear();
        foreach (var patchId in normalizedInstalledIds)
        {
            _installedPatchIds.Add(patchId);
        }

        HasInstalledPatches = _installedPatchIds.Count > 0;
        RemoveOrphanedPatches();

        _isBulkUpdatingPatchChecks = true;
        try
        {
            foreach (var patch in AllPatches)
            {
                patch.IsChecked = _installedPatchIds.Contains(patch.Id);
            }
        }
        finally
        {
            _isBulkUpdatingPatchChecks = false;
        }

        foreach (var patchId in normalizedInstalledIds)
        {
            if (AllPatches.Any(p => string.Equals(p.Id, patchId, StringComparison.OrdinalIgnoreCase)))
                continue;

            var orphanedPatch = new PatchItemViewModel
            {
                Id = patchId,
                Name = $"{patchId} (not found)",
                Version = "?",
                Author = "Unknown",
                Description = "This patch is installed but not found in patches directory",
                IsOrphaned = true,
                IsChecked = true,
                IsCompatible = false,
                CompatibilityStatus = "Patch files not found"
            };
            orphanedPatch.CheckedChanged += OnPatchCheckedChanged;
            AllPatches.Insert(0, orphanedPatch);
        }

        SetPreferredInstallOrder(normalizedInstalledIds);
        ReconcilePreferredInstallOrder();
        SaveCheckedPatches();
        UpdateSelectAllState();
        UpdatePendingChanges();
    }

    private void RemoveOrphanedPatches()
    {
        for (var i = AllPatches.Count - 1; i >= 0; i--)
        {
            if (!AllPatches[i].IsOrphaned)
                continue;

            AllPatches[i].CheckedChanged -= OnPatchCheckedChanged;
            AllPatches.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops every patch view model, detaching the check handlers first so discarded
    /// view models cannot keep driving this one.
    /// </summary>
    private void ClearPatchViewModels()
    {
        foreach (var patch in AllPatches)
        {
            patch.CheckedChanged -= OnPatchCheckedChanged;
        }

        AllPatches.Clear();
        _preferredInstallOrder.Clear();
    }

    /// <summary>
    /// Reconciles <see cref="VisiblePatches"/> with the compatible subset of
    /// <see cref="AllPatches"/> using in-place inserts, moves and removes. Rebuilding the
    /// collection with Clear/Add instead would push a Reset at the list control and cost
    /// row identity, so a reorder could leave a row rendering the previous patch.
    /// </summary>
    private void SyncVisiblePatches()
    {
        IEnumerable<PatchItemViewModel> query = AllPatches;
        if (!ShowIncompatible)
        {
            query = query.Where(p => p.IsCompatible || p.IsOrphaned);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Author.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Id.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.TagsText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var desired = ApplyLibrarySort(query).ToList();
        var desiredSet = new HashSet<PatchItemViewModel>(desired);

        for (var i = VisiblePatches.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(VisiblePatches[i]))
            {
                VisiblePatches.RemoveAt(i);
            }
        }

        // Everything left is also in desired, so each remaining item is at or after its
        // target index and this walk places them all without disturbing settled rows.
        for (var i = 0; i < desired.Count; i++)
        {
            var patch = desired[i];
            var current = VisiblePatches.IndexOf(patch);

            if (current < 0)
            {
                VisiblePatches.Insert(i, patch);
            }
            else if (current != i)
            {
                VisiblePatches.Move(current, i);
            }
        }

        OnPropertyChanged(nameof(HasVisiblePatches));
        RaiseMoveCommandsCanExecute();
    }

    private void ClearPersistedPatchSelection()
    {
        if (_settings.CheckedPatchIds.Count == 0)
            return;

        _settings.CheckedPatchIds.Clear();
        _settings.Save();
    }

    private void SaveCheckedPatches()
    {
        _settings.CheckedPatchIds = GetPreferredCheckedPatchIds(compatibleOnly: false);
        _settings.Save();
    }

    private void UpdatePendingChanges()
    {
        UpdateInstallStates();
        OnPropertyChanged(nameof(PendingChangesCount));
        OnPropertyChanged(nameof(PendingChangesMessage));
        OnPropertyChanged(nameof(HasPendingChanges));
        RefreshHealthStatus();
        RecomputeLoadoutDirty();
    }

    private void UpdateInstallStates()
    {
        foreach (var patch in AllPatches)
        {
            patch.IsInstalled = IsInstalled(patch.Id);
        }
    }

    private void UpdateDependencyWarning()
    {
        var patch = SelectedPatch;
        if (patch == null || !patch.IsChecked)
        {
            DependencyWarning = string.Empty;
            return;
        }

        var checkedIds = AllPatches
            .Where(p => p.IsChecked)
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = patch.Requires
            .Where(id => !checkedIds.Contains(id))
            .ToList();
        var conflicts = patch.Conflicts
            .Where(id => checkedIds.Contains(id))
            .ToList();

        var parts = new List<string>();
        if (missing.Count > 0)
            parts.Add("Missing required: " + string.Join(", ", missing));
        if (conflicts.Count > 0)
            parts.Add("Conflicts with: " + string.Join(", ", conflicts));

        DependencyWarning = string.Join("  •  ", parts);
    }

    private void RefreshCommandStates()
    {
        ((SimpleCommand)BrowseGameCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)BrowsePatchesCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)AddPatchCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)ApplyPatchesCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)UninstallAllCommand).RaiseCanExecuteChanged();
        ((SimpleCommand)LaunchGameCommand).RaiseCanExecuteChanged();
        RaiseMoveCommandsCanExecute();
        RaiseLoadoutCommandsCanExecute();
        ((SimpleCommand)SelectKotor1Command).RaiseCanExecuteChanged();
        ((SimpleCommand)SelectKotor2Command).RaiseCanExecuteChanged();
        ((SimpleCommand)RepairStagingCommand).RaiseCanExecuteChanged();
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        if (Dialogs == null)
            return;

        await Dialogs.ShowErrorAsync(title, message);
    }

    private void SetOperationInProgress(bool inProgress, string? message = null, bool isAutoRefresh = false)
    {
        IsOperationInProgress = inProgress;
        ProgressValue = inProgress ? 100 : 0;

        if (message != null && !isAutoRefresh)
        {
            StatusMessage = message;
        }

        if (!inProgress)
        {
            UpdatePendingChanges();
        }
    }

    private async Task ApplyPatches(bool skipEmptyConfirm = false)
    {
        if (string.IsNullOrWhiteSpace(GamePath) || !File.Exists(GamePath))
        {
            StatusMessage = "Error: Invalid game executable path";
            return;
        }

        if (_repository == null)
        {
            StatusMessage = "Error: No patches loaded";
            return;
        }

        try
        {
            var checkedPatches = AllPatches.Where(p => p.IsChecked && !p.IsOrphaned && p.IsCompatible).ToList();

            // If no patches are checked, uninstall all
            if (checkedPatches.Count == 0)
            {
                if (!skipEmptyConfirm && Dialogs != null)
                {
                    var confirmed = await Dialogs.ConfirmAsync(
                        "UNINSTALL ALL",
                        "No patches are selected. This will remove every installed patch from the game.");
                    if (!confirmed)
                        return;
                }

                SetOperationInProgress(true, "Uninstalling all patches...");

                var uninstallResult = await Task.Run(() =>
                    PatchRemover.RemoveAllPatches(GamePath));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (uninstallResult.Success)
                    {
                        SyncPatchSelectionWithInstalledPatches(Array.Empty<string>());
                        SetOperationInProgress(false, "All patches uninstalled successfully");
                    }
                    else
                    {
                        SetOperationInProgress(false, $"Error: {uninstallResult.Error}");
                    }
                });

                if (!uninstallResult.Success)
                    await ShowErrorAsync("UNINSTALL FAILED", uninstallResult.Error ?? "Unknown error");
                return;
            }

            // Otherwise, uninstall existing patches and install checked ones
            SetOperationInProgress(true, "Applying patches...");

            // First, uninstall any existing patches, but preserve KPM's managed
            // identity file so a statically-patched EXE can still be recognized
            // during the reinstall that follows.
            await Task.Run(() => PatchRemover.RemoveAllPatches(GamePath, removeManagedState: false));

            // Get patcher DLL path (should be in same directory as launcher)
            // AppContext.BaseDirectory works reliably with both regular and single-file builds
            var appDir = AppContext.BaseDirectory;
            var patcherDllPath = Path.Combine(appDir, "KotorPatcher.dll");
            // The native Linux engine, staged when patching the native ELF.
            var patcherSoPath = Path.Combine(appDir, "KotorPatcher.so");
            // The KProxy ships alongside the launcher; staged when deploying via proxy.
            var proxyDllPath = Path.Combine(appDir, "binkw32.dll");

            var applicator = new PatchApplicator(_repository);
            var options = new PatchApplicator.InstallOptions
            {
                GameExePath = GamePath,
                PatchIds = GetPreferredCheckedPatchIds(compatibleOnly: true),
                CreateBackup = true,
                PatcherDllPath = File.Exists(patcherDllPath) ? patcherDllPath : null,
                PatcherSoPath = File.Exists(patcherSoPath) ? patcherSoPath : null,
                ProxyDllPath = File.Exists(proxyDllPath) ? proxyDllPath : null
            };

            // Run on background thread
            var result = await Task.Run(() => applicator.InstallPatches(options));

            // Update UI on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Success)
                {
                    SetOperationInProgress(false, $"Patches applied successfully ({result.InstalledPatches.Count} patches)");
                }
                else
                {
                    SetOperationInProgress(false, $"Error: {result.Error}");
                }
            });

            if (!result.Success)
                await ShowErrorAsync("APPLY FAILED", result.Error ?? "Unknown error");

            // Refresh installed status
            await CheckPatchStatusAsync(GamePath);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetOperationInProgress(false, $"Error applying patches: {ex.Message}");
            });
            await ShowErrorAsync("APPLY FAILED", ex.Message);
        }
    }

    private async Task UninstallAll()
    {
        if (string.IsNullOrWhiteSpace(GamePath) || !File.Exists(GamePath))
        {
            StatusMessage = "Error: Invalid game executable path";
            return;
        }

        if (Dialogs != null)
        {
            var confirmed = await Dialogs.ConfirmAsync(
                "UNINSTALL ALL",
                "Remove every installed patch from the game?");
            if (!confirmed)
                return;
        }

        try
        {
            SetOperationInProgress(true, "Uninstalling all patches...");

            // Uncheck all patches
            _isBulkUpdatingPatchChecks = true;
            try
            {
                foreach (var patch in AllPatches)
                {
                    patch.IsChecked = false;
                }
            }
            finally
            {
                _isBulkUpdatingPatchChecks = false;
            }

            SaveCheckedPatches();
            UpdatePendingChanges();
            UpdateSelectAllState();

            // Now apply (which will uninstall since nothing is checked)
            await ApplyPatches(skipEmptyConfirm: true);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetOperationInProgress(false, $"Error: {ex.Message}");
            });
        }
    }

    private async Task LaunchGame()
    {
        if (string.IsNullOrWhiteSpace(GamePath) || !File.Exists(GamePath))
        {
            StatusMessage = "Error: Invalid game executable path";
            return;
        }

        if (HasPendingChanges && Dialogs != null)
        {
            var confirmed = await Dialogs.ConfirmAsync(
                "PENDING CHANGES",
                "You have unapplied patch changes. Launch anyway without Apply?");
            if (!confirmed)
                return;
        }

        try
        {
            SetOperationInProgress(true, "Launching game...");

            var launchConfig = new LaunchConfig
            {
                Method = _useCustomLaunch ? LaunchMethod.Custom : LaunchMethod.Steam,
                CustomCommand = _customLaunchCommand
            };

            // Use KPatchCore's game launcher (handles patch detection and injection automatically)
            var result = await Task.Run(() =>
            {
                var orchestrator = new PatchOrchestrator(_patchesPath ?? string.Empty);
                return orchestrator.LaunchGame(GamePath, commandLineArgs: null, launchConfig);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Success && result.ProcessId.HasValue)
                {
                    var mode = result.VanillaLaunch ? "no patches" : "with patches";
                    SetOperationInProgress(false, $"Game launched {mode} (PID: {result.ProcessId})");
                }
                else if (result.Success)
                {
                    // Process-less launch (e.g. through Steam); show its message.
                    SetOperationInProgress(false, result.Messages.FirstOrDefault() ?? "Game launched");
                }
                else
                {
                    SetOperationInProgress(false, $"Error: {result.Error}");
                }
            });

            if (!result.Success)
                await ShowErrorAsync("LAUNCH FAILED", result.Error ?? "Unknown error");
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetOperationInProgress(false, $"Error launching game: {ex.Message}");
            });
            await ShowErrorAsync("LAUNCH FAILED", ex.Message);
        }
    }

    private async Task LoadPatchesFromDirectoryAsync(string directory)
    {
        // Loads overlap whenever Refresh is clicked while one is running, or the startup
        // load races a directory change. Only the newest load may touch AllPatches;
        // otherwise each one appends its own copy of every patch.
        var requestVersion = ++_patchLoadRequestVersion;

        try
        {
            SetOperationInProgress(true, "Loading patches...");

            // Do heavy work on background thread
            var (repository, scanResult, allPatches) = await Task.Run(() =>
            {
                var repo = new PatchRepository(directory);
                var result = repo.ScanPatches();
                var patches = result.Success ? repo.GetAllPatches() : null;
                return (repo, result, patches);
            });

            // Update UI on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Back on UI thread for result handling
                if (requestVersion != _patchLoadRequestVersion)
                    return;

                if (!scanResult.Success)
                {
                    SetOperationInProgress(false, $"Error loading patches: {scanResult.Error}");
                    return;
                }

                _repository = repository;

                if (allPatches == null)
                {
                    SetOperationInProgress(false, "Error: No patches found");
                    return;
                }

                // Swap the old view models out here rather than before the scan, so a load
                // that gets superseded never empties the list it is no longer filling.
                ClearPatchViewModels();

                var patchViewModels = allPatches.Values.Select(entry =>
                {
                    var vm = new PatchItemViewModel
                    {
                        Id = entry.Manifest.Id,
                        Name = entry.Manifest.Name,
                        Version = entry.Manifest.Version,
                        Author = entry.Manifest.Author,
                        Description = entry.Manifest.Description,
                        Requires = entry.Manifest.Requires.ToList(),
                        Conflicts = entry.Manifest.Conflicts.ToList(),
                        SupportedVersions = entry.Manifest.SupportedVersions.Keys.ToList(),
                        Tags = entry.Manifest.Tags.ToList(),
                        Url = entry.Manifest.Url,
                        License = entry.Manifest.License,
                        HasAdditionalFiles = entry.HasAdditionalFiles
                    };

                    return vm;
                }).ToList();

                // Restore checked state from settings
                var checkedIds = _settings.CheckedPatchIds.ToHashSet();

                foreach (var patch in patchViewModels)
                {
                    patch.IsChecked = checkedIds.Contains(patch.Id);
                    patch.CheckedChanged += OnPatchCheckedChanged;
                    AllPatches.Add(patch);
                }

                SetPreferredInstallOrder(_settings.CheckedPatchIds);
                ReconcilePreferredInstallOrder();

                // Update compatibility status for loaded patches
                UpdatePatchCompatibility();
                UpdateSelectAllState();
                RestoreActiveLoadoutAfterLibraryLoad();
                OnPropertyChanged(nameof(HasEmptyLibrary));

                SetOperationInProgress(false, $"Loaded {patchViewModels.Count} patches from {Path.GetFileName(directory)}");
            });

            // Check patch status if we have a game path set
            if (requestVersion == _patchLoadRequestVersion
                && !string.IsNullOrWhiteSpace(GamePath)
                && File.Exists(GamePath))
            {
                await CheckPatchStatusAsync(GamePath);
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestVersion != _patchLoadRequestVersion)
                    return;

                SetOperationInProgress(false, $"Error loading patches: {ex.Message}");
            });
        }
    }

    private async Task CheckPatchStatusAsync(string gameExePath, bool isAutoRefresh = true)
    {
        if (_repository == null)
            return;

        var requestVersion = ++_patchStatusRequestVersion;

        try
        {
            SetOperationInProgress(true, "Checking patch status...", isAutoRefresh);

            // Get installation info and game version on background thread.
            var (installInfo, versionInfo) = await Task.Run(() =>
            {
                var install = PatchRemover.GetInstallationInfo(gameExePath);
                var version = GameDetector.DetectVersion(
                    gameExePath,
                    allowManagedInstallState: true);
                return (install, version);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsPatchStatusRequestCurrent(requestVersion, gameExePath))
                    return;

                ApplyDetectedGameVersion(versionInfo);
                UpdatePatchCompatibility();

                if (!installInfo.Success || installInfo.Data == null)
                {
                    SyncPatchSelectionWithInstalledPatches(Array.Empty<string>());
                    SetOperationInProgress(false, isAutoRefresh ? null : "No patches detected", isAutoRefresh);
                    return;
                }

                var info = installInfo.Data;
                SyncPatchSelectionWithInstalledPatches(info.InstalledPatches);

                var installedCount = _installedPatchIds.Count;
                SetOperationInProgress(
                    false,
                    isAutoRefresh
                        ? null
                        : installedCount == 0
                            ? "No patches currently installed"
                            : $"Found {installedCount} installed patch{(installedCount == 1 ? "" : "es")}",
                    isAutoRefresh);
            });
        }
        catch (Exception ex)
        {
            // Silent failure - not critical
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsPatchStatusRequestCurrent(requestVersion, gameExePath))
                    return;

                SyncPatchSelectionWithInstalledPatches(Array.Empty<string>());
                SetOperationInProgress(false, isAutoRefresh ? null : $"Could not check patch status: {ex.Message}", isAutoRefresh);
            });
        }
    }

    private bool IsPatchStatusRequestCurrent(int requestVersion, string gameExePath)
    {
        return requestVersion == _patchStatusRequestVersion
            && PathsEqual(GamePath, gameExePath);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ApplyDetectedGameVersion(PatchResult<GameVersion> versionInfo)
    {
        if (versionInfo.Success && versionInfo.Data != null)
        {
            var v = versionInfo.Data;
            _detectedGameVersion = v;
            KotorVersion = v.DisplayName;

            // Switch theme based on detected game title
            if (Application.Current is App app)
            {
                app.LoadTheme(v.Title);
            }

            RememberCurrentTarget();
            RefreshLoadoutList();
            OnPropertyChanged(nameof(IsKotor1Target));
            OnPropertyChanged(nameof(IsKotor2Target));
        }
        else
        {
            ApplyUnknownGameVersion();
        }
    }

    private void ApplyUnknownGameVersion()
    {
        _detectedGameVersion = null;
        KotorVersion = "Unknown";

        // Load default theme (KOTOR 1) for unknown games
        if (Application.Current is App app)
        {
            app.LoadTheme(KPatchCore.Models.GameTitle.KOTOR1);
        }

        OnPropertyChanged(nameof(IsKotor1Target));
        OnPropertyChanged(nameof(IsKotor2Target));
    }

    private void UpdatePatchCompatibility()
    {
        if (_repository == null)
            return;

        // Get all patch entries from repository
        var allPatchEntries = _repository.GetAllPatches();

        foreach (var patchViewModel in AllPatches)
        {
            // Skip orphaned patches - they can't be checked for compatibility
            if (patchViewModel.IsOrphaned)
            {
                patchViewModel.IsCompatible = false;
                patchViewModel.CompatibilityStatus = "Patch files not found";
                continue;
            }

            // If game version is unknown, show all patches as compatible
            if (_detectedGameVersion == null || _detectedGameVersion.Version == "Unknown")
            {
                patchViewModel.IsCompatible = true;
                patchViewModel.CompatibilityStatus = "Unknown game version - compatibility not verified";
                continue;
            }

            // Find the patch entry in the repository
            if (allPatchEntries.TryGetValue(patchViewModel.Id, out var patchEntry))
            {
                // Use GameVersionValidator to check compatibility
                var validationResult = GameVersionValidator.ValidateGameVersion(
                    patchEntry.Manifest,
                    _detectedGameVersion
                );

                patchViewModel.IsCompatible = validationResult.Success;
                patchViewModel.CompatibilityStatus = validationResult.Success
                    ? $"Compatible with {_detectedGameVersion.DisplayName}"
                    : $"Incompatible: {validationResult.Error}";
            }
            else
            {
                // Shouldn't happen, but handle it gracefully
                patchViewModel.IsCompatible = false;
                patchViewModel.CompatibilityStatus = "Patch not found in repository";
            }
        }

        // IsCompatible changes are not collection changes, so mirror them across by hand
        SyncVisiblePatches();
        UpdateSelectAllState();
    }
}
