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
    public string Publisher => Entry.Publisher.Length > 0 ? Entry.Publisher : "Unknown publisher";
    public string Detail => Entry.Command;
    public string SourceLabel => Entry.SourceLabel;
    public string Scope => Entry.Scope;
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

    public string StateText => IsEnabled ? "On" : "Off";

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
        Summary = $"{Items.Count} apps start with Windows — {Items.Count - off} on, {off} off"
                + (orphans > 0 ? $". {orphans} point to a file that no longer exists." : ".");
        IsEmpty = Items.Count == 0;
    }

    private bool _userGuardAccepted;

    private void OnToggled(StartupItemViewModel item, bool enabled)
    {
        // Every entry here lives under the signed-in user's registry or Startup folder. When
        // WinPure runs as another account, ask once per session before writing to the wrong profile.
        if (!_userGuardAccepted)
        {
            if (!Main.ConfirmDespiteGuards("change startup apps", g => g.Id == SystemGuards.DifferentUserId))
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
            Main.StatusText = $"{item.Name} will {(enabled ? "start" : "no longer start")} with Windows.";
            LogService.Log($"Startup entry {(enabled ? "enabled" : "disabled")}: {item.Entry.Id}");
            Main.RefreshBackupCount();
        }
        else
        {
            // Put the switch back where it was: it did not happen.
            item.SetEnabledSilently(!enabled);
            Main.StatusText = $"Could not change {item.Name}: {result.Message}";
            MessageBox.Show($"Could not change '{item.Name}'.\n\n{result.Message}", "WinPure",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        int off = Items.Count(i => !i.IsEnabled);
        Summary = $"{Items.Count} apps start with Windows — {Items.Count - off} on, {off} off.";
    }

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
            Name = $"Startup: {entry.Name}",
            Description = $"Stop {entry.Name} from starting with Windows.",
            Icon = "",
            Actions = new[] { action },
        };
    }
}
