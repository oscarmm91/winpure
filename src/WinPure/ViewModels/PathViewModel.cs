using System.Collections.ObjectModel;
using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One PATH entry with its verdict and a checkbox for removal.</summary>
public sealed class PathEntryViewModel : ObservableObject
{
    public required PathEntry Entry { get; init; }

    public string Value => Entry.Value.Trim().Length == 0 ? Loc.T("(empty entry)") : Entry.Value;

    public string IssueText => Entry.Issue switch
    {
        PathIssue.Missing => Loc.T("Folder not found"),
        PathIssue.Duplicate => Loc.T("Duplicate"),
        PathIssue.Empty => Loc.T("Empty entry"),
        PathIssue.Unverifiable => Loc.T("On a drive that is not connected — left alone"),
        _ => "",
    };

    public bool HasIssue => Entry.Issue != PathIssue.None;

    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>
/// The PATH page: lists the entries of the user or machine PATH, flags the ones worth removing, and rewrites the
/// PATH reversibly (the whole PATH is backed up first). A folder is only suggested for removal when it is clearly
/// dead (missing on a connected fixed drive), a duplicate, or an empty segment — never when it sits on a drive
/// that is not connected, because "looks dead" on a disconnected disk is not dead.
/// </summary>
public sealed class PathViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<PathEntryViewModel> Entries { get; } = new();
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ReloadCommand { get; }

    private readonly TweakEngine _engine;

    private bool _machineScope;
    public bool MachineScope { get => _machineScope; set { if (Set(ref _machineScope, value)) Load(); } }
    public bool UserScope { get => !_machineScope; set { if (value) MachineScope = false; } }

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    public bool HasLoaded { get; private set; }

    public PathViewModel(TweakEngine engine)
    {
        _engine = engine;
        ApplyCommand = new RelayCommand(_ => Apply(), _ => Main is { IsBusy: false } && Entries.Any(e => e.IsSelected));
        ReloadCommand = new RelayCommand(_ => Load());
    }

    private PathScope Scope => _machineScope ? PathScope.Machine : PathScope.User;

    /// <summary>Reads the selected PATH, flags every entry and pre-ticks the clearly removable ones.</summary>
    public void Load()
    {
        Entries.Clear();
        int removable = 0;
        foreach (var entry in PathService.Analyze(Scope))
        {
            bool suggest = entry.Issue is PathIssue.Missing or PathIssue.Duplicate or PathIssue.Empty;
            if (suggest) removable++;
            Entries.Add(new PathEntryViewModel { Entry = entry, IsSelected = suggest });
        }
        Summary = Entries.Count == 0
            ? Loc.T("This PATH is empty.")
            : removable == 0
                ? Loc.F("{0} entries, all in order.", Entries.Count)
                : Loc.F("{0} entries — {1} look removable (missing, duplicate or empty). Entries on drives that are not connected are left alone.", Entries.Count, removable);
        OnPropertyChanged(nameof(MachineScope));
        OnPropertyChanged(nameof(UserScope));
        HasLoaded = true;
    }

    private void Apply()
    {
        if (!Entries.Any(e => e.IsSelected)) return;
        if (!Main.ConfirmDespiteGuards(Loc.T("edit the PATH"), SystemGuards.ForRepair)) return;
        var answer = WinPure.Views.WinPureDialog.Show(
            Loc.T("Remove the ticked PATH entries? WinPure saves the whole PATH first, so Restore can put it back."),
            Loc.T("WinPure — PATH"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var kept = Entries.Where(e => !e.IsSelected).Select(e => e.Entry.Value).ToList();
        Main.IsBusy = true;
        try
        {
            int removed = _engine.ApplyPath(Scope, kept);
            Main.RefreshBackupCount();
            Main.StatusText = Loc.F("PATH updated — {0} removed. WinPure saved the previous PATH so Restore can put it back.", removed);
        }
        finally
        {
            Main.IsBusy = false;
        }
        Load();
    }
}
