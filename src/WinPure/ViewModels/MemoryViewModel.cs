using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class MemoryViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public RelayCommand CleanCommand { get; }

    private MemoryInfo _info;
    public MemoryInfo Info
    {
        get => _info;
        private set
        {
            _info = value;
            OnPropertyChanged(nameof(LoadPercent));
            OnPropertyChanged(nameof(UsageText));
        }
    }

    public int LoadPercent => _info.LoadPercent;
    public string UsageText => Loc.F("{0} of {1} in use ({2}%)",
        CleanupViewModel.FormatBytes((long)_info.UsedBytes),
        CleanupViewModel.FormatBytes((long)_info.TotalBytes),
        _info.LoadPercent);

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public MemoryViewModel()
    {
        CleanCommand = new RelayCommand(_ => _ = CleanAsync(), _ => Main is { IsBusy: false });
    }

    /// <summary>Reads current memory use when the page opens.</summary>
    public void Load()
    {
        Info = MemoryService.Query();
        Status = Loc.T("Windows reclaims memory on its own; freeing it manually usually helps only for a moment.");
    }

    private async Task CleanAsync()
    {
        Main.IsBusy = true;
        Main.StatusText = Loc.T("Freeing up memory…");
        try
        {
            var (before, after) = await Task.Run(() => MemoryService.Clean());
            Info = after;
            long freed = (long)after.AvailableBytes - (long)before.AvailableBytes;
            Status = freed > 0
                ? Loc.F("Freed {0}. Windows will use it again as it needs to.", CleanupViewModel.FormatBytes(freed))
                : Loc.T("Little was reclaimed — Windows was already managing memory efficiently.");
            Main.StatusText = Status;
        }
        finally
        {
            Main.IsBusy = false;
        }
    }
}
