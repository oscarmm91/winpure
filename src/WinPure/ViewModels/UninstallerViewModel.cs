using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One installed program on the uninstaller page.</summary>
public sealed class InstalledProgramViewModel : ObservableObject
{
    public required InstalledProgram Program { get; init; }

    public string Name => Program.Name;
    public string Detail => string.Join("  ·  ", new[] { Program.Version, Program.Publisher }.Where(s => s.Length > 0));

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(CanUninstall)); } }

    private bool _removed;
    public bool Removed { get => _removed; set { if (Set(ref _removed, value)) OnPropertyChanged(nameof(CanUninstall)); } }

    public bool CanUninstall => !IsRunning && !Removed;

    // Filtering hides rows via Visibility rather than removing them from a CollectionView, which would crash the
    // WPF accessibility layer (ItemAutomationPeer NRE) when the removed row's peer is queried.
    private bool _visible = true;
    public bool IsVisibleUnderFilter { get => _visible; set => Set(ref _visible, value); }
}

/// <summary>
/// The generic uninstaller: lists installed programs and runs their own uninstaller. Like Remove Apps and
/// Install, this is NOT undone by Restore, so it sits apart, shows no preset, and confirms before acting.
/// Success is judged by whether the program is gone when the list is read again — never by an exit code.
/// </summary>
public sealed class UninstallerViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<InstalledProgramViewModel> Programs { get; } = new();
    public RelayCommand UninstallCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) ApplyFilter(); } }

    private string _summary = Loc.T("Lists your installed programs when you open this page.");
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    private bool _isChecking;
    public bool IsChecking { get => _isChecking; set => Set(ref _isChecking, value); }

    public bool HasChecked { get; private set; }

    public UninstallerViewModel()
    {
        UninstallCommand = new RelayCommand(p => _ = UninstallAsync((InstalledProgramViewModel)p!),
            p => Main is { IsBusy: false } && !IsChecking && p is InstalledProgramViewModel { CanUninstall: true });
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => Main is { IsBusy: false } && !IsChecking);
    }

    /// <summary>Reads the installed programs from the Uninstall registry keys when the page opens.</summary>
    public async Task RefreshAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        Summary = Loc.T("Reading your installed programs…");
        try
        {
            var programs = await Task.Run(() => UninstallService.Read());
            Programs.Clear();
            foreach (var p in programs) Programs.Add(new InstalledProgramViewModel { Program = p });
            ApplyFilter();
            HasChecked = true;
            Summary = Loc.F("{0} installed programs. Uninstalling runs each program's own uninstaller and is not undone by Restore.", Programs.Count);
        }
        finally
        {
            IsChecking = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ApplyFilter()
    {
        var q = _filter.Trim();
        foreach (var p in Programs)
            p.IsVisibleUnderFilter = q.Length == 0
                || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || p.Program.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task UninstallAsync(InstalledProgramViewModel vm)
    {
        if (!vm.CanUninstall) return;
        if (!Main.ConfirmDespiteGuards(Loc.F("uninstall {0}", vm.Name), SystemGuards.ForRepair)) return;
        var answer = WinPure.Views.WinPureDialog.Show(
            Loc.F("Uninstall {0}? This runs the program's own uninstaller. WinPure's Restore cannot undo this — to get the program back you would reinstall it.", vm.Name),
            Loc.T("WinPure — Uninstall program"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        Main.IsBusy = true;
        vm.IsRunning = true;
        vm.StatusText = Loc.T("Uninstalling…");
        Main.StatusText = Loc.F("Uninstalling {0}…", vm.Name);
        LogService.Log($"Uninstall started: {vm.Program.Name} ({vm.Program.Key})");
        try
        {
            await Task.Run(() => UninstallService.Run(vm.Program));
            var after = await Task.Run(() => UninstallService.Read());
            bool present = !UninstallService.IsGone(vm.Program, after);

            vm.Removed = !present;
            vm.StatusText = present
                ? Loc.T("Still installed — finish the uninstaller, then Refresh.")
                : Loc.T("Uninstalled.");
            Main.StatusText = present
                ? Loc.F("{0} is still installed — see its row.", vm.Name)
                : Loc.F("{0} uninstalled.", vm.Name);
            LogService.Log($"Uninstall finished: {vm.Program.Name}, gone afterwards={!present}");
        }
        catch (Exception ex)
        {
            // A malformed uninstall command can fail to launch — report it instead of leaving the row stuck on
            // "Uninstalling…" with an unobserved task exception.
            vm.StatusText = Loc.T("Could not run the uninstaller — see the log.");
            Main.StatusText = Loc.F("{0} could not be uninstalled — see the log.", vm.Name);
            LogService.Log($"Uninstall error for {vm.Program.Name}: {ex.Message}");
        }
        finally
        {
            vm.IsRunning = false;
            Main.IsBusy = false;
        }
    }
}
