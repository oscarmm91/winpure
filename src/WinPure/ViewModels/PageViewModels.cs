using System.Collections.ObjectModel;
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
