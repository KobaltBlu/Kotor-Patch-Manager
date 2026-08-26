using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using KPatchCore.Models;
using KPatchLauncher.Models;

namespace KPatchLauncher.ViewModels;

public partial class MainViewModel
{
    private LoadoutStore.StoreData _loadoutData = new();
    private PatchLoadout? _activeLoadout;
    private string _newLoadoutName = "New loadout";
    private string _librarySortMode = "name";
    private string _healthSummary = "Health unknown";
    private string _healthDetail = string.Empty;
    private bool _healthOk = true;
    private bool _isLoadoutDirty;
    private bool _suppressLoadoutSelection;
    private bool _loadoutSwitchInProgress;

    public ObservableCollection<PatchLoadout> AvailableLoadouts { get; } = new();

    public PatchLoadout? ActiveLoadout
    {
        get => _activeLoadout;
        set
        {
            if (_suppressLoadoutSelection)
            {
                if (SetProperty(ref _activeLoadout, value))
                    OnActiveLoadoutMetaChanged();
                return;
            }

            if (ReferenceEquals(_activeLoadout, value))
                return;

            if (_loadoutSwitchInProgress)
                return;

            if (_activeLoadout != null && IsLoadoutDirty)
            {
                _ = SwitchLoadoutWithConfirmAsync(value);
                OnPropertyChanged(nameof(ActiveLoadout));
                return;
            }

            CommitActiveLoadout(value, applyChecks: value != null);
        }
    }

    public string ActiveLoadoutName => ActiveLoadout?.Name ?? "None";

    public string ActiveLoadoutDisplayName =>
        ActiveLoadout == null
            ? "None"
            : IsLoadoutDirty
                ? $"{ActiveLoadout.Name} *"
                : ActiveLoadout.Name;

    public string NewLoadoutName
    {
        get => _newLoadoutName;
        set => SetProperty(ref _newLoadoutName, value);
    }

    public bool IsLoadoutDirty
    {
        get => _isLoadoutDirty;
        private set
        {
            if (SetProperty(ref _isLoadoutDirty, value))
            {
                OnPropertyChanged(nameof(ActiveLoadoutDisplayName));
                OnPropertyChanged(nameof(LoadoutDirtyHint));
                OnPropertyChanged(nameof(LoadoutManagerButtonLabel));
            }
        }
    }

    public string LoadoutManagerButtonLabel =>
        IsLoadoutDirty ? "LOADOUTS *" : "LOADOUTS";

    public string LoadoutDirtyHint =>
        IsLoadoutDirty ? "Loadout edited — SAVE to keep, or switch to discard" : string.Empty;

    public bool HasActiveLoadout => ActiveLoadout != null;

    public bool IsKotor1Target =>
        _detectedGameVersion?.Title == GameTitle.KOTOR1
        || (_detectedGameVersion == null && GuessTitleFromPath(GamePath) == GameTitle.KOTOR1);

    public bool IsKotor2Target =>
        _detectedGameVersion?.Title == GameTitle.KOTOR2
        || (_detectedGameVersion == null && GuessTitleFromPath(GamePath) == GameTitle.KOTOR2);

    public bool HasKotor1Memory =>
        _settings.Kotor1Target != null && File.Exists(_settings.Kotor1Target.GamePath);

    public bool HasKotor2Memory =>
        _settings.Kotor2Target != null && File.Exists(_settings.Kotor2Target.GamePath);

    public string LibrarySortMode
    {
        get => _librarySortMode;
        set
        {
            if (SetProperty(ref _librarySortMode, value))
            {
                _settings.LibrarySortMode = value;
                _settings.Save();
                SyncVisiblePatches();
            }
        }
    }

    public string HealthSummary
    {
        get => _healthSummary;
        private set => SetProperty(ref _healthSummary, value);
    }

    public string HealthDetail
    {
        get => _healthDetail;
        private set
        {
            if (SetProperty(ref _healthDetail, value))
                OnPropertyChanged(nameof(HasHealthDetail));
        }
    }

    public bool HasHealthDetail => !string.IsNullOrWhiteSpace(HealthDetail);

    public bool HealthOk
    {
        get => _healthOk;
        private set => SetProperty(ref _healthOk, value);
    }

    public bool HasEmptyLibrary =>
        !string.IsNullOrWhiteSpace(PatchesPath)
        && Directory.Exists(PatchesPath)
        && AllPatches.Count(p => !p.IsOrphaned) == 0;

    public string EmptyLibraryMessage =>
        "No .kpatch files found. Point PATCHES at the release patches folder (or build patches into this directory).";

    private void InitSystemsConsole()
    {
        _librarySortMode = string.IsNullOrWhiteSpace(_settings.LibrarySortMode)
            ? "name"
            : _settings.LibrarySortMode;
        if (string.Equals(_librarySortMode, "custom", StringComparison.OrdinalIgnoreCase))
            _librarySortMode = "name";
        _loadoutData = LoadoutStore.Load();
        RefreshLoadoutList();

        OpenLoadoutManagerCommand = new SimpleCommand(async () => await OpenLoadoutManagerAsync());
        OpenSettingsCommand = new SimpleCommand(async () => await OpenSettingsAsync());
        SaveLoadoutCommand = new SimpleCommand(SaveCurrentAsLoadout, () => CanEditPaths);
        DeleteLoadoutCommand = new SimpleCommand(async () => await DeleteActiveLoadoutAsync(),
            () => CanEditPaths && HasActiveLoadout);
        DuplicateLoadoutCommand = new SimpleCommand(DuplicateActiveLoadout,
            () => CanEditPaths && HasActiveLoadout);
        ClearLoadoutCommand = new SimpleCommand(ClearActiveLoadout,
            () => CanEditPaths && HasActiveLoadout);
        ExportLoadoutCommand = new SimpleCommand(async () => await ExportLoadoutAsync(), () => CanEditPaths);
        ImportLoadoutCommand = new SimpleCommand(async () => await ImportLoadoutAsync(), () => CanEditPaths);
        SelectKotor1Command = new SimpleCommand(() => SwitchToRememberedTarget(GameTitle.KOTOR1), () => HasKotor1Memory);
        SelectKotor2Command = new SimpleCommand(() => SwitchToRememberedTarget(GameTitle.KOTOR2), () => HasKotor2Memory);
        RepairStagingCommand = new SimpleCommand(async () => await ApplyPatches(skipEmptyConfirm: true),
            () => CanEditPaths && HasValidGamePath);

        if (!string.IsNullOrEmpty(_settings.ActiveLoadoutId))
        {
            var match = AvailableLoadouts.FirstOrDefault(l => l.Id == _settings.ActiveLoadoutId);
            if (match != null)
            {
                _suppressLoadoutSelection = true;
                try
                {
                    _activeLoadout = match;
                    NewLoadoutName = match.Name;
                }
                finally
                {
                    _suppressLoadoutSelection = false;
                }
            }
        }
    }

    public System.Windows.Input.ICommand OpenLoadoutManagerCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand OpenSettingsCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand SaveLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand DeleteLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand DuplicateLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand ClearLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand ExportLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand ImportLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor1Command { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor2Command { get; private set; } = null!;
    public System.Windows.Input.ICommand RepairStagingCommand { get; private set; } = null!;

    private async Task OpenLoadoutManagerAsync()
    {
        if (Dialogs == null)
            return;

        await Dialogs.ShowLoadoutManagerAsync(this);
    }

    private async Task OpenSettingsAsync()
    {
        if (Dialogs == null)
            return;

        await Dialogs.ShowSettingsAsync(this);
    }

    private void OnActiveLoadoutMetaChanged()
    {
        _settings.ActiveLoadoutId = _activeLoadout?.Id;
        _settings.Save();
        OnPropertyChanged(nameof(ActiveLoadoutName));
        OnPropertyChanged(nameof(ActiveLoadoutDisplayName));
        OnPropertyChanged(nameof(HasActiveLoadout));
        RaiseLoadoutCommandsCanExecute();
        RecomputeLoadoutDirty();
    }

    private void CommitActiveLoadout(PatchLoadout? value, bool applyChecks)
    {
        if (!SetProperty(ref _activeLoadout, value))
        {
            OnActiveLoadoutMetaChanged();
            return;
        }

        _settings.ActiveLoadoutId = value?.Id;
        _settings.Save();
        if (value != null)
            NewLoadoutName = value.Name;

        OnPropertyChanged(nameof(ActiveLoadoutName));
        OnPropertyChanged(nameof(ActiveLoadoutDisplayName));
        OnPropertyChanged(nameof(HasActiveLoadout));
        RaiseLoadoutCommandsCanExecute();

        if (applyChecks && value != null)
            ApplyLoadout(value, markClean: true);
        else
            RecomputeLoadoutDirty();

        RememberCurrentTarget();
    }

    private async Task SwitchLoadoutWithConfirmAsync(PatchLoadout? requested)
    {
        if (_loadoutSwitchInProgress)
            return;

        _loadoutSwitchInProgress = true;
        try
        {
            var confirmed = Dialogs == null || await Dialogs.ConfirmAsync(
                "DISCARD LOADOUT EDITS?",
                "Current checklist differs from the selected loadout. Switch and discard edits?",
                "SWITCH",
                "CANCEL");

            if (!confirmed)
            {
                OnPropertyChanged(nameof(ActiveLoadout));
                return;
            }

            CommitActiveLoadout(requested, applyChecks: requested != null);
        }
        finally
        {
            _loadoutSwitchInProgress = false;
        }
    }

    private void RaiseLoadoutCommandsCanExecute()
    {
        if (SaveLoadoutCommand is SimpleCommand save)
            save.RaiseCanExecuteChanged();
        if (DeleteLoadoutCommand is SimpleCommand del)
            del.RaiseCanExecuteChanged();
        if (DuplicateLoadoutCommand is SimpleCommand dup)
            dup.RaiseCanExecuteChanged();
        if (ClearLoadoutCommand is SimpleCommand clr)
            clr.RaiseCanExecuteChanged();
        if (ExportLoadoutCommand is SimpleCommand exp)
            exp.RaiseCanExecuteChanged();
        if (ImportLoadoutCommand is SimpleCommand imp)
            imp.RaiseCanExecuteChanged();
    }

    private void RefreshLoadoutList()
    {
        var previousId = _activeLoadout?.Id ?? _settings.ActiveLoadoutId;
        AvailableLoadouts.Clear();
        var key = CurrentGameKey();
        foreach (var loadout in _loadoutData.Loadouts
                     .Where(l => l.GameKey == key)
                     .OrderBy(l => l.Name))
        {
            AvailableLoadouts.Add(loadout);
        }

        if (previousId == null)
            return;

        var stillVisible = AvailableLoadouts.FirstOrDefault(l => l.Id == previousId);
        if (stillVisible == null && _activeLoadout != null)
        {
            _suppressLoadoutSelection = true;
            try
            {
                _activeLoadout = null;
                OnActiveLoadoutMetaChanged();
            }
            finally
            {
                _suppressLoadoutSelection = false;
            }
        }
    }

    private string CurrentGameKey()
    {
        var title = _detectedGameVersion?.Title ?? GuessTitleFromPath(GamePath);
        return title switch
        {
            GameTitle.KOTOR1 => "kotor1",
            GameTitle.KOTOR2 => "kotor2",
            _ => "unknown"
        };
    }

    private static GameTitle GuessTitleFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GameTitle.Unknown;
        var name = Path.GetFileName(path);
        if (name.Contains("kotor2", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("KOTOR2", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("swkotor2", StringComparison.OrdinalIgnoreCase))
            return GameTitle.KOTOR2;
        if (name.Contains("kotor", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("swkotor", StringComparison.OrdinalIgnoreCase))
            return GameTitle.KOTOR1;
        return GameTitle.Unknown;
    }

    private void SaveCurrentAsLoadout()
    {
        var gameKey = CurrentGameKey();
        var typedName = NewLoadoutName?.Trim() ?? string.Empty;

        PatchLoadout target;
        string statusVerb;

        if (_activeLoadout != null &&
            (string.IsNullOrWhiteSpace(typedName) ||
             string.Equals(typedName, _activeLoadout.Name, StringComparison.OrdinalIgnoreCase)))
        {
            target = _activeLoadout;
            if (!string.IsNullOrWhiteSpace(typedName))
                target.Name = typedName;
            statusVerb = "Updated";
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(typedName) ? "Loadout" : typedName;
            var existing = _loadoutData.Loadouts.FirstOrDefault(l =>
                string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)
                && l.GameKey == gameKey);

            if (existing != null)
            {
                target = existing;
                statusVerb = "Updated";
            }
            else if (_activeLoadout != null &&
                     !string.Equals(typedName, _activeLoadout.Name, StringComparison.OrdinalIgnoreCase))
            {
                target = _activeLoadout;
                target.Name = name;
                statusVerb = "Renamed and saved";
            }
            else
            {
                target = new PatchLoadout
                {
                    Name = name,
                    GameKey = gameKey
                };
                _loadoutData.Loadouts.Add(target);
                statusVerb = "Saved";
            }
        }

        target.GameKey = gameKey;
        target.PatchIds = GetPreferredCheckedPatchIds(compatibleOnly: false);
        target.UpdatedAt = DateTimeOffset.UtcNow;

        if (!LoadoutStore.TrySave(_loadoutData, out var error))
        {
            StatusMessage = $"Failed to save loadout: {error}";
            return;
        }

        RefreshLoadoutList();
        _suppressLoadoutSelection = true;
        try
        {
            _activeLoadout = AvailableLoadouts.FirstOrDefault(l => l.Id == target.Id) ?? target;
            NewLoadoutName = _activeLoadout.Name;
            OnPropertyChanged(nameof(ActiveLoadout));
            OnActiveLoadoutMetaChanged();
        }
        finally
        {
            _suppressLoadoutSelection = false;
        }

        IsLoadoutDirty = false;
        RememberCurrentTarget();
        StatusMessage = $"{statusVerb} loadout '{target.Name}'";
    }

    private async Task DeleteActiveLoadoutAsync()
    {
        if (_activeLoadout == null)
            return;

        var name = _activeLoadout.Name;
        var id = _activeLoadout.Id;
        var confirmed = Dialogs == null || await Dialogs.ConfirmAsync(
            "DELETE LOADOUT?",
            $"Remove loadout '{name}'? Checklist is unchanged.",
            "DELETE",
            "CANCEL");

        if (!confirmed)
            return;

        _loadoutData.Loadouts.RemoveAll(l => l.Id == id);
        if (!LoadoutStore.TrySave(_loadoutData, out var error))
        {
            StatusMessage = $"Failed to delete loadout: {error}";
            return;
        }

        RefreshLoadoutList();
        CommitActiveLoadout(null, applyChecks: false);
        StatusMessage = $"Deleted loadout '{name}'";
    }

    private void DuplicateActiveLoadout()
    {
        if (_activeLoadout == null)
            return;

        var baseName = _activeLoadout.Name;
        var copyName = UniqueCopyName(baseName, _activeLoadout.GameKey);
        var clone = new PatchLoadout
        {
            Name = copyName,
            GameKey = _activeLoadout.GameKey,
            PatchIds = _activeLoadout.PatchIds.ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _loadoutData.Loadouts.Add(clone);
        if (!LoadoutStore.TrySave(_loadoutData, out var error))
        {
            StatusMessage = $"Failed to duplicate loadout: {error}";
            return;
        }

        RefreshLoadoutList();
        CommitActiveLoadout(AvailableLoadouts.FirstOrDefault(l => l.Id == clone.Id) ?? clone, applyChecks: true);
        StatusMessage = $"Duplicated loadout as '{copyName}'";
    }

    private void ClearActiveLoadout()
    {
        if (_activeLoadout == null)
            return;

        CommitActiveLoadout(null, applyChecks: false);
        StatusMessage = "Loadout selection cleared";
    }

    private async Task ExportLoadoutAsync()
    {
        try
        {
            var window = GetMainWindow();
            if (window == null)
            {
                StatusMessage = "Error: Could not access window";
                return;
            }

            var name = !string.IsNullOrWhiteSpace(ActiveLoadout?.Name)
                ? ActiveLoadout!.Name
                : !string.IsNullOrWhiteSpace(NewLoadoutName)
                    ? NewLoadoutName.Trim()
                    : "Exported";

            var portable = new PortableLoadoutFile
            {
                SchemaVersion = PortableLoadoutFile.CurrentSchemaVersion,
                Name = name,
                GameKey = CurrentGameKey(),
                PatchIds = GetPreferredCheckedPatchIds(compatibleOnly: false)
            };

            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "loadout";

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export loadout",
                SuggestedFileName = $"{safeName}.kploadout",
                DefaultExtension = "kploadout",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("KotOR Patch Loadout")
                    {
                        Patterns = new[] { "*.kploadout" }
                    },
                    new FilePickerFileType("JSON")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

            if (file == null)
                return;

            var path = file.Path.LocalPath;
            await File.WriteAllTextAsync(path, PortableLoadoutFile.Serialize(portable));
            StatusMessage = $"Exported loadout '{name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to export loadout: {ex.Message}";
        }
    }

    private async Task ImportLoadoutAsync()
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
                Title = "Import loadout",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KotOR Patch Loadout")
                    {
                        Patterns = new[] { "*.kploadout", "*.json" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (result.Count == 0)
                return;

            var path = result[0].Path.LocalPath;
            var json = await File.ReadAllTextAsync(path);
            var portable = PortableLoadoutFile.TryDeserialize(json, out var error);
            if (portable == null)
            {
                StatusMessage = $"Failed to import loadout: {error}";
                return;
            }

            // Always attach to the current target so the loadout appears in the combo.
            var gameKey = CurrentGameKey();

            var name = portable.Name;
            if (_loadoutData.Loadouts.Any(l =>
                    l.GameKey == gameKey &&
                    string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = UniqueCopyName(name, gameKey);
            }

            var loadout = portable.ToLoadout();
            loadout.Name = name;
            loadout.GameKey = gameKey;

            _loadoutData.Loadouts.Add(loadout);
            if (!LoadoutStore.TrySave(_loadoutData, out var saveError))
            {
                StatusMessage = $"Failed to import loadout: {saveError}";
                return;
            }

            RefreshLoadoutList();
            var imported = AvailableLoadouts.FirstOrDefault(l => l.Id == loadout.Id) ?? loadout;
            CommitActiveLoadout(imported, applyChecks: true);

            var missing = loadout.PatchIds
                .Where(id => !AllPatches.Any(p => !p.IsOrphaned &&
                    string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            StatusMessage = missing.Count == 0
                ? $"Imported loadout '{name}' — Apply to stage"
                : $"Imported loadout '{name}' — {missing.Count} patch(es) missing from library";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to import loadout: {ex.Message}";
        }
    }

    private string UniqueCopyName(string baseName, string gameKey)
    {
        var candidate = $"{baseName} (copy)";
        var n = 2;
        while (_loadoutData.Loadouts.Any(l =>
                   l.GameKey == gameKey &&
                   string.Equals(l.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} (copy {n})";
            n++;
        }

        return candidate;
    }

    private void RecomputeLoadoutDirty()
    {
        if (_activeLoadout == null)
        {
            IsLoadoutDirty = false;
            return;
        }

        var currentIds = GetPreferredCheckedPatchIds(compatibleOnly: false);
        var savedIds = _activeLoadout.PatchIds;

        IsLoadoutDirty = currentIds.Count != savedIds.Count ||
            !currentIds.SequenceEqual(savedIds, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyLoadout(PatchLoadout loadout, bool markClean = true)
    {
        _isBulkUpdatingPatchChecks = true;
        try
        {
            var ids = loadout.PatchIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var patch in AllPatches)
            {
                if (patch.IsOrphaned)
                    continue;
                patch.IsChecked = ids.Contains(patch.Id);
            }
        }
        finally
        {
            _isBulkUpdatingPatchChecks = false;
        }

        SetPreferredInstallOrder(loadout.PatchIds);
        ReconcilePreferredInstallOrder();

        SaveCheckedPatches();
        UpdatePendingChanges();
        UpdateSelectAllState();
        UpdateDependencyWarning();
        if (markClean)
            IsLoadoutDirty = false;
        else
            RecomputeLoadoutDirty();
        StatusMessage = $"Loadout '{loadout.Name}' selected — Apply to stage";
    }

    private void RestoreActiveLoadoutAfterLibraryLoad()
    {
        RefreshLoadoutList();

        var id = _settings.ActiveLoadoutId;
        if (string.IsNullOrEmpty(id))
        {
            RecomputeLoadoutDirty();
            return;
        }

        var match = AvailableLoadouts.FirstOrDefault(l => l.Id == id);
        if (match == null)
        {
            RecomputeLoadoutDirty();
            return;
        }

        _suppressLoadoutSelection = true;
        try
        {
            _activeLoadout = match;
            NewLoadoutName = match.Name;
            OnPropertyChanged(nameof(ActiveLoadout));
            OnActiveLoadoutMetaChanged();
        }
        finally
        {
            _suppressLoadoutSelection = false;
        }

        ApplyLoadout(match, markClean: true);
    }

    private void RememberCurrentTarget()
    {
        var title = _detectedGameVersion?.Title ?? GuessTitleFromPath(GamePath);
        if (title is not (GameTitle.KOTOR1 or GameTitle.KOTOR2))
            return;
        if (string.IsNullOrWhiteSpace(GamePath) || !File.Exists(GamePath))
            return;

        var memory = new GameTargetMemory
        {
            GamePath = GamePath,
            PatchesPath = PatchesPath,
            ActiveLoadoutId = ActiveLoadout?.Id
        };

        if (title == GameTitle.KOTOR1)
            _settings.Kotor1Target = memory;
        else
            _settings.Kotor2Target = memory;

        _settings.Save();
        OnPropertyChanged(nameof(HasKotor1Memory));
        OnPropertyChanged(nameof(HasKotor2Memory));
        if (SelectKotor1Command is SimpleCommand k1)
            k1.RaiseCanExecuteChanged();
        if (SelectKotor2Command is SimpleCommand k2)
            k2.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsKotor1Target));
        OnPropertyChanged(nameof(IsKotor2Target));
    }

    private void SwitchToRememberedTarget(GameTitle title)
    {
        var memory = title == GameTitle.KOTOR1 ? _settings.Kotor1Target : _settings.Kotor2Target;
        if (memory == null || !File.Exists(memory.GamePath))
            return;

        if (!string.IsNullOrEmpty(memory.ActiveLoadoutId))
            _settings.ActiveLoadoutId = memory.ActiveLoadoutId;

        if (!string.IsNullOrWhiteSpace(memory.PatchesPath))
            PatchesPath = memory.PatchesPath;
        GamePath = memory.GamePath;

        StatusMessage = title == GameTitle.KOTOR1 ? "Switched to KotOR 1 target" : "Switched to KotOR 2 target";
    }

    private void RefreshHealthStatus()
    {
        if (!HasValidGamePath)
        {
            HealthSummary = "No game targeted";
            HealthDetail = string.Empty;
            HealthOk = true;
            return;
        }

        var gameDir = Path.GetDirectoryName(GamePath)!;
        var issues = new List<string>();
        var installed = AllPatches.Count(p => p.IsInstalled);
        var orphans = AllPatches.Count(p => p.IsOrphaned);
        var pending = PendingChangesCount;

        if (pending > 0)
            issues.Add($"{pending} pending change(s) — Apply to sync");
        if (orphans > 0)
            issues.Add($"{orphans} orphaned patch(es)");

        var needsPatcher = installed > 0 || AllPatches.Any(p => p.IsChecked);
        var hasDll = File.Exists(Path.Combine(gameDir, "KotorPatcher.dll"));
        var hasSo = File.Exists(Path.Combine(gameDir, "KotorPatcher.so"));
        var hasConfig = File.Exists(Path.Combine(gameDir, "patch_config.toml"));

        if (needsPatcher && !hasDll && !hasSo && installed > 0)
            issues.Add("KotorPatcher runtime missing from game folder");
        if (installed > 0 && !hasConfig)
            issues.Add("patch_config.toml missing");

        if (issues.Count == 0)
        {
            HealthOk = true;
            HealthSummary = installed == 0
                ? "Clean — no patches staged"
                : $"Healthy — {installed} patch(es) staged";
            HealthDetail = hasConfig ? "Config present" : string.Empty;
            if (hasDll) HealthDetail = string.IsNullOrEmpty(HealthDetail) ? "KotorPatcher.dll present" : HealthDetail + " · KotorPatcher.dll present";
        }
        else
        {
            HealthOk = false;
            HealthSummary = "Attention needed";
            HealthDetail = string.Join(" · ", issues);
        }

        OnPropertyChanged(nameof(HasEmptyLibrary));
        if (RepairStagingCommand is SimpleCommand repair)
            repair.RaiseCanExecuteChanged();
    }

    private IEnumerable<PatchItemViewModel> ApplyLibrarySort(IEnumerable<PatchItemViewModel> source)
    {
        return LibrarySortMode switch
        {
            "author" => source.OrderBy(p => p.Author).ThenBy(p => p.Name),
            "installed" => source.OrderByDescending(p => p.IsInstalled).ThenBy(p => p.Name),
            "pending" => source.OrderByDescending(p => p.IsPendingAdd || p.IsPendingRemove).ThenBy(p => p.Name),
            _ => source.OrderBy(p => p.Name)
        };
    }
}
