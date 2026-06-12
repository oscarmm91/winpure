using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class TweakViewModel : ObservableObject
{
    public Tweak Tweak { get; }

    public TweakViewModel(Tweak tweak) => Tweak = tweak;

    public string Name => Tweak.Name;
    public string Description => Tweak.Description;
    public string Help => string.IsNullOrEmpty(Tweak.Help) ? Tweak.Description : Tweak.Help;
    public string Icon => Tweak.Icon;
    public TweakCategory Category => Tweak.Category;
    public PresetLevel Preset => Tweak.Preset;
    public bool FullyReversible => Tweak.FullyReversible;

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
        TweakStatus.Optimized => "Optimized",
        TweakStatus.Pending => "Not applied",
        _ => "Scanning…",
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

    /// <summary>True when the toggle differs from the real system state.</summary>
    public bool IsDirty => Status != TweakStatus.Unknown && IsSelected != IsOptimized;

    public event Action? SelectionChanged;

    public void RefreshStatus(TweakEngine engine, ScanContext ctx)
    {
        Status = engine.GetStatus(Tweak, ctx);
        _isSelected = IsOptimized;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsDirty));
    }
}
