using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class NavItem : ObservableObject
{
    public required string Label { get; init; }
    public required string Glyph { get; init; }
    public required PageViewModel Page { get; init; }
    internal MainViewModel? Owner { get; set; }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (Set(ref _isCurrent, value) && value && Owner is not null)
                Owner.CurrentNav = this;
        }
    }

    internal void SetCurrentSilently(bool value)
    {
        if (_isCurrent == value) return;
        _isCurrent = value;
        OnPropertyChanged(nameof(IsCurrent));
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly BackupManager _backupManager = new();
    private readonly TweakEngine _engine;
    private ScanContext _scanContext = new();

    public ObservableCollection<NavItem> NavItems { get; } = new();
    public List<TweakViewModel> AllTweaks { get; } = new();

    private readonly DashboardViewModel _dashboard;
    private readonly RestoreViewModel _restore;
    private readonly StartupViewModel _startup;

    public MainViewModel()
    {
        _engine = new TweakEngine(_backupManager);

        foreach (var tweak in TweakCatalog.Build())
        {
            var vm = new TweakViewModel(tweak);
            vm.SelectionChanged += UpdatePendingCount;
            AllTweaks.Add(vm);
        }

        _dashboard = new DashboardViewModel
        {
            Title = "Dashboard",
            Subtitle = "Overview of your system optimization state.",
            Main = this,
            OsInfo = GetOsInfo(),
        };
        _restore = new RestoreViewModel
        {
            Title = "Restore / Backup",
            Subtitle = "Every change WinPure makes is snapshotted first. Roll back any session here.",
            Main = this,
        };
        _restore.RestoreCommand = new RelayCommand(p => RestoreSession((BackupSessionViewModel)p!), _ => !IsBusy);
        _restore.DeleteCommand = new RelayCommand(p => DeleteSession((BackupSessionViewModel)p!), _ => !IsBusy);

        NavItems.Add(new NavItem { Label = "Home", Glyph = "", Page = _dashboard });
        AddCategory("Privacy", "", TweakCategory.Privacy, "Privacy & Telemetry",
            "Manage privacy settings and telemetry data collection to protect your privacy.");
        AddCategory("Apps", "", TweakCategory.Apps, "Bloatware & Apps",
            "Remove preinstalled apps and disable built-in features you don't use.");
        AddCategory("Services", "", TweakCategory.Services, "Services",
            "Disable optional Windows services to free memory and reduce background activity.");
        AddCategory("Performance", "", TweakCategory.Performance, "Performance",
            "Speed up shutdown, app handling and responsiveness.");
        AddCategory("UI", "", TweakCategory.UI, "UI & Personalization",
            "Clean up the Start Menu, taskbar and File Explorer.");
        AddCategory("Context Menu", "", TweakCategory.ContextMenu, "Context Menu",
            "Remove clutter from the right-click menu or restore the classic one.");
        AddCategory("Features", "", TweakCategory.Features, "Windows Features",
            "Turn optional parts of Windows off, or on. Most of these changes finish after a restart.");
        _startup = new StartupViewModel(_engine)
        {
            Title = "Startup Apps",
            Subtitle = "Everything that starts with Windows: apps, shortcuts in your Startup folder and scheduled tasks. "
                     + "Switching one off does not uninstall or delete anything - it flips the same switch Task Manager uses, and it takes effect right away.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Startup", Glyph = "", Page = _startup });

        NavItems.Add(new NavItem
        {
            Label = "Repair", Glyph = "",
            Page = new RepairViewModel
            {
                Title = "Repair & Maintenance",
                Subtitle = "One-click fixes for common Windows problems - system files, Windows Update, network and disk space.",
                Main = this,
            },
        });
        NavItems.Add(new NavItem { Label = "Restore", Glyph = "", Page = _restore });

        foreach (var item in NavItems) item.Owner = this;
        _currentNav = NavItems[0];
        _currentNav.SetCurrentSilently(true);

        ApplyCommand = new RelayCommand(_ => _ = ApplyChangesAsync(), _ => !IsBusy && PendingCount > 0);
        RescanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsBusy);
        SelectPresetCommand = new RelayCommand(p => SelectPreset((PresetLevel)p!), _ => !IsBusy);
    }

    private void AddCategory(string label, string glyph, TweakCategory category, string title, string subtitle)
    {
        var page = new CategoryPageViewModel { Title = title, Subtitle = subtitle, Category = category, Main = this };
        foreach (var vm in AllTweaks.Where(t => t.Category == category))
            page.Tweaks.Add(vm);
        NavItems.Add(new NavItem { Label = label, Glyph = glyph, Page = page });
    }

    // ---------------------------------------------------------------- state

    private NavItem _currentNav;
    public NavItem CurrentNav
    {
        get => _currentNav;
        set
        {
            if (value == _currentNav) return;
            var old = _currentNav;
            _currentNav = value;
            old?.SetCurrentSilently(false);
            value.SetCurrentSilently(true);
            if (value.Page == _restore) LoadBackups();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(ApplyHint));   // the hint differs on the Startup page
        }
    }

    public string OsInfo { get; } = GetOsInfo();

    public PageViewModel CurrentPage => CurrentNav.Page;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            // Every command's CanExecute depends on this. WPF only re-queries on input
            // events, so after a long operation ends the buttons would stay greyed out
            // until the user moved the mouse.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsIdle => !IsBusy;

    private string _statusText = "Ready.";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private int _pendingCount;
    public int PendingCount { get => _pendingCount; set { if (Set(ref _pendingCount, value)) OnPropertyChanged(nameof(ApplyHint)); } }

    public string ApplyHint => CurrentPage is StartupViewModel
        // The Startup page has no Apply step — promising one there would be a lie.
        // Kept to roughly the length of the line below: the status bar shares this row with
        // the scan result on the right, and a longer sentence overlaps it.
        ? "Startup switches apply immediately and are backed up."
        : PendingCount == 0
            ? "Changes will be applied after clicking Apply Changes."
            : $"{PendingCount} pending change{(PendingCount == 1 ? "" : "s")} — a backup is created before applying.";

    private PresetLevel? _activePreset;
    public PresetLevel? ActivePreset { get => _activePreset; set => Set(ref _activePreset, value); }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand SelectPresetCommand { get; }

    private void UpdatePendingCount() => PendingCount = AllTweaks.Count(t => t.IsDirty);

    /// <summary>Startup toggles apply immediately, so the dashboard's backup count moves too.</summary>
    internal void RefreshBackupCount() => _dashboard.BackupCount = _backupManager.ListSessions().Count;

    // ---------------------------------------------------------------- scan

    public async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning system state…";
        try
        {
            // Registry-backed tweaks resolve instantly; Appx/tasks/power need one PS pass.
            var (ctx, guards) = await Task.Run(() =>
            {
                // Started alongside the scan rather than after it: it is its own PowerShell call,
                // and waiting for it in sequence would add its whole duration to every scan.
                var bitLocker = Task.Run(GuardInputs.ReadBitLockerStatus);
                var c = ScanContext.Gather();
                return (c, SystemGuards.Evaluate(GuardInputs.Gather(c, bitLocker)));
            });
            _scanContext = ctx;
            _guards = guards;
            foreach (var g in guards) LogService.Log($"Guard: {g.Title} — {g.Detail}");
            foreach (var tweak in AllTweaks)
                tweak.RefreshStatus(_engine, ctx);
            _startup.Load(ctx);
            UpdatePendingCount();
            UpdateDashboard();
            int undetected = AllTweaks.Count(t => t.Status == TweakStatus.Unknown);
            StatusText = ctx.Warnings.Count == 0 && guards.Count > 0
                // Kept short: this text shares the status bar with the apply hint.
                ? $"Scan complete — {guards.Count} system warning{(guards.Count == 1 ? "" : "s")}, shown before applying."
                : ctx.Warnings.Count == 0
                ? $"Scan complete. {_dashboard.OptimizedCount} of {_dashboard.TotalCount} tweaks already optimized."
                // Name what could not be checked. "Some states are unknown" told the user
                // nothing, and an undetected tweak used to look exactly like an optimized one.
                : $"Scan incomplete — {string.Join("; ", ctx.Warnings)}. {undetected} tweak(s) could not be checked."
                  // An incomplete scan must not hide the system warnings it did find.
                  + (guards.Count > 0 ? $" {guards.Count} system warning{(guards.Count == 1 ? "" : "s")}." : "");
            LogService.Log(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateDashboard()
    {
        _dashboard.TotalCount = AllTweaks.Count;
        _dashboard.OptimizedCount = AllTweaks.Count(t => t.Status == TweakStatus.Optimized);
        _dashboard.PendingCount = AllTweaks.Count(t => t.Status == TweakStatus.Pending);
        _dashboard.BackupCount = _backupManager.ListSessions().Count;
    }

    // ---------------------------------------------------------------- presets

    public void SelectPreset(PresetLevel level)
    {
        ActivePreset = level;

        // Manual tweaks the user ticked by hand belong to the user, not to the preset:
        // wiping them silently makes a preset click feel like it undid your work.
        int keptManual = 0;
        foreach (var tweak in AllTweaks)
        {
            if (tweak.Preset == PresetLevel.Manual && tweak.IsSelected && !tweak.IsOptimized)
            {
                keptManual++;
                continue;
            }
            bool inPreset = tweak.Preset != PresetLevel.Manual && tweak.Preset <= level;
            // a preset switches its tweaks on but never reverts something already optimized
            tweak.IsSelected = inPreset || tweak.IsOptimized;
        }

        StatusText = keptManual == 0
            ? $"{level} preset selected — review and click Apply Changes."
            : $"{level} preset selected, keeping {keptManual} manual selection{(keptManual == 1 ? "" : "s")} — review and click Apply Changes.";
    }

    // ---------------------------------------------------------------- guards

    private List<GuardWarning> _guards = new();

    /// <summary>
    /// A brake, not a banner: a warning that only prints stops working the moment the next step
    /// is one click away. Returns true when there is nothing to warn about or the user chose to
    /// go ahead anyway. The default button is No.
    /// </summary>
    internal bool ConfirmDespiteGuards(string what, Func<IEnumerable<GuardWarning>, List<GuardWarning>>? select = null)
    {
        // Which guards concern which action is decided in SystemGuards (ForApply, ForRestore,
        // ForStartup, ForRepair), where it is tested — not in a lambda at each call site.
        var relevant = (select ?? SystemGuards.ForApply)(_guards);
        if (relevant.Count == 0) return true;

        var answer = MessageBox.Show(
            $"Before you {what}, WinPure found:\n\n{SystemGuards.Describe(relevant)}\n\nContinue anyway?",
            "WinPure — Check before continuing", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        bool go = answer == MessageBoxResult.Yes;
        LogService.Log($"Guards shown before '{what}' ({string.Join(", ", relevant.Select(g => g.Id))}): user chose {(go ? "to continue" : "to stop")}");
        return go;
    }

    // ---------------------------------------------------------------- apply

    public async Task ApplyChangesAsync()
    {
        var changes = AllTweaks
            .Where(t => t.IsDirty)
            .Select(t => (t.Tweak, apply: t.IsSelected))
            .ToList();
        if (changes.Count == 0) return;
        if (!ConfirmDespiteGuards("apply these changes")) return;

        var irreversible = changes.Where(c => c.apply && !c.Tweak.FullyReversible).ToList();
        if (irreversible.Count > 0)
        {
            var names = string.Join("\n  • ", irreversible.Select(c => c.Tweak.Name));
            var answer = MessageBox.Show(
                $"These changes remove apps and can only be undone by reinstalling from the Microsoft Store:\n\n  • {names}\n\nContinue?",
                "WinPure — Confirm app removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusText = msg);
        try
        {
            var results = await Task.Run(() => _engine.ApplyChanges(changes, progress));
            if (results.Any(r => r.Success && r.Tweak.NotifiesThemeChange))
                NativeMethods.BroadcastThemeChange();
            int failed = results.Count(r => !r.Success);
            bool needsExplorer = results.Any(r => r.Success && r.Tweak.RequiresExplorerRestart);
            bool needsReboot = results.Any(r => r.Success && r.Tweak.RequiresRestart);

            StatusText = failed == 0
                ? $"Done — {results.Count} change{(results.Count == 1 ? "" : "s")} applied."
                : $"Finished with {failed} error{(failed == 1 ? "" : "s")} — see the log in %AppData%\\WinPure\\Logs.";

            if (needsExplorer)
            {
                var answer = MessageBox.Show(
                    "Some changes need File Explorer to restart to take effect.\nRestart Explorer now?",
                    "WinPure", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                    await Task.Run(() => PowerShellRunner.Run("Stop-Process -Name explorer -Force"));
            }
            else if (needsReboot)
            {
                MessageBox.Show("Some changes will take full effect after a reboot.", "WinPure",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            IsBusy = false;
        }
        await ScanAsync();
    }

    // ---------------------------------------------------------------- restore

    public void LoadBackups()
    {
        _restore.Sessions.Clear();
        foreach (var session in _backupManager.ListSessions())
            _restore.Sessions.Add(new BackupSessionViewModel { Session = session });
        _restore.IsEmpty = _restore.Sessions.Count == 0;
    }

    private async void RestoreSession(BackupSessionViewModel vm)
    {
        // Restoring as the wrong user writes the per-user half of the backup into the wrong profile.
        if (!ConfirmDespiteGuards("restore this backup", SystemGuards.ForRestore)) return;

        var answer = MessageBox.Show(
            $"Restore the snapshot from {vm.Title}?\nAll {vm.Session.Entries.Count} captured values will be written back.",
            "WinPure — Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = "Restoring backup…";
        try
        {
            int failures = await Task.Run(() => _backupManager.RestoreSession(vm.Session));
            // a snapshot may include theme values — make open apps repaint
            NativeMethods.BroadcastThemeChange();
            StatusText = failures == 0 ? "Backup restored." : $"Backup restored with {failures} errors (see log).";
        }
        finally
        {
            IsBusy = false;
        }
        await ScanAsync();
    }

    private void DeleteSession(BackupSessionViewModel vm)
    {
        // Backups live in the profile of the account WinPure runs as. Elevated as someone else, this
        // list is that account's backups, not the signed-in user's — and deleting cannot be undone.
        if (!ConfirmDespiteGuards("delete this backup", SystemGuards.ForRestore)) return;

        var answer = MessageBox.Show(
            $"Delete the backup from {vm.Title}? This cannot be undone.",
            "WinPure — Delete backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _backupManager.DeleteSession(vm.Session);
        LoadBackups();
        UpdateDashboard();
    }

    // ---------------------------------------------------------------- misc

    private static string GetOsInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName") as string ?? "Windows";
            string display = key?.GetValue("DisplayVersion") as string ?? "";
            string build = key?.GetValue("CurrentBuildNumber") as string ?? "";
            // ProductName still says "Windows 10" on Win11; fix by build number
            if (int.TryParse(build, out int b) && b >= 22000)
                product = product.Replace("Windows 10", "Windows 11");
            return $"{product}\n{display} {build}".Trim();
        }
        catch
        {
            return "Windows";
        }
    }
}
