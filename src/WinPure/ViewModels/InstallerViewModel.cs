using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class InstallableAppViewModel : ObservableObject
{
    public required InstallableApp App { get; init; }

    // Product names are not translated.
    public string Name => App.Name;
    public string Description => Loc.T(App.Description);
    public string Group => Loc.T(App.Group);
    public string Icon => App.Icon;

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set { if (Set(ref _isInstalled, value)) OnPropertyChanged(nameof(CanInstall)); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(CanInstall)); }
    }

    public bool CanInstall => !IsInstalled && !IsRunning;
}

/// <summary>
/// One of the two pages whose changes WinPure cannot undo (Remove Apps is the other): Restore never uninstalls an app. So it lives apart from
/// the tweaks, is in no preset, asks before every install, and judges each install by what winget sees
/// afterwards rather than by its exit code.
/// </summary>
public sealed class InstallerViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<InstallableAppViewModel> Apps { get; } = new();
    public ObservableCollection<InstallableAppViewModel> SearchResults { get; } = new();
    public RelayCommand InstallCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SearchCommand { get; }

    private IReadOnlySet<string>? _installed;

    private string _searchQuery = "";
    public string SearchQuery { get => _searchQuery; set => Set(ref _searchQuery, value); }

    private string _searchSummary = "";
    public string SearchSummary { get => _searchSummary; set => Set(ref _searchSummary, value); }

    private bool _hasSearched;
    public bool HasSearched { get => _hasSearched; set => Set(ref _hasSearched, value); }

    private string _summary = Loc.T("Checks which of these apps are installed when you open this page.");
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    private bool _isChecking;
    public bool IsChecking { get => _isChecking; set => Set(ref _isChecking, value); }

    /// <summary>A successful check has run at least once; opening the page again does not repeat a 15-second export.</summary>
    public bool HasChecked { get; private set; }

    public InstallerViewModel()
    {
        foreach (var app in AppInstallerCatalog.Build())
            Apps.Add(new InstallableAppViewModel { App = app });
        InstallCommand = new RelayCommand(p => _ = InstallAsync((InstallableAppViewModel)p!),
            p => Main is { IsBusy: false } && !IsChecking && p is InstallableAppViewModel { CanInstall: true });
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => Main is { IsBusy: false } && !IsChecking);
        SearchCommand = new RelayCommand(_ => _ = SearchAsync(), _ => Main is { IsBusy: false } && !IsChecking && SearchQuery.Trim().Length > 0);
    }

    /// <summary>
    /// Searches the whole winget catalog for anything, not just the curated list, so any app can be installed
    /// from here. Results reuse the same card and install flow; an id winget already sees installed is marked so.
    /// </summary>
    public async Task SearchAsync()
    {
        string query = SearchQuery.Trim();
        if (query.Length == 0 || IsChecking) return;
        IsChecking = true;
        SearchResults.Clear();
        SearchSummary = Loc.F("Searching winget for \"{0}\"…", query);
        try
        {
            var results = await Task.Run(() => Winget.Backend.Search(query));
            foreach (var r in results)
            {
                var app = new InstallableApp(r.Id, r.Name, r.Id, "winget", ((char)0xE896).ToString());
                var vm = new InstallableAppViewModel { App = app };
                vm.IsInstalled = _installed?.Contains(r.Id) == true;
                vm.StatusText = vm.IsInstalled ? Loc.T("Installed") : Loc.T("Not detected");
                SearchResults.Add(vm);
            }
            HasSearched = true;
            SearchSummary = results.Count == 0
                ? Loc.F("winget found nothing for \"{0}\".", query)
                : Loc.F("{0} result(s) from winget. These install from their publisher, like the list below.", results.Count);
        }
        finally
        {
            IsChecking = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Asks winget which packages it sees installed: one export call, only when the page is opened, never
    /// during the tweak scan. "Not detected" is the honest word — winget only lists installs it can match
    /// to its catalog, so an app installed some other way can be present and still not be listed.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        Summary = Loc.T("Checking which apps winget sees installed…");
        try
        {
            var installed = await Task.Run(() => Winget.Backend.ReadInstalledIds());
            _installed = installed;
            if (installed is null)
            {
                Summary = Loc.T("winget is not available or did not answer, so installed apps cannot be checked. It comes with 'App Installer' from the Microsoft Store.");
                return;
            }

            int found = 0;
            foreach (var app in Apps)
            {
                app.IsInstalled = installed.Contains(app.App.Id);
                if (app.IsInstalled) found++;
                if (!app.IsRunning) app.StatusText = app.IsInstalled ? Loc.T("Installed") : Loc.T("Not detected");
            }
            HasChecked = true;
            Summary = Loc.F("{0} of {1} already installed. Installed apps are not removed by Restore: uninstall them from Settings > Apps.", found, Apps.Count);
        }
        finally
        {
            IsChecking = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task InstallAsync(InstallableAppViewModel app)
    {
        if (!Main.ConfirmDespiteGuards(Loc.F("install {0}", app.Name), SystemGuards.ForRepair)) return;
        var answer = WinPure.Views.WinPureDialog.Show(
            Loc.F("Install {0} with winget?\n\nIt is downloaded from its publisher, and installing accepts that app's license terms. WinPure's Restore cannot undo this: to remove the app later, uninstall it from Settings > Apps.", app.Name),
            Loc.T("WinPure — Install app"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await InstallConfirmedAsync(app);
    }

    /// <summary>
    /// The install itself, once the user has said yes. The badge follows what winget sees afterwards: an
    /// installer can report success and leave nothing behind, or fail after installing.
    /// </summary>
    internal async Task InstallConfirmedAsync(InstallableAppViewModel app)
    {
        Main.IsBusy = true;
        app.IsRunning = true;
        var clock = Stopwatch.StartNew();
        app.StatusText = Loc.F("Installing… {0}", "0:00");
        using var ticker = new Timer(_ => app.StatusText = Loc.F("Installing… {0}", RepairToolViewModel.Format(clock.Elapsed)), null, 1000, 1000);
        Main.StatusText = Loc.F("Installing {0}…", app.Name);
        LogService.Log($"Install started: {app.App.Id}");
        try
        {
            int exitCode = await Task.Run(() => Winget.Backend.Install(app.App.Id));
            var installed = await Task.Run(() => Winget.Backend.ReadInstalledIds());
            ticker.Change(Timeout.Infinite, Timeout.Infinite);
            bool present = installed?.Contains(app.App.Id) == true;
            string elapsed = RepairToolViewModel.Format(clock.Elapsed);

            app.IsInstalled = present;
            app.StatusText = present
                ? Loc.F("Installed ({0})", elapsed)
                : installed is null
                    ? Loc.F("winget finished with code 0x{0:X8}, and installed apps could not be checked afterwards.", exitCode)
                    : Loc.F("Not installed: winget finished with code 0x{0:X8} and does not see {1} afterwards.", exitCode, app.Name);
            Main.StatusText = present ? Loc.F("{0} installed.", app.Name) : Loc.F("{0} was not installed — see its card.", app.Name);
            LogService.Log($"Install finished in {elapsed}: {app.App.Id}, exit 0x{exitCode:X8}, detected afterwards={present}");
        }
        finally
        {
            ticker.Change(Timeout.Infinite, Timeout.Infinite);
            app.IsRunning = false;
            Main.IsBusy = false;
        }
    }
}
