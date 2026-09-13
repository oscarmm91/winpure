using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class TweakViewModel : ObservableObject
{
    public Tweak Tweak { get; }

    public TweakViewModel(Tweak tweak) => Tweak = tweak;

    // The catalog is written in English; the card shows the user's language.
    public string Name => Loc.T(Tweak.Name);
    public string Description => Loc.T(Tweak.Description);
    public string Help => Loc.T(string.IsNullOrEmpty(Tweak.Help) ? Tweak.Description : Tweak.Help);
    public string Icon => Tweak.Icon;
    public TweakCategory Category => Tweak.Category;
    public PresetLevel Preset => Tweak.Preset;
    public bool FullyReversible => Tweak.FullyReversible;
    public bool RequiresRestart => Tweak.RequiresRestart;

    private TweakStatus _status = TweakStatus.Unknown;
    public TweakStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsOptimized));
            }
        }
    }

    public bool IsOptimized => Status == TweakStatus.Optimized;
    public string StatusText => Status switch
    {
        TweakStatus.Optimized => Loc.T("Optimized"),
        TweakStatus.Pending => Loc.T("Not applied"),
        // Before the scan it really is still scanning; afterwards, Unknown means the check
        // failed — saying "Scanning…" forever hid that from the user.
        _ => _scanned ? Loc.T("Couldn't detect") : Loc.T("Scanning…"),
    };

    private bool _isSelected;
    /// <summary>Desired state shown by the toggle. Differs from Status until Apply is clicked.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(IsDirty));
                SelectionChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// True when the toggle differs from the real system state. An undetectable tweak counts
    /// as dirty once the user switches it on: applying is idempotent and always snapshots,
    /// so "we could not check" must not mean "you cannot apply this".
    /// </summary>
    public bool IsDirty => IsSelected != IsOptimized;

    public event Action? SelectionChanged;

    private bool _scanned;

    public void RefreshStatus(TweakEngine engine, ScanContext ctx)
    {
        _scanned = true;
        Status = engine.GetStatus(Tweak, ctx);
        _isSelected = IsOptimized;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsDirty));
    }
}
