using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class InstallableAppViewModel : ObservableObject
{
    public required InstallableApp App { get; init; }

    public string Name => App.Name;
    public string Description => App.Description;
    public string Group => App.Group;
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
/// The one page whose changes WinPure cannot undo: Restore never uninstalls an app. So it lives apart from
/// the tweaks, is in no preset, asks before every install, and judges each install by what winget sees
/// afterwards rather than by its exit code.
/// </summary>
public sealed class InstallerViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<InstallableAppViewModel> Apps { get; } = new();
    public RelayCommand InstallCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string _summary = "Checks which of these apps are installed when you open this page.";
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
        Summary = "Checking which apps winget sees installed…";
        try
        {
            var installed = await Task.Run(() => Winget.Backend.ReadInstalledIds());
            if (installed is null)
            {
                Summary = "winget is not available or did not answer, so installed apps cannot be checked. It comes with 'App Installer' from the Microsoft Store.";
                return;
            }

            int found = 0;
            foreach (var app in Apps)
            {
                app.IsInstalled = installed.Contains(app.App.Id);
                if (app.IsInstalled) found++;
                if (!app.IsRunning) app.StatusText = app.IsInstalled ? "Installed" : "Not detected";
            }
            HasChecked = true;
            Summary = $"{found} of {Apps.Count} already installed. Installed apps are not removed by Restore: uninstall them from Settings > Apps.";
        }
        finally
        {
            IsChecking = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task InstallAsync(InstallableAppViewModel app)
    {
        if (!Main.ConfirmDespiteGuards($"install {app.Name}", SystemGuards.ForRepair)) return;
        var answer = MessageBox.Show(
            $"Install {app.Name} with winget?\n\n" +
            "It is downloaded from its publisher, and installing accepts that app's license terms. " +
            "WinPure's Restore cannot undo this: to remove the app later, uninstall it from Settings > Apps.",
            "WinPure — Install app", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
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
        app.StatusText = "Installing… 0:00";
        using var ticker = new Timer(_ => app.StatusText = $"Installing… {RepairToolViewModel.Format(clock.Elapsed)}", null, 1000, 1000);
        Main.StatusText = $"Installing {app.Name}…";
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
                ? $"Installed ({elapsed})"
                : installed is null
                    ? $"winget finished with code 0x{exitCode:X8}, and installed apps could not be checked afterwards."
                    : $"Not installed: winget finished with code 0x{exitCode:X8} and does not see {app.Name} afterwards.";
            Main.StatusText = present ? $"{app.Name} installed." : $"{app.Name} was not installed — see its card.";
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
