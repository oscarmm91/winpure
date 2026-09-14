using System.Collections.ObjectModel;
using System.IO;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private readonly string _title = "", _subtitle = "";
    // Translated here, once, so no page can forget to. What is passed in is the English key.
    public required string Title { get => _title; init => _title = Loc.T(value); }
    public required string Subtitle { get => _subtitle; init => _subtitle = Loc.T(value); }
}

/// <summary>A page of tweak cards: one category, or the search results (no category).</summary>
public sealed class CategoryPageViewModel : PageViewModel
{
    public TweakCategory? Category { get; init; }
    public required MainViewModel Main { get; init; }
    public ObservableCollection<TweakViewModel> Tweaks { get; } = new();

    /// <summary>False on Remove Apps: a preset never ticks what cannot be undone, so its buttons have no place there.</summary>
    public bool ShowsPresets { get; init; } = true;

    private readonly string _warning = "";
    /// <summary>A banner shown above the tweaks, translated when set like the title. Empty = no banner.</summary>
    public string Warning { get => _warning; init => _warning = Loc.T(value); }
    public bool HasWarning => Warning.Length > 0;
}

public sealed class DashboardViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }

    private int _total, _optimized, _pending;
    public int TotalCount { get => _total; set => Set(ref _total, value); }
    public int OptimizedCount { get => _optimized; set => Set(ref _optimized, value); }
    public int PendingCount { get => _pending; set => Set(ref _pending, value); }

    private int _backupCount;
    public int BackupCount { get => _backupCount; set => Set(ref _backupCount, value); }

    public string OsInfo { get; init; } = "";

    // Clicking a count card opens a filtered list. Set by MainViewModel, which owns the tweaks and pages.
    public RelayCommand? ShowAllCommand { get; set; }
    public RelayCommand? ShowOptimizedCommand { get; set; }
    public RelayCommand? ShowPendingCommand { get; set; }
    public RelayCommand? ShowBackupsCommand { get; set; }

    // ---- Live system stats: refreshed by a timer in MainViewModel while the dashboard is open ----
    private MemoryInfo _ram;
    public int RamPercent => _ram.LoadPercent;
    public string RamText => Loc.F("{0} of {1} in use ({2}%)",
        CleanupViewModel.FormatBytes((long)_ram.UsedBytes),
        CleanupViewModel.FormatBytes((long)_ram.TotalBytes),
        _ram.LoadPercent);

    private long _diskFree, _diskTotal;
    public int DiskPercent => _diskTotal == 0 ? 0 : (int)Math.Round(100.0 * (_diskTotal - _diskFree) / _diskTotal);
    public string DiskText => Loc.F("{0} free of {1}",
        CleanupViewModel.FormatBytes(_diskFree), CleanupViewModel.FormatBytes(_diskTotal));

    /// <summary>Re-reads RAM and system-drive free space. Cheap, read-only, safe to call on a timer.</summary>
    public void RefreshLive()
    {
        _ram = MemoryService.Query();
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(root))
            {
                var d = new DriveInfo(root);
                if (d.IsReady) { _diskFree = d.AvailableFreeSpace; _diskTotal = d.TotalSize; }
            }
        }
        catch { }
        OnPropertyChanged(nameof(RamPercent));
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(DiskPercent));
        OnPropertyChanged(nameof(DiskText));
    }
}

public sealed class BackupSessionViewModel : ObservableObject
{
    public required BackupSession Session { get; init; }
    public string Title => Session.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy — HH:mm");
    public string Summary => Session.TweakNames.Count == 0
        ? (Session.Entries.Count == 1 ? Loc.T("1 change") : Loc.F("{0} changes", Session.Entries.Count))
        // App removals first, so one never hides in "(+N more)": the order otherwise follows the catalog, where Privacy
        // comes before the removals.
        : string.Join(", ", Session.TweakNames.OrderBy(n => IrreversibleNames.Value.Contains(n) ? 0 : 1).Take(4).Select(DisplayName))
          + (Session.TweakNames.Count > 4 ? "  " + Loc.F("(+{0} more)", Session.TweakNames.Count - 4) : "");
    public string EntryCount => Session.Entries.Count == 1
        ? Loc.T("1 registry/service entry")
        : Loc.F("{0} registry/service entries", Session.Entries.Count);

    // The full list of what this backup changed, for the expandable detail — the Summary shows only the first
    // four. Tweak names read plainly to a person; a DNS/PATH session carries no names, so those fall back to a
    // short per-entry description. Every removal is marked, since restoring the session does not reinstall it.
    public IReadOnlyList<string> Details =>
        Session.TweakNames.Count > 0
            ? Session.TweakNames.OrderBy(n => IrreversibleNames.Value.Contains(n) ? 0 : 1).Select(DisplayName).ToList()
            // A DNS session captures one entry per adapter; collapse the repeats so it reads "DNS servers" once.
            : Session.Entries.Select(DescribeEntry).Distinct().ToList();

    public bool HasDetails => Details.Count > 0;

    private static string DescribeEntry(BackupEntry e) => e.Type switch
    {
        "dns" => Loc.T("DNS servers"),
        "path" => Loc.T("PATH"),
        "service" => Loc.F("Service: {0}", e.ServiceName ?? ""),
        "scheduled-task" => Loc.F("Task: {0}", e.TaskPath ?? ""),
        _ => e.ValueName ?? e.KeyPath ?? e.Type,
    };

    /// <summary>
    /// A backup records tweak names in English, so it reads the same whichever language made it; they are
    /// translated here, where they are shown. A startup entry's name is the app's own and stays as it is.
    /// </summary>
    private static string DisplayName(string name) =>
        name.StartsWith(StartupViewModel.TweakNamePrefix, StringComparison.Ordinal)
            ? Loc.F("Startup: {0}", name[StartupViewModel.TweakNamePrefix.Length..])
            // An app removal is recorded in its session like any change. Restoring the session puts back the registry values
            // some removals also change, but it does not reinstall the app.
            : IrreversibleNames.Value.Contains(name) ? Loc.F("{0} (app not reinstalled)", Loc.T(name))
            : Loc.T(name);

    private static readonly Lazy<HashSet<string>> IrreversibleNames = new(() =>
        TweakCatalog.Build().Where(t => !t.FullyReversible).Select(t => t.Name).ToHashSet(StringComparer.Ordinal));
}

public sealed class RestoreViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<BackupSessionViewModel> Sessions { get; } = new();

    private bool _isEmpty = true;
    public bool IsEmpty { get => _isEmpty; set => Set(ref _isEmpty, value); }

    public RelayCommand RestoreCommand { get; set; } = null!;
    public RelayCommand DeleteCommand { get; set; } = null!;
}
