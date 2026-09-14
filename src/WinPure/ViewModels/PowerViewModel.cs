using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class PowerViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }

    // ---- Section 1: scheduled shutdown / restart (native, persists if WinPure closes) ----

    private bool _isRestart;
    /// <summary>False = shut down, true = restart.</summary>
    public bool IsRestart { get => _isRestart; set => Set(ref _isRestart, value); }
    public bool IsShutdown { get => !_isRestart; set { if (value) IsRestart = false; } }

    private string _delayMinutes = "0";
    public string DelayMinutes { get => _delayMinutes; set => Set(ref _delayMinutes, value); }

    private bool _forceCloseApps;
    public bool ForceCloseApps { get => _forceCloseApps; set => Set(ref _forceCloseApps, value); }

    public RelayCommand ScheduleCommand { get; }
    public RelayCommand CancelCommand { get; }

    // ---- Section 2: right now ----

    public RelayCommand SleepCommand { get; }
    public RelayCommand HibernateCommand { get; }
    public RelayCommand LockCommand { get; }
    public RelayCommand SignOutCommand { get; }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public PowerViewModel()
    {
        ScheduleCommand = new RelayCommand(_ => Schedule());
        CancelCommand = new RelayCommand(_ => Cancel());
        SleepCommand = new RelayCommand(_ => { if (Ask(Loc.T("Put the PC to sleep now?"))) Fire(PowerAction.Sleep); });
        HibernateCommand = new RelayCommand(_ => { if (Ask(Loc.T("Hibernate the PC now?"))) Fire(PowerAction.Hibernate); });
        LockCommand = new RelayCommand(_ => Fire(PowerAction.Lock));   // no confirm: instant, just re-login
        SignOutCommand = new RelayCommand(_ => { if (Ask(Loc.T("Sign out now? Unsaved work in open apps will be lost."))) Fire(PowerAction.SignOut); });
    }

    private bool Ask(string prompt) =>
        MessageBox.Show(prompt, Loc.T("WinPure — Power"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            == MessageBoxResult.Yes;

    private void Fire(PowerAction action)
    {
        var (ok, error) = PowerService.Run(action, 0, force: false);
        if (!ok)
            Status = string.IsNullOrEmpty(error)
                ? Loc.T("Windows would not do that — the action may be turned off on this PC.")
                : Loc.F("Could not do that: {0}", error);
    }

    private void Schedule()
    {
        // A typo'd 8-9 digit delay must NEVER wrap negative and clamp to an immediate shutdown, so the value
        // is bounded (see PowerService.TryParseDelayMinutes / MaxDelayMinutes).
        if (!PowerService.TryParseDelayMinutes(_delayMinutes, out int minutes))
        {
            Status = Loc.T("Enter the delay in whole minutes, from 0 (right now) up to 10080 (one week).");
            return;
        }
        var action = IsRestart ? PowerAction.Restart : PowerAction.Shutdown;
        string verb = IsRestart ? Loc.T("restart") : Loc.T("shut down");
        if (!Main.ConfirmDespiteGuards(IsRestart ? Loc.T("restart the PC") : Loc.T("shut down the PC"), SystemGuards.ForRepair)) return;
        string when = minutes == 0 ? Loc.T("right now") : Loc.F("in {0} minute(s)", minutes);
        var answer = MessageBox.Show(
            Loc.F("Schedule the PC to {0} {1}? {2}You can cancel it here before it happens.",
                verb, when, ForceCloseApps ? Loc.T("Open apps will be closed without saving. ") : ""),
            Loc.T("WinPure — Power"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var (ok, error) = PowerService.Run(action, minutes * 60, ForceCloseApps);
        Status = ok
            ? Loc.F("Scheduled: the PC will {0} {1}.", verb, when)
            : Loc.F("Could not schedule it: {0}", error ?? "");
    }

    private void Cancel()
    {
        var (ok, _) = PowerService.Abort();
        Status = ok
            ? Loc.T("Any scheduled shutdown or restart was cancelled.")
            : Loc.T("There was nothing scheduled to cancel.");
    }
}
