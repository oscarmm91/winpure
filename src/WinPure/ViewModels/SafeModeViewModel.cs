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
        var answer = MessageBox.Show(
            Loc.T("Windows will start in Safe Mode on the NEXT restart, and stay that way until you change it back. To return to a normal Windows: open this page again (it works in Safe Mode) and click 'Restore normal boot', then restart. Continue?"),
            Loc.T("WinPure — Safe Mode"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            SafeModeService.Set(mode);
            _state = SafeModeService.Read();          // judge by the real state, not by the fact we ran
            OnPropertyChanged(nameof(StateText));
            if (_state == SafeBoot.Off)
            {
                Status = Loc.T("The safe-boot flag was not set — nothing changed.");
                return;
            }
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
            SafeModeService.Set(SafeBoot.Off);
            _state = SafeModeService.Read();
            OnPropertyChanged(nameof(StateText));
            Status = _state == SafeBoot.Off
                ? Loc.T("Normal boot restored. Restart to leave Safe Mode.")
                : Loc.T("The safe-boot flag is still set — see the log.");
            if (_state == SafeBoot.Off) OfferRestart();
        }
        catch (Exception ex)
        {
            Status = Loc.F("Could not restore normal boot: {0}", ex.Message);
        }
    }

    private void OfferRestart()
    {
        var answer = MessageBox.Show(
            Loc.T("Restart now to apply the boot change?"),
            Loc.T("WinPure — Safe Mode"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        var (ok, error) = PowerService.Run(PowerAction.Restart, 0, force: false);
        if (!ok) Status = Loc.F("Could not restart: {0}", error ?? "");
    }
}
