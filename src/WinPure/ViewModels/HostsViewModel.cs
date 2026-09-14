using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class HostsViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand UndoCommand { get; }

    private string _loaded = "";
    private bool _readFailed;

    private string _content = "";
    public string Content
    {
        get => _content;
        set { if (Set(ref _content, value)) OnPropertyChanged(nameof(IsDirty)); }
    }

    /// <summary>True while the editor differs from what is on disk.</summary>
    public bool IsDirty => _content != _loaded;

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool HasLoaded { get; private set; }
    public bool HasBackup => HostsService.HasBackup;

    public HostsViewModel()
    {
        // Save is only offered when there are unsaved edits AND we actually read the file we would overwrite.
        SaveCommand = new RelayCommand(_ => Save(), _ => Main is { IsBusy: false } && IsDirty && !_readFailed);
        ResetCommand = new RelayCommand(_ => Reset(), _ => Main is { IsBusy: false });
        UndoCommand = new RelayCommand(_ => Undo(), _ => Main is { IsBusy: false } && HostsService.HasBackup);
    }

    /// <summary>Reads the current hosts file when the page opens.</summary>
    public void Load()
    {
        var content = HostsService.Read();
        if (content is null)
        {
            // Could not read it (locked by AV/VPN, mid-write by another tool). Never show a blank editor as
            // if that were the real content, and do not let Save overwrite a file we never saw.
            _readFailed = true;
            _loaded = "";
            Content = "";
            Status = Loc.T("Could not read the hosts file — it may be locked by another program. Close it, then reopen this page.");
        }
        else
        {
            _readFailed = false;
            _loaded = content;
            Content = content;
            Status = Loc.F("Editing {0}", HostsService.HostsPath);
        }
        HasLoaded = true;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasBackup));
    }

    private void Save()
    {
        if (!Main.ConfirmDespiteGuards(Loc.T("edit the hosts file"), SystemGuards.ForRepair)) return;
        var answer = MessageBox.Show(
            Loc.T("Write these changes to the hosts file? This affects how your PC resolves domain names. WinPure saves the current file first so you can undo this."),
            Loc.T("WinPure — Hosts file"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        Apply(HostsService.Save(_content), Loc.T("Saved. The previous hosts file was backed up."));
    }

    private void Reset()
    {
        if (!Main.ConfirmDespiteGuards(Loc.T("reset the hosts file to the Windows default"), SystemGuards.ForRepair)) return;
        var answer = MessageBox.Show(
            Loc.T("Replace the hosts file with the stock Windows default (comments only, no custom entries)? The current file is backed up first."),
            Loc.T("WinPure — Hosts file"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        Apply(HostsService.ResetToDefault(), Loc.T("Reset to the Windows default. The previous hosts file was backed up."));
    }

    private void Undo()
    {
        if (!Main.ConfirmDespiteGuards(Loc.T("undo the last hosts change"), SystemGuards.ForRepair)) return;
        var answer = MessageBox.Show(
            Loc.T("Restore the hosts file WinPure backed up before the last save? Any unsaved edits in the editor are discarded."),
            Loc.T("WinPure — Hosts file"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        Apply(HostsService.RestoreBackup(), Loc.T("Restored the hosts file WinPure backed up before the last save."));
    }

    private void Apply(HostsService.Result result, string okMessage)
    {
        if (result.Ok)
        {
            var reread = HostsService.Read();
            _readFailed = reread is null;
            _loaded = reread ?? "";
            Content = _loaded;
            Status = okMessage;
        }
        else
        {
            Status = Loc.F("Could not write the hosts file: {0}", result.Error ?? "");
        }
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasBackup));
    }
}
