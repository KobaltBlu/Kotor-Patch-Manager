using System.Collections.Generic;

namespace KPatchLauncher.ViewModels;

public class PatchItemViewModel : ViewModelBase
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _version = string.Empty;
    private string _author = string.Empty;
    private string _description = string.Empty;
    private bool _isChecked = false;
    private bool _isOrphaned = false;
    private int _displayOrder = 0;
    private bool _isCompatible = true;
    private bool _isInstalled = false;
    private string _compatibilityStatus = string.Empty;
    private string? _url;
    private string? _license;
    private bool _hasAdditionalFiles;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (SetProperty(ref _version, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                NotifyStateChanged();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? CheckedChanged;

    public bool IsOrphaned
    {
        get => _isOrphaned;
        set
        {
            if (SetProperty(ref _isOrphaned, value))
                NotifyStateChanged();
        }
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set
        {
            if (SetProperty(ref _displayOrder, value))
            {
                OnPropertyChanged(nameof(InstallOrderText));
                OnPropertyChanged(nameof(HasInstallOrder));
            }
        }
    }

    public string InstallOrderText => _displayOrder > 0 ? $"#{_displayOrder}" : "-";

    public bool HasInstallOrder => _displayOrder > 0;

    public bool IsCompatible
    {
        get => _isCompatible;
        set
        {
            if (SetProperty(ref _isCompatible, value))
                NotifyStateChanged();
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
                NotifyStateChanged();
        }
    }

    public string CompatibilityStatus
    {
        get => _compatibilityStatus;
        set => SetProperty(ref _compatibilityStatus, value);
    }

    public string? Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
                OnPropertyChanged(nameof(HasUrl));
        }
    }

    public string? License
    {
        get => _license;
        set
        {
            if (SetProperty(ref _license, value))
                OnPropertyChanged(nameof(HasLicense));
        }
    }

    public bool HasAdditionalFiles
    {
        get => _hasAdditionalFiles;
        set
        {
            if (SetProperty(ref _hasAdditionalFiles, value))
                OnPropertyChanged(nameof(AdditionalFilesNote));
        }
    }

    public string AdditionalFilesNote =>
        HasAdditionalFiles
            ? "This patch ships additional game files. Re-apply after updating the .kpatch to refresh them."
            : string.Empty;

    public List<string> Requires { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
    public List<string> SupportedVersions { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public bool HasRequires => Requires.Count > 0;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasDependencies => HasRequires || HasConflicts;
    public bool HasSupportedVersions => SupportedVersions.Count > 0;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    public bool HasLicense => !string.IsNullOrWhiteSpace(License);
    public bool HasTags => Tags.Count > 0;
    public string TagsText => string.Join(" · ", Tags);
    public string SupportedVersionsText => string.Join(", ", SupportedVersions);

    public string DisplayText => $"{Name} v{Version}";

    public bool IsMuted => !IsCompatible && !IsOrphaned;

    public bool IsPendingAdd => IsChecked && !IsInstalled && !IsOrphaned;
    public bool IsPendingRemove => !IsChecked && IsInstalled;

    public string StateLabel
    {
        get
        {
            if (IsOrphaned)
                return "ORPHAN";
            if (!IsCompatible)
                return "INCOMPAT";
            if (IsPendingAdd)
                return "ADD";
            if (IsPendingRemove)
                return "REMOVE";
            if (IsInstalled)
                return "ON";
            return string.Empty;
        }
    }

    public bool HasStateLabel => !string.IsNullOrEmpty(StateLabel);

    public bool IsStateOn => IsInstalled && IsChecked && IsCompatible && !IsOrphaned;
    public bool IsStatePending => IsPendingAdd;
    public bool IsStateWarn => IsPendingRemove || (!IsCompatible && !IsOrphaned);
    public bool IsStateError => IsOrphaned;

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsPendingAdd));
        OnPropertyChanged(nameof(IsPendingRemove));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(HasStateLabel));
        OnPropertyChanged(nameof(IsStateOn));
        OnPropertyChanged(nameof(IsStatePending));
        OnPropertyChanged(nameof(IsStateWarn));
        OnPropertyChanged(nameof(IsStateError));
    }
}
