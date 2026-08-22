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

    public ObservableCollection<PatchLoadout> AvailableLoadouts { get; } = new();

    public PatchLoadout? ActiveLoadout
    {
        get => _activeLoadout;
        set
        {
            if (SetProperty(ref _activeLoadout, value))
            {
                _settings.ActiveLoadoutId = value?.Id;
                _settings.Save();
                if (value != null)
                    ApplyLoadout(value);
                OnPropertyChanged(nameof(ActiveLoadoutName));
            }
        }
    }

    public string ActiveLoadoutName => ActiveLoadout?.Name ?? "None";

    public string NewLoadoutName
    {
        get => _newLoadoutName;
        set => SetProperty(ref _newLoadoutName, value);
    }

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
        SelectKotor1Command = new SimpleCommand(() => SwitchToRememberedTarget(GameTitle.KOTOR1), () => HasKotor1Memory);
        SelectKotor2Command = new SimpleCommand(() => SwitchToRememberedTarget(GameTitle.KOTOR2), () => HasKotor2Memory);
        RepairStagingCommand = new SimpleCommand(async () => await ApplyPatches(skipEmptyConfirm: true),
            () => CanEditPaths && HasValidGamePath);

        if (!string.IsNullOrEmpty(_settings.ActiveLoadoutId))
        {
            var match = AvailableLoadouts.FirstOrDefault(l => l.Id == _settings.ActiveLoadoutId);
            if (match != null)
                _activeLoadout = match;
        }
    }

    public System.Windows.Input.ICommand SaveLoadoutCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor1Command { get; private set; } = null!;
    public System.Windows.Input.ICommand SelectKotor2Command { get; private set; } = null!;
    public System.Windows.Input.ICommand RepairStagingCommand { get; private set; } = null!;

    private void RefreshLoadoutList()
    {
        AvailableLoadouts.Clear();
        var key = CurrentGameKey();
        foreach (var loadout in _loadoutData.Loadouts
                     .Where(l => l.GameKey == key || l.GameKey == "unknown" || key == "unknown")
                     .OrderBy(l => l.Name))
        {
            AvailableLoadouts.Add(loadout);
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
        var name = string.IsNullOrWhiteSpace(NewLoadoutName) ? "Loadout" : NewLoadoutName.Trim();
        var loadout = new PatchLoadout
        {
            Name = name,
            GameKey = CurrentGameKey(),
            PatchIds = AllPatches.Where(p => p.IsChecked && !p.IsOrphaned).Select(p => p.Id).ToList(),
            OptionValues = CaptureOptionValues()
        };

        _loadoutData.Loadouts.RemoveAll(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)
            && l.GameKey == loadout.GameKey);
        _loadoutData.Loadouts.Add(loadout);
        LoadoutStore.Save(_loadoutData);
        RefreshLoadoutList();
        _activeLoadout = AvailableLoadouts.FirstOrDefault(l => l.Id == loadout.Id);
        OnPropertyChanged(nameof(ActiveLoadout));
        OnPropertyChanged(nameof(ActiveLoadoutName));
        _settings.ActiveLoadoutId = _activeLoadout?.Id;
        _settings.Save();
        StatusMessage = $"Saved loadout '{name}'";
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

    private void ApplyLoadout(PatchLoadout loadout)
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
        StatusMessage = $"Loadout '{loadout.Name}' selected — Apply to stage";
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
        ((SimpleCommand)SelectKotor1Command).RaiseCanExecuteChanged();
        ((SimpleCommand)SelectKotor2Command).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsKotor1Target));
        OnPropertyChanged(nameof(IsKotor2Target));
    }

    private void SwitchToRememberedTarget(GameTitle title)
    {
        var memory = title == GameTitle.KOTOR1 ? _settings.Kotor1Target : _settings.Kotor2Target;
        if (memory == null || !File.Exists(memory.GamePath))
            return;

        if (!string.IsNullOrWhiteSpace(memory.PatchesPath))
            PatchesPath = memory.PatchesPath;
        GamePath = memory.GamePath;

        if (!string.IsNullOrEmpty(memory.ActiveLoadoutId))
        {
            RefreshLoadoutList();
            var match = AvailableLoadouts.FirstOrDefault(l => l.Id == memory.ActiveLoadoutId);
            if (match != null)
                ActiveLoadout = match;
        }

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
        ((SimpleCommand)RepairStagingCommand).RaiseCanExecuteChanged();
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
