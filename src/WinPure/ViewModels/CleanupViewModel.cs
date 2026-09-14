using System.Collections.ObjectModel;
using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One category of junk on the Cleanup page.</summary>
public sealed class CleanupItemViewModel : ObservableObject
{
    public required CleanupTarget Target { get; init; }

    public string Name => Loc.T(Target.Name);
    public string Description => Loc.T(Target.Description);
    public string Icon => Target.Icon;
    public bool NeedsConfirm => Target.NeedsConfirm;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) SelectionChanged?.Invoke(); }
    }

    internal event Action? SelectionChanged;

    private long _bytes = -1;   // -1 = not measured yet
    /// <summary>How much this would free. -1 until measured.</summary>
    public long Bytes
    {
        get => _bytes;
        set
        {
            if (Set(ref _bytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => _bytes == 0;
    public string SizeText => _bytes < 0 ? Loc.T("measuring…") : _bytes == 0 ? Loc.T("nothing to clean") : CleanupViewModel.FormatBytes(_bytes);
}

public sealed class CleanupViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<CleanupItemViewModel> Items { get; } = new();
    public RelayCommand CleanCommand { get; }

    public bool HasMeasured { get; private set; }

    public CleanupViewModel()
    {
        foreach (var target in CleanupCatalog.Build())
        {
            if (!CleanupService.ShouldShow(target)) continue;   // e.g. Windows.old on a PC that never updated
            var vm = new CleanupItemViewModel { Target = target, IsSelected = !target.NeedsConfirm };
            vm.SelectionChanged += () => OnPropertyChanged(nameof(SelectedSizeText));
            Items.Add(vm);
        }
        CleanCommand = new RelayCommand(_ => _ = CleanAsync(), _ => Main is { IsBusy: false } && Items.Any(i => i.IsSelected));
    }

    private long SelectedBytes => Items.Where(i => i.IsSelected && i.Bytes > 0).Sum(i => i.Bytes);

    public string SelectedSizeText => HasMeasured
        ? Loc.F("{0} selected to clean", FormatBytes(Math.Max(0, SelectedBytes)))
        : Loc.T("Measuring how much can be freed…");

    /// <summary>Measures every category's size once when the page is first opened.</summary>
    public async Task MeasureAllAsync()
    {
        HasMeasured = false;
        OnPropertyChanged(nameof(SelectedSizeText));
        foreach (var item in Items)
        {
            var target = item.Target;
            item.Bytes = await Task.Run(() => CleanupService.Measure(target));
        }
        HasMeasured = true;
        OnPropertyChanged(nameof(SelectedSizeText));
    }

    private async Task CleanAsync()
    {
        var chosen = Items.Where(i => i.IsSelected).ToList();
        if (chosen.Count == 0) return;

        // Cleaning cannot be undone — always confirm, and default to No, more firmly when the Recycle Bin
        // or any other "your own data" row is among them.
        bool destructive = chosen.Any(i => i.NeedsConfirm);
        string names = string.Join("\n  • ", chosen.Select(i => i.Name));
        string body = destructive
            ? Loc.F("This permanently deletes the following, which include your own files or the ability to roll back a Windows update. It cannot be undone:\n\n  • {0}\n\nClean now?", names)
            : Loc.F("This permanently deletes the following caches to free space. Windows recreates them as needed:\n\n  • {0}\n\nClean now?", names);
        var answer = WinPure.Views.WinPureDialog.Show(body, Loc.T("WinPure — Clean up"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        Main.IsBusy = true;
        Main.StatusText = Loc.T("Cleaning…");
        long freed = 0;
        int skipped = 0;
        try
        {
            foreach (var item in chosen)
            {
                var target = item.Target;
                var result = await Task.Run(() => CleanupService.Clean(target));
                freed += result.Bytes;
                skipped += result.Skipped;
                LogService.Log($"Cleaned {target.Id}: freed {result.Bytes} bytes, {result.Deleted} item(s), {result.Skipped} in use/skipped.");
            }
            Main.StatusText = skipped == 0
                ? Loc.F("Done — {0} freed.", FormatBytes(freed))
                : Loc.F("Done — {0} freed; some files were in use and were left.", FormatBytes(freed));
        }
        finally
        {
            Main.IsBusy = false;
        }
        await MeasureAllAsync();
        Main.RefreshBackupCount();
    }

    /// <summary>Human-readable size, e.g. "512 KB", "1.2 GB".</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
