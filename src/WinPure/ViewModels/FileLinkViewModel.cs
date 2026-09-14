using Microsoft.Win32;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>
/// The "Move folder to another drive" page. It copies a folder to another drive, verifies the copy, deletes the
/// original and leaves a junction in its place, so programs still find it. Copy-then-verify-then-delete means an
/// interrupted move never loses data, and it refuses system-critical folders and same-drive moves.
/// </summary>
public sealed class FileLinkViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }

    private string _source = "";
    public string Source { get => _source; set { if (Set(ref _source, value)) UpdatePreview(); } }

    private string _destinationParent = "";
    public string DestinationParent { get => _destinationParent; set => Set(ref _destinationParent, value); }

    private string _preview = "";
    public string Preview { get => _preview; set => Set(ref _preview, value); }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public RelayCommand PickSourceCommand { get; }
    public RelayCommand PickDestinationCommand { get; }
    public RelayCommand MoveCommand { get; }

    public FileLinkViewModel()
    {
        PickSourceCommand = new RelayCommand(_ => PickSource());
        PickDestinationCommand = new RelayCommand(_ => PickDestination());
        MoveCommand = new RelayCommand(_ => _ = MoveAsync(),
            _ => Main is { IsBusy: false } && Source.Length > 0 && DestinationParent.Length > 0);
    }

    private void PickSource()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose the folder to move") };
        if (dialog.ShowDialog() == true) Source = dialog.FolderName;
    }

    private void PickDestination()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose the destination drive or folder") };
        if (dialog.ShowDialog() == true) DestinationParent = dialog.FolderName;
    }

    private void UpdatePreview()
    {
        Status = "";
        if (Source.Length == 0) { Preview = ""; return; }
        var reject = FileLinkService.RejectSource(Source);
        if (reject is not null) { Preview = Loc.F("Cannot move this folder: {0}.", reject); return; }
        var size = FileLinkService.Backend.FolderSize(Source);
        Preview = Loc.F("About {0} to move.", FormatGB(size));
    }

    private async Task MoveAsync()
    {
        if (FileLinkService.RejectSource(Source) is { } reject)
        {
            Status = Loc.F("Cannot move this folder: {0}.", reject);
            return;
        }
        if (!Main.ConfirmDespiteGuards(Loc.T("move a folder to another drive"), SystemGuards.ForRepair)) return;
        var answer = System.Windows.MessageBox.Show(
            Loc.T("Move this folder to the other drive and leave a junction behind? The data is copied and verified BEFORE the original is removed, so nothing is lost if it is interrupted. Continue?"),
            Loc.T("WinPure — Move folder"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        Main.IsBusy = true;
        Status = Loc.T("Copying… this can take a while for a large folder.");
        Main.StatusText = Loc.T("Moving a folder to another drive…");
        try
        {
            var result = await Task.Run(() => FileLinkService.MoveToAnotherDrive(Source, DestinationParent));
            Status = result.Message;
            Main.StatusText = result.Ok ? Loc.T("Folder moved and linked.") : Loc.T("The folder was not moved — see the page.");
            if (result.Ok) UpdatePreview();
        }
        catch (Exception ex)
        {
            Status = Loc.T("The move failed — see the log.");
            Main.StatusText = Loc.T("The folder was not moved — see the page.");
            LogService.Log($"Move folder failed: {ex.Message}");
        }
        finally
        {
            Main.IsBusy = false;
        }
    }

    private static string FormatGB(long bytes) => bytes >= 1L << 30
        ? $"{bytes / 1024.0 / 1024 / 1024:0.0} GB"
        : $"{bytes / 1024.0 / 1024:0.0} MB";
}
