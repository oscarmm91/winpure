using System.Collections.ObjectModel;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>Which startup rows the list shows.</summary>
public enum StartupFilter { All, Enabled, Disabled }

/// <summary>One row of the Startup Apps page.</summary>
public sealed class StartupItemViewModel : ObservableObject
{
    public required StartupEntry Entry { get; init; }

    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher.Length > 0 ? Entry.Publisher : Loc.T("Unknown publisher");
    public string Detail => Entry.Command;
    // Kept in English on the entry — the scope is part of its id, and ids end up in backups — and translated here.
    public string SourceLabel => Loc.T(Entry.SourceLabel);
    public string Scope => Loc.T(Entry.Scope);
    public bool IsOrphan => Entry.IsOrphan;

    private bool _visibleUnderFilter = true;
    /// <summary>Whether the current On/Off filter shows this row. The row is hidden, never removed from the
    /// list, so WPF's automation layer never queries a removed row's peer.</summary>
    public bool IsVisibleUnderFilter
    {
        get => _visibleUnderFilter;
        private set { if (_visibleUnderFilter != value) { _visibleUnderFilter = value; OnPropertyChanged(); } }
    }

    internal void SetVisible(bool value) => IsVisibleUnderFilter = value;

    public string Glyph => Entry.Source switch
    {
        StartupSource.RegistryRun => "",     // settings gear
        StartupSource.StartupFolder => "",   // folder
        _ => "",                             // calendar = scheduled
    };

    /// <summary>The scanned state. A toggle stays "dirty" until Apply commits its new value here.</summary>
    internal bool OriginalEnabled { get; private set; }

    private bool _isEnabled;
    /// <summary>The toggle. Setting it only marks the row pending; nothing is written until Apply.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsDirty));
            DirtyChanged?.Invoke();
        }
    }

    public string StateText => IsEnabled ? Loc.T("On") : Loc.T("Off");

    /// <summary>The toggle differs from what the machine has — it is waiting for Apply.</summary>
    public bool IsDirty => IsEnabled != OriginalEnabled;

    internal event Action? DirtyChanged;

    /// <summary>Set the toggle to the machine's state without marking it dirty (on load).</summary>
    internal void SetOriginal(bool value)
    {
        OriginalEnabled = value;
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>After a successful apply, the current toggle becomes the new baseline.</summary>
    internal void Commit()
    {
        OriginalEnabled = _isEnabled;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>After a failed apply, snap the toggle back to the baseline.</summary>
    internal void Revert()
    {
        _isEnabled = OriginalEnabled;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsDirty));
    }
}

public sealed class StartupViewModel : PageViewModel
{
    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => Set(ref _isEmpty, value); }

    private readonly TweakEngine _engine;

    public StartupViewModel(TweakEngine engine)
    {
        _engine = engine;
        SetFilterCommand = new RelayCommand(p => SetFilter((StartupFilter)p!));
    }

    // ---- filter (All / Enabled / Disabled) ----

    private StartupFilter _filter = StartupFilter.All;
    public RelayCommand SetFilterCommand { get; }

    // Hide non-matching rows via each row's IsVisibleUnderFilter rather than removing them from the
    // collection. Removing items made WPF's automation layer ask a removed row's peer for its name and crash
    // (NullReferenceException in ItemAutomationPeer.GetNameCore during fireAutomationEvents); hiding never
    // removes a row, so the peer always has its item.
    private void ApplyFilterVisibility()
    {
        foreach (var i in Items)
            i.SetVisible(_filter switch
            {
                StartupFilter.Enabled => i.OriginalEnabled,
                StartupFilter.Disabled => !i.OriginalEnabled,
                _ => true,
            });
    }

    private void SetFilter(StartupFilter f)
    {
        if (_filter == f) return;
        _filter = f;
        ApplyFilterVisibility();
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterEnabled));
        OnPropertyChanged(nameof(IsFilterDisabled));
    }

    public bool IsFilterAll => _filter == StartupFilter.All;
    public bool IsFilterEnabled => _filter == StartupFilter.Enabled;
    public bool IsFilterDisabled => _filter == StartupFilter.Disabled;

    // Counts (and the chip labels) reflect the machine's state, matching what each filter shows.
    public int EnabledCount => Items.Count(i => i.OriginalEnabled);
    public int DisabledCount => Items.Count(i => !i.OriginalEnabled);
    public string AllChipText => Loc.F("All ({0})", Items.Count);
    public string EnabledChipText => Loc.F("On ({0})", EnabledCount);
    public string DisabledChipText => Loc.F("Off ({0})", DisabledCount);

    private void RefreshCountsAndView()
    {
        ApplyFilterVisibility();
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(AllChipText));
        OnPropertyChanged(nameof(EnabledChipText));
        OnPropertyChanged(nameof(DisabledChipText));
    }

    /// <summary>How many rows are toggled away from what the machine has, waiting for Apply.</summary>
    public int PendingCount => Items.Count(i => i.IsDirty);

    /// <summary>Raised when the pending count changes, so the shared Apply bar re-reads it.</summary>
    public event Action? PendingChanged;

    /// <summary>Rebuilds the list from the machine. Called after each system scan.</summary>
    public void Load(ScanContext ctx)
    {
        foreach (var item in Items) item.DirtyChanged -= OnDirtyChanged;
        Items.Clear();

        foreach (var entry in StartupScanner.Scan(ctx))
        {
            var vm = new StartupItemViewModel { Entry = entry };
            vm.SetOriginal(entry.Enabled);
            vm.DirtyChanged += OnDirtyChanged;
            Items.Add(vm);
        }

        UpdateSummary();
        IsEmpty = Items.Count == 0;
        RefreshCountsAndView();
        PendingChanged?.Invoke();
    }

    private void OnDirtyChanged()
    {
        OnPropertyChanged(nameof(PendingCount));
        PendingChanged?.Invoke();
    }

    private void UpdateSummary()
    {
        int off = Items.Count(i => !i.IsEnabled);
        int orphans = Items.Count(i => i.IsOrphan);
        Summary = Loc.F("{0} apps start with Windows — {1} on, {2} off.", Items.Count, Items.Count - off, off)
                + (orphans == 0 ? ""
                    : orphans == 1 ? " " + Loc.T("1 points to a file that no longer exists.")
                    : " " + Loc.F("{0} point to a file that no longer exists.", orphans));
    }

    /// <summary>
    /// Applies every pending toggle in ONE backup session — not one per toggle, which used to leave a pile
    /// of tiny backups. Successful rows commit their new baseline; failed ones snap back. Returns how many
    /// were applied and how many failed.
    /// </summary>
    public (int applied, int failed) ApplyPending()
    {
        var dirty = Items.Where(i => i.IsDirty).ToList();
        if (dirty.Count == 0) return (0, 0);

        // Turning an entry OFF applies the tweak; turning it back ON reverts it.
        var changes = dirty.Select(i => (BuildTweak(i.Entry), apply: !i.IsEnabled)).ToList();
        var results = _engine.ApplyChanges(changes);

        int failed = 0;
        for (int k = 0; k < dirty.Count; k++)
        {
            if (results[k].Success)
            {
                dirty[k].Commit();
                LogService.Log($"Startup entry {(dirty[k].IsEnabled ? "enabled" : "disabled")}: {dirty[k].Entry.Id}");
            }
            else
            {
                dirty[k].Revert();
                failed++;
                LogService.Log($"FAILED startup change {dirty[k].Entry.Id}: {results[k].Message}");
            }
        }

        UpdateSummary();
        RefreshCountsAndView();   // committed rows may now belong to the other filter group
        OnPropertyChanged(nameof(PendingCount));
        PendingChanged?.Invoke();
        return (dirty.Count - failed, failed);
    }

    /// <summary>
    /// Starts the name a startup change is recorded under in its backup — in English, like every tweak name; the
    /// Restore page translates it where it shows it.
    /// </summary>
    internal const string TweakNamePrefix = "Startup: ";

    private static Tweak BuildTweak(StartupEntry entry)
    {
        TweakAction action = entry.TaskPath is { } taskPath
            ? new ScheduledTaskAction { TaskPath = taskPath }
            : new StartupEntryAction
            {
                ApprovedKeyPath = entry.ApprovedKeyPath
                    ?? throw new InvalidOperationException($"{entry.Id} has nothing to toggle"),
                EntryName = entry.EntryName,
            };

        return new Tweak
        {
            Id = entry.Id,
            Category = TweakCategory.Apps,
            Name = TweakNamePrefix + entry.Name,
            Description = $"Stop {entry.Name} from starting with Windows.",
            Icon = "",
            Actions = new[] { action },
        };
    }
}
