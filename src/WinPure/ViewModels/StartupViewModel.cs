using System.Collections.ObjectModel;
using System.Windows;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

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

    public string Glyph => Entry.Source switch
    {
        StartupSource.RegistryRun => "",     // settings gear
        StartupSource.StartupFolder => "",   // folder
        _ => "",                             // calendar = scheduled
    };

    private bool _isEnabled;
    /// <summary>The toggle. Setting it applies the change immediately, like Task Manager does.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            Toggled?.Invoke(this, value);
        }
    }

    public string StateText => IsEnabled ? Loc.T("On") : Loc.T("Off");

    internal event Action<StartupItemViewModel, bool>? Toggled;

    /// <summary>Puts the switch back without re-triggering the apply (used when it failed).</summary>
    internal void SetEnabledSilently(bool value)
    {
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StateText));
    }
}

public sealed class StartupViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => Set(ref _isEmpty, value); }

    private readonly TweakEngine _engine;

    public StartupViewModel(TweakEngine engine) => _engine = engine;

    /// <summary>Rebuilds the list from the machine. Called after each system scan.</summary>
    public void Load(ScanContext ctx)
    {
        foreach (var item in Items) item.Toggled -= OnToggled;
        Items.Clear();

        foreach (var entry in StartupScanner.Scan(ctx))
        {
            var vm = new StartupItemViewModel { Entry = entry };
            vm.SetEnabledSilently(entry.Enabled);
            vm.Toggled += OnToggled;
            Items.Add(vm);
        }

        int off = Items.Count(i => !i.IsEnabled);
        int orphans = Items.Count(i => i.IsOrphan);
        Summary = Loc.F("{0} apps start with Windows — {1} on, {2} off.", Items.Count, Items.Count - off, off)
                + (orphans == 0 ? ""
                    : orphans == 1 ? " " + Loc.T("1 points to a file that no longer exists.")
                    : " " + Loc.F("{0} point to a file that no longer exists.", orphans));
        IsEmpty = Items.Count == 0;
    }

    private bool _userGuardAccepted;

    private void OnToggled(StartupItemViewModel item, bool enabled)
    {
        // Every entry here lives under the signed-in user's registry or Startup folder. When
        // WinPure runs as another account, ask once per session before writing to the wrong profile.
        if (!_userGuardAccepted)
        {
            if (!Main.ConfirmDespiteGuards(Loc.T("change startup apps"), SystemGuards.ForStartup))
            {
                item.SetEnabledSilently(!enabled);
                return;
            }
            _userGuardAccepted = true;
        }

        // Turning an entry OFF is "applying" the tweak; turning it back on is reverting it.
        var tweak = BuildTweak(item.Entry);
        var results = _engine.ApplyChanges(new[] { (tweak, apply: !enabled) });
        var result = results[0];

        if (result.Success)
        {
            Main.StatusText = enabled
                ? Loc.F("{0} will start with Windows.", item.Name)
                : Loc.F("{0} will no longer start with Windows.", item.Name);
            LogService.Log($"Startup entry {(enabled ? "enabled" : "disabled")}: {item.Entry.Id}");
            Main.RefreshBackupCount();
        }
        else
        {
            // Put the switch back where it was: it did not happen.
            item.SetEnabledSilently(!enabled);
            Main.StatusText = Loc.F("Could not change {0}: {1}", item.Name, result.Message);
            // The detail can be WinPure's own exception text, which stays in English; labelled, it reads as a detail to pass on.
            MessageBox.Show(Loc.F("Could not change '{0}'.\n\nTechnical detail: {1}", item.Name, result.Message), "WinPure",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        int off = Items.Count(i => !i.IsEnabled);
        Summary = Loc.F("{0} apps start with Windows — {1} on, {2} off.", Items.Count, Items.Count - off, off);
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
