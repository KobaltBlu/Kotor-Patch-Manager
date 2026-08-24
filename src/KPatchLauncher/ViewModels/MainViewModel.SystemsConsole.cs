using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
            }
        }
    }

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
        _loadoutData = LoadoutStore.Load();
        RefreshLoadoutList();

        SaveLoadoutCommand = new SimpleCommand(SaveCurrentAsLoadout, () => CanEditPaths);
        DeleteLoadoutCommand = new SimpleCommand(async () => await DeleteActiveLoadoutAsync(),
            () => CanEditPaths && HasActiveLoadout);
        DuplicateLoadoutCommand = new SimpleCommand(DuplicateActiveLoadout,
            () => CanEditPaths && HasActiveLoadout);
        ClearLoadoutCommand = new SimpleCommand(ClearActiveLoadout,
            () => CanEditPaths && HasActiveLoadout);
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

    public System.Windows.Input.ICommand SaveLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand DeleteLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand DuplicateLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand ClearLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor1Command { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor2Command { get; private set; } = null!;
    public System.Windows.Input.ICommand RepairStagingCommand { get; private set; } = null!;

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
        target.PatchIds = AllPatches.Where(p => p.IsChecked && !p.IsOrphaned).Select(p => p.Id).ToList();
        target.OptionValues = CaptureOptionValues();
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
            OptionValues = CloneOptionValues(_activeLoadout.OptionValues),
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

    private static Dictionary<string, Dictionary<string, int>> CloneOptionValues(
        Dictionary<string, Dictionary<string, int>> source)
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (patchId, opts) in source)
            map[patchId] = new Dictionary<string, int>(opts, StringComparer.OrdinalIgnoreCase);
        return map;
    }

    private Dictionary<string, Dictionary<string, int>> CaptureOptionValues()
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var patch in AllPatches.Where(p => p.HasOptions))
        {
            var opts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var opt in patch.Options)
                opts[opt.Id] = opt.IntValue;
            map[patch.Id] = opts;
        }
        return map;
    }

    private void RecomputeLoadoutDirty()
    {
        if (_activeLoadout == null)
        {
            IsLoadoutDirty = false;
            return;
        }

        var currentIds = AllPatches
            .Where(p => p.IsChecked && !p.IsOrphaned)
            .Select(p => p.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var savedIds = _activeLoadout.PatchIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currentIds.Count != savedIds.Count ||
            !currentIds.SequenceEqual(savedIds, StringComparer.OrdinalIgnoreCase))
        {
            IsLoadoutDirty = true;
            return;
        }

        var currentOpts = CaptureOptionValues();
        IsLoadoutDirty = !OptionMapsEqual(currentOpts, _activeLoadout.OptionValues);
    }

    private static bool OptionMapsEqual(
        Dictionary<string, Dictionary<string, int>> a,
        Dictionary<string, Dictionary<string, int>> b)
    {
        var aKeys = a.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bKeys = b.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in aKeys.Union(bKeys))
        {
            a.TryGetValue(key, out var ao);
            b.TryGetValue(key, out var bo);
            ao ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bo ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!IntMapsEqual(ao, bo))
                return false;
        }

        return true;
    }

    private static bool IntMapsEqual(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var other) || other != v)
                return false;
        }

        return true;
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
                if (loadout.OptionValues.TryGetValue(patch.Id, out var opts))
                {
                    foreach (var opt in patch.Options)
                    {
                        if (opts.TryGetValue(opt.Id, out var v))
                            opt.Value = v;
                    }
                }
            }
        }
        finally
        {
            _isBulkUpdatingPatchChecks = false;
        }

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
