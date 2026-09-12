using System.Collections.ObjectModel;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
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
        ? $"{Session.Entries.Count} changes"
        : string.Join(", ", Session.TweakNames.Take(4)) + (Session.TweakNames.Count > 4 ? $"  (+{Session.TweakNames.Count - 4} more)" : "");
    public string EntryCount => $"{Session.Entries.Count} registry/service entries";
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
