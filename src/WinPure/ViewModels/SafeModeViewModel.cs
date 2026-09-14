using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>
/// The Safe Mode page. It sets (or clears) the next restart's safe-boot flag, and is careful never to strand the
/// machine: "Restore normal boot" is always one click and works from inside Safe Mode too, and the confirmation
/// spells out how to get back before anything changes.
/// </summary>
public sealed class SafeModeViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public RelayCommand EnterMinimalCommand { get; }
    public RelayCommand EnterNetworkCommand { get; }
    public RelayCommand RestoreNormalCommand { get; }

    private SafeBoot _state = SafeBoot.Off;

    public string StateText => _state switch
    {
        SafeBoot.Minimal => Loc.T("Next restart: Safe Mode."),
        SafeBoot.Network => Loc.T("Next restart: Safe Mode with Networking."),
        _ => Loc.T("Next restart: Normal."),
    };

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool HasLoaded { get; private set; }

    public SafeModeViewModel()
    {
        EnterMinimalCommand = new RelayCommand(_ => Enter(SafeBoot.Minimal), _ => Main is { IsBusy: false });
        EnterNetworkCommand = new RelayCommand(_ => Enter(SafeBoot.Network), _ => Main is { IsBusy: false });
        RestoreNormalCommand = new RelayCommand(_ => RestoreNormal(), _ => Main is { IsBusy: false });
    }

    public void Load()
    {
        _state = SafeModeService.Read();
        OnPropertyChanged(nameof(StateText));
        HasLoaded = true;
    }

    private void Enter(SafeBoot mode)
    {
        if (!Main.ConfirmDespiteGuards(Loc.T("set the next restart to Safe Mode"), SystemGuards.ForRepair)) return;
        var answer = WinPure.Views.WinPureDialog.Show(
            Loc.T("Windows will start in Safe Mode on the NEXT restart, and stay that way until you change it back. To return to a normal Windows: open this page again (it works in Safe Mode) and click 'Restore normal boot', then restart. Continue?"),
            Loc.T("WinPure — Safe Mode"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            // Set throws unless bcdedit returned exit 0, which is the reliable, language-invariant proof the flag
            // was written. Trust that — do NOT re-read and re-parse bcdedit's (localized) text to second-guess a
            // success, or a localized "safeboot" label would make the page claim "nothing changed" after it did.
            SafeModeService.Set(mode);
            _state = mode;
            OnPropertyChanged(nameof(StateText));
            Status = Loc.T("Safe Mode is set for the next restart.");
            OfferRestart();
        }
        catch (Exception ex)
        {
            Status = Loc.F("Could not set Safe Mode: {0}", ex.Message);
        }
    }

    private void RestoreNormal()
    {
        if (!Main.ConfirmDespiteGuards(Loc.T("restore normal boot"), SystemGuards.ForRepair)) return;
        try
        {
            // After deletevalue the safeboot element is absent whether it cleared a set flag or was already gone —
            // either way the next boot is normal. (Set(Off) tolerates the "element not found" exit for that reason.)
            SafeModeService.Set(SafeBoot.Off);
            _state = SafeBoot.Off;
            OnPropertyChanged(nameof(StateText));
            Status = Loc.T("Normal boot restored. Restart to leave Safe Mode.");
            OfferRestart();
        }
        catch (Exception ex)
        {
            Status = Loc.F("Could not restore normal boot: {0}", ex.Message);
        }
    }

    private void OfferRestart()
    {
        var answer = WinPure.Views.WinPureDialog.Show(
            Loc.T("Restart now to apply the boot change?"),
            Loc.T("WinPure — Safe Mode"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        var (ok, error) = PowerService.Run(PowerAction.Restart, 0, force: false);
        if (!ok) Status = Loc.F("Could not restart: {0}", error ?? "");
    }
}
