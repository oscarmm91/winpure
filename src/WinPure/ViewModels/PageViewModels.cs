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
}

public sealed class BackupSessionViewModel : ObservableObject
{
    public required BackupSession Session { get; init; }
    public string Title => Session.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy — HH:mm");
    public string Summary => Session.TweakNames.Count == 0
        ? (Session.Entries.Count == 1 ? Loc.T("1 change") : Loc.F("{0} changes", Session.Entries.Count))
        : string.Join(", ", Session.TweakNames.Take(4).Select(DisplayName))
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
            : Loc.T(name);
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
