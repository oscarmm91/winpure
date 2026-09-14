using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinPure.Models;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class NavItem : ObservableObject
{
    private readonly string _label = "";
    /// <summary>Translated when set: the English passed in is the key.</summary>
    public required string Label { get => _label; init => _label = Loc.T(value); }
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

    private int _pendingCount;
    /// <summary>Tweaks on this page whose toggle differs from the system, shown as a badge in the sidebar.</summary>
    public int PendingCount
    {
        get => _pendingCount;
        set { if (Set(ref _pendingCount, value)) OnPropertyChanged(nameof(HasPending)); }
    }
    public bool HasPending => PendingCount > 0;
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
    private readonly CleanupViewModel _cleanup;
    private readonly MemoryViewModel _memory;
    private readonly PowerViewModel _power;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly HardwareViewModel _hardware;
    private readonly DispatcherTimer _liveTimer;
    private readonly DnsViewModel _dns;
    private readonly HostsViewModel _hosts;
    private readonly PathViewModel _path;
    private readonly InstallerViewModel _installer;

    /// <summary>The Remove Apps banner, also shown above search results that include an app removal.</summary>
    private const string RemoveAppsWarning = "WinPure cannot undo anything on this page: Restore does not bring an app back. To get one back, reinstall it yourself, from the Microsoft Store (OneDrive from microsoft.com).";

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
        // The count cards open a filtered list of what they count; the Backups card jumps to Restore.
        _dashboard.ShowAllCommand = new RelayCommand(_ => ShowStatusFilter(null), _ => !IsBusy);
        _dashboard.ShowOptimizedCommand = new RelayCommand(_ => ShowStatusFilter(TweakStatus.Optimized), _ => !IsBusy);
        _dashboard.ShowPendingCommand = new RelayCommand(_ => ShowStatusFilter(TweakStatus.Pending), _ => !IsBusy);
        _dashboard.ShowBackupsCommand = new RelayCommand(_ => CurrentNav = NavItems.First(n => n.Page == _restore), _ => !IsBusy);
        _restore = new RestoreViewModel
        {
            Title = "Restore / Backup",
            Subtitle = "Every change WinPure makes is snapshotted first. Roll back any session here. App removals are the exception: no backup brings an app back.",
            Main = this,
        };
        _restore.RestoreCommand = new RelayCommand(p => RestoreSession((BackupSessionViewModel)p!), _ => !IsBusy);
        _restore.DeleteCommand = new RelayCommand(p => DeleteSession((BackupSessionViewModel)p!), _ => !IsBusy);

        NavItems.Add(new NavItem { Label = "Home", Glyph = "", Page = _dashboard });
        AddCategory("Privacy", "", TweakCategory.Privacy, "Privacy & Telemetry",
            "Manage privacy settings and telemetry data collection to protect your privacy.");
        AddCategory("Apps", "", TweakCategory.Apps, "Apps & AI",
            "Turn off built-in AI features and Game Bar extras you don't use. Uninstalling apps has its own page, Remove Apps.");
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
        AddCategory("Edge", ((char)0xE774).ToString(), TweakCategory.Edge, "Microsoft Edge",
            "Clean up Edge's new tab page and stop its promotions and prompts. These are Edge policies: while any of them is on, Edge says it is managed by your organization.",
            showsPresets: false);   // every Edge tweak is Manual, so preset buttons would do nothing here
        _startup = new StartupViewModel(_engine)
        {
            Title = "Startup Apps",
            Subtitle = "Everything that starts with Windows: apps, shortcuts in your Startup folder and scheduled tasks. "
                     + "Switching one off does not uninstall or delete anything - it flips the same switch Task Manager uses. "
                     + "Toggle the ones you want, then click Apply Changes.",
        };
        // The Startup page shares the Apply bar; when its pending count changes, the bar re-reads it.
        _startup.PendingChanged += () => { if (CurrentPage is StartupViewModel) UpdatePendingCount(); };
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
        _cleanup = new CleanupViewModel
        {
            Title = "Clean up",
            Subtitle = "Free disk space by deleting caches and junk Windows recreates as needed. Cleaning is permanent — "
                     + "there is no backup for a deleted cache — so it is not undone by Restore. Rows marked \"your files\" are your own data.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Clean up", Glyph = ((char)0xEA99).ToString(), Page = _cleanup });
        _memory = new MemoryViewModel
        {
            Title = "Free up memory",
            Subtitle = "Trim app memory and clear the standby cache to lower RAM use. Windows already manages memory well, "
                     + "so this is a manual, honest nudge — any gain is usually short-lived.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Memory", Glyph = ((char)0xE950).ToString(), Page = _memory });
        _power = new PowerViewModel
        {
            Title = "Power",
            Subtitle = "Schedule a shutdown or restart, or sleep, hibernate, lock and sign out right now.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Power", Glyph = ((char)0xE7E8).ToString(), Page = _power });
        _diagnostics = new DiagnosticsViewModel
        {
            Title = "Diagnostics",
            Subtitle = "A read-only summary of this PC, and a support bundle you can save and share when something needs troubleshooting.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Diagnostics", Glyph = ((char)0xE9D9).ToString(), Page = _diagnostics });
        _hardware = new HardwareViewModel
        {
            Title = "Hardware",
            Subtitle = "A read-only look at what is inside this PC — processor, memory, graphics, drives and motherboard. "
                     + "Read from the registry, so it needs no extra driver.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Hardware", Glyph = ((char)0xE964).ToString(), Page = _hardware });
        _dns = new DnsViewModel(_engine)
        {
            Title = "DNS servers",
            Subtitle = "Switch every network adapter to a faster or ad-blocking DNS resolver. WinPure saves your current DNS first, so Restore can put it back.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "DNS", Glyph = ((char)0xE968).ToString(), Page = _dns });
        _hosts = new HostsViewModel
        {
            Title = "Hosts file",
            Subtitle = "Edit the Windows hosts file — the local map of names to IP addresses that is consulted before DNS. "
                     + "WinPure backs it up before every save and can reset it to the Windows default.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Hosts", Glyph = ((char)0xE8A5).ToString(), Page = _hosts });
        _path = new PathViewModel(_engine)
        {
            Title = "PATH editor",
            Subtitle = "Review the folders on your PATH and remove the dead, duplicate or empty ones. WinPure backs up the "
                     + "whole PATH first, so Restore can put it back. Entries on drives that are not connected are left alone.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "PATH", Glyph = ((char)0xE8FD).ToString(), Page = _path });
        NavItems.Add(new NavItem { Label = "Restore", Glyph = "", Page = _restore });

        // The two pages whose changes Restore cannot undo sit together at the bottom, apart from everything else.
        // Remove Apps shows no preset buttons — a preset never ticks what cannot be undone — and keeps its warning in view.
        AddCategory("Remove Apps", ((char)0xE74D).ToString(), TweakCategory.RemoveApps, "Remove Apps",
            "Uninstall preinstalled apps you do not want. No preset ever ticks these, and removing them asks once more before it runs.",
            showsPresets: false,
            warning: RemoveAppsWarning);
        _installer = new InstallerViewModel
        {
            Title = "Install Apps",
            Subtitle = "Popular apps installed with winget, straight from their publishers. Like an app removal, an install is not undone by Restore — remove an app from Settings > Apps.",
            Main = this,
        };
        NavItems.Add(new NavItem { Label = "Install", Glyph = ((char)0xE896).ToString(), Page = _installer });

        foreach (var item in NavItems) item.Owner = this;
        _currentNav = NavItems[0];
        _currentNav.SetCurrentSilently(true);

        // The dashboard shows live RAM and disk figures; refresh them every 2 s, but only while it is the
        // page on screen (the setter stops the timer on navigation away). The app opens on the dashboard.
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveTimer.Tick += (_, _) => _dashboard.RefreshLive();
        _dashboard.RefreshLive();
        _liveTimer.Start();

        ApplyCommand = new RelayCommand(_ => _ = ApplyChangesAsync(), _ => !IsBusy && PendingCount > 0);
        RescanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsBusy);
        SelectPresetCommand = new RelayCommand(p => SelectPreset((PresetLevel)p!), _ => !IsBusy);
        ExportConfigCommand = new RelayCommand(_ => ExportConfigToFile(), _ => !IsBusy);
        ImportConfigCommand = new RelayCommand(_ => ImportConfigFromFile(), _ => !IsBusy);
    }

    private void AddCategory(string label, string glyph, TweakCategory category, string title, string subtitle,
        bool showsPresets = true, string warning = "")
    {
        var page = new CategoryPageViewModel
        {
            Title = title, Subtitle = subtitle, Category = category, Main = this,
            ShowsPresets = showsPresets, Warning = warning,
        };
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
            if (IsSearching || _searchPage is not null)
            {
                // Picking a page in the sidebar leaves the search results — or a Dashboard filter view,
                // which is the same transient slot with no search text — including the page the search
                // started from, whose button was unchecked while searching.
                _searchText = "";
                _searchPage = null;
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(IsSearching));
                if (value == _currentNav)
                {
                    value.SetCurrentSilently(true);
                    OnPropertyChanged(nameof(CurrentPage));
                    SyncLiveTimer();        // the filter view is gone; the dashboard may be visible again
                    UpdatePendingCount();   // the Apply bar counts this page's pending changes
                    return;
                }
            }
            if (value == _currentNav) return;
            var old = _currentNav;
            _currentNav = value;
            old?.SetCurrentSilently(false);
            value.SetCurrentSilently(true);
            if (value.Page == _restore) LoadBackups();
            if (value.Page == _installer && !_installer.HasChecked) _ = _installer.RefreshAsync();
            if (value.Page == _cleanup && !_cleanup.HasMeasured) _ = _cleanup.MeasureAllAsync();
            if (value.Page == _dns && !_dns.HasLoaded) _ = _dns.LoadAsync();
            if (value.Page == _hosts && !_hosts.HasLoaded) _hosts.Load();
            if (value.Page == _memory) _memory.Load();   // re-read RAM each time the page opens
            if (value.Page == _diagnostics && !_diagnostics.HasLoaded) _diagnostics.Load();
            if (value.Page == _hardware && !_hardware.HasLoaded) _hardware.Load();
            if (value.Page == _path && !_path.HasLoaded) _path.Load();
            SyncLiveTimer();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPage));
            UpdatePendingCount();   // the Apply bar counts this page's pending changes (startup vs tweaks)
        }
    }

    /// <summary>The live RAM/disk timer ticks only while the dashboard is the page actually on screen —
    /// keyed on CurrentPage, so a Dashboard-launched search or filter view (which keeps CurrentNav on the
    /// dashboard) still stops it.</summary>
    private void SyncLiveTimer()
    {
        if (CurrentPage == _dashboard) { _dashboard.RefreshLive(); _liveTimer?.Start(); }
        else _liveTimer?.Stop();
    }

    public string OsInfo { get; } = GetOsInfo();

    public PageViewModel CurrentPage => _searchPage ?? CurrentNav.Page;

    // ---------------------------------------------------------------- search

    private string _searchText = "";
    private CategoryPageViewModel? _searchPage;

    /// <summary>
    /// Looks through every tweak's name, description and help. While it holds text, the results
    /// replace the current page; clearing it or clicking the sidebar goes back. With over a hundred
    /// tweaks, browsing category by category stops being a way to find one.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? "")) return;
            _currentNav.SetCurrentSilently(!IsSearching);
            _searchPage = IsSearching ? BuildSearchPage(_searchText.Trim()) : null;
            SyncLiveTimer();   // searching from the dashboard hides its live stats
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(ApplyHint));
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    /// <summary>
    /// Shows a transient page listing every tweak with the given status (or all of them), reusing the search
    /// page's slot. Clicking any sidebar item leaves it. This is what the Dashboard count cards open.
    /// </summary>
    public void ShowStatusFilter(TweakStatus? status)
    {
        var matches = status is null ? AllTweaks.ToList() : AllTweaks.Where(t => t.Status == status.Value).ToList();
        _searchText = "";
        _searchPage = BuildFilterPage(status, matches);
        _currentNav.SetCurrentSilently(false);
        SyncLiveTimer();   // a filter view replaces the dashboard's live stats
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(CurrentPage));
        UpdatePendingCount();
    }

    private CategoryPageViewModel BuildFilterPage(TweakStatus? status, List<TweakViewModel> matches)
    {
        string title = status switch
        {
            TweakStatus.Pending => Loc.N("Pending changes"),
            TweakStatus.Optimized => Loc.N("Already optimized"),
            _ => Loc.N("All tweaks"),
        };
        string subtitle = status switch
        {
            TweakStatus.Pending => matches.Count == 1
                ? Loc.T("1 tweak you have not applied, from every category. Tick the ones you want and Apply.")
                : Loc.F("{0} tweaks you have not applied, from every category. Tick the ones you want and Apply.", matches.Count),
            TweakStatus.Optimized => matches.Count == 1
                ? Loc.T("1 tweak already applied on this PC, from every category.")
                : Loc.F("{0} tweaks already applied on this PC, from every category.", matches.Count),
            _ => Loc.F("Every one of WinPure's {0} tweaks, from every category.", matches.Count),
        };
        var page = new CategoryPageViewModel
        {
            Title = title,
            Subtitle = subtitle,
            Main = this,
            ShowsPresets = !matches.Any(t => !t.FullyReversible),
            Warning = matches.Any(t => !t.FullyReversible) ? RemoveAppsWarning : "",
        };
        foreach (var tweak in matches) page.Tweaks.Add(tweak);
        return page;
    }

    private CategoryPageViewModel BuildSearchPage(string query)
    {
        var matches = AllTweaks.Where(t => MatchesSearch(t, query)).ToList();
        var page = new CategoryPageViewModel
        {
            // PageViewModel translates what it is given, so the title goes in as its English key. The subtitle has to
            // be formatted first; the lookup PageViewModel then makes on it finds nothing and leaves it as it is.
            Title = Loc.N("Search"),
            Subtitle = matches.Count == 0
                ? Loc.F("No tweak mentions \"{0}\". Try a single word, such as an app or a Windows feature.", query)
                : matches.Count == 1
                ? Loc.F("1 tweak mentioning \"{0}\", from every category.", query)
                : Loc.F("{0} tweaks mentioning \"{1}\", from every category.", matches.Count, query),
            Main = this,
            // Results that include an app removal look like Remove Apps: its warning, and no preset buttons (the two
            // share one row of the page).
            ShowsPresets = !matches.Any(t => !t.FullyReversible),
            Warning = matches.Any(t => !t.FullyReversible) ? RemoveAppsWarning : "",
        };
        // The same TweakViewModel objects as on their own pages: a toggle here is the same toggle.
        foreach (var tweak in matches) page.Tweaks.Add(tweak);
        return page;
    }

    private static bool MatchesSearch(TweakViewModel tweak, string query)
    {
        // Case and accents ignored: an accent typed by habit on a Spanish keyboard still finds it.
        // Both languages are searched: someone who knows a setting by its English name still finds it
        // in the Spanish app.
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return new[] { tweak.Name, tweak.Description, tweak.Help, tweak.Tweak.Name, tweak.Tweak.Description, tweak.Tweak.Help }
            .Any(text => compare.IndexOf(text, query, options) >= 0);
    }

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

    private string _statusText = Loc.T("Ready.");
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private int _pendingCount;
    public int PendingCount { get => _pendingCount; set { if (Set(ref _pendingCount, value)) OnPropertyChanged(nameof(ApplyHint)); } }

    private bool _applyToFutureUsers;
    /// <summary>When on, the reversible HKCU tweaks being applied are also written into the Default profile template.</summary>
    public bool ApplyToFutureUsers { get => _applyToFutureUsers; set => Set(ref _applyToFutureUsers, value); }

    /// <summary>True when at least one pending, ticked change is a reversible per-user tweak that can reach new accounts.</summary>
    public bool FutureUsersApplies => AllTweaks.Any(t => t.IsDirty && t.IsSelected && FutureUsers.IsEligible(t.Tweak));

    public string ApplyHint => CurrentPage is StartupViewModel
        ? (_startup.PendingCount == 0
            ? Loc.T("Toggle the apps you want, then click Apply Changes.")
            : _startup.PendingCount == 1
            ? Loc.T("1 startup change pending — one backup is made when you apply.")
            : Loc.F("{0} startup changes pending — one backup is made when you apply.", _startup.PendingCount))
        : PendingCount == 0
            ? Loc.T("Changes will be applied after clicking Apply Changes.")
            // An app removal among them has no backup to fall back on, so the hint stops promising one for it.
            : AllTweaks.Any(t => t.IsDirty && t.IsSelected && !t.FullyReversible)
            ? (PendingCount == 1
                ? Loc.T("1 pending change — an app removal, which cannot be undone.")
                : Loc.F("{0} pending changes — app removals among them cannot be undone.", PendingCount))
            : PendingCount == 1
            ? Loc.T("1 pending change — a backup is created before applying.")
            : Loc.F("{0} pending changes — a backup is created before applying.", PendingCount);

    private PresetLevel? _activePreset;
    public PresetLevel? ActivePreset { get => _activePreset; set => Set(ref _activePreset, value); }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand SelectPresetCommand { get; }
    public RelayCommand ExportConfigCommand { get; }
    public RelayCommand ImportConfigCommand { get; }

    private void UpdatePendingCount()
    {
        // The Apply bar counts the pending changes of the page you are on: startup toggles on the Startup
        // page, dirty tweaks everywhere else. The sidebar badges always count that category's tweaks.
        PendingCount = CurrentPage is StartupViewModel ? _startup.PendingCount : AllTweaks.Count(t => t.IsDirty);
        OnPropertyChanged(nameof(ApplyHint));
        OnPropertyChanged(nameof(FutureUsersApplies));
        foreach (var item in NavItems)
            if (item.Page is CategoryPageViewModel page)
                item.PendingCount = page.Tweaks.Count(t => t.IsDirty);
    }

    /// <summary>Startup toggles apply immediately, so the dashboard's backup count moves too.</summary>
    internal void RefreshBackupCount() => _dashboard.BackupCount = _backupManager.ListSessions().Count;

    // ---------------------------------------------------------------- scan

    public async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = Loc.T("Scanning system state…");
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
            string systemWarnings = guards.Count == 1 ? Loc.T("1 system warning") : Loc.F("{0} system warnings", guards.Count);
            StatusText = ctx.Warnings.Count == 0 && guards.Count > 0
                // Kept short: this text shares the status bar with the apply hint.
                ? Loc.F("Scan complete — {0}, shown before applying.", systemWarnings)
                : ctx.Warnings.Count == 0
                ? Loc.F("Scan complete. {0} of {1} tweaks already optimized.", _dashboard.OptimizedCount, _dashboard.TotalCount)
                // Name what could not be checked. "Some states are unknown" told the user
                // nothing, and an undetected tweak used to look exactly like an optimized one.
                : Loc.F("Scan incomplete — {0}. {1} tweak(s) could not be checked.", string.Join("; ", ctx.Warnings), undetected)
                  // An incomplete scan must not hide the system warnings it did find.
                  + (guards.Count > 0 ? " " + systemWarnings + "." : "");
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
            // The catalog keeps every removal Manual; this second check keeps a preset from ticking one even if a
            // removal were ever given a preset level by mistake.
            bool inPreset = tweak.Preset != PresetLevel.Manual && tweak.Preset <= level && tweak.FullyReversible;
            // a preset switches its tweaks on but never reverts something already optimized
            tweak.IsSelected = inPreset || tweak.IsOptimized;
        }

        string preset = level switch
        {
            PresetLevel.Safe => Loc.T("Safe"),
            PresetLevel.Balanced => Loc.T("Balanced"),
            PresetLevel.Aggressive => Loc.T("Aggressive"),
            _ => level.ToString(),
        };
        StatusText = keptManual == 0
            ? Loc.F("{0} preset selected — review and click Apply Changes.", preset)
            : keptManual == 1
            ? Loc.F("{0} preset selected, keeping 1 manual selection — review and click Apply Changes.", preset)
            : Loc.F("{0} preset selected, keeping {1} manual selections — review and click Apply Changes.", preset, keptManual);
    }

    // ---------------------------------------------------------------- configuration files

    private void ExportConfigToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Export WinPure configuration"),
            Filter = Loc.T("WinPure configuration (*.json)|*.json"),
            FileName = $"winpure-config-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var ids = AllTweaks.Where(t => t.IsSelected).Select(t => t.Tweak.Id).ToList();
            string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
            File.WriteAllText(dialog.FileName, ConfigFile.Serialize(ids, version));
            StatusText = ids.Count == 1
                ? Loc.T("Configuration exported — 1 tweak switched on.")
                : Loc.F("Configuration exported — {0} tweaks switched on.", ids.Count);
            LogService.Log($"Configuration exported to {dialog.FileName} ({ids.Count} tweaks).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Loc.F("The configuration could not be saved:\n\n{0}", ex.Message), Loc.T("WinPure — Export configuration"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportConfigFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Import WinPure configuration"),
            Filter = Loc.T("WinPure configuration (*.json)|*.json|All files (*.*)|*.*"),
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            // Checked before reading, so a huge file picked by mistake is never loaded into memory.
            if (new FileInfo(dialog.FileName).Length > ConfigFile.MaxChars * 4L)
                throw new InvalidDataException(Loc.T("This file is too large to be a WinPure configuration."));
            ImportConfig(File.ReadAllText(dialog.FileName));
            LogService.Log($"Configuration imported from {dialog.FileName}: {StatusText}");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, Loc.T("WinPure — Import configuration"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Ticks the tweaks a configuration file lists. It never unticks anything — so an import can never
    /// schedule a revert — and never applies: that stays the user's click, with the usual checks.
    /// It never ticks an app removal either. A configuration file can come from anyone, and nearly every export
    /// lists removals, because an app that is already absent reads as applied. Removals are counted and left for
    /// the user to tick on Remove Apps. Returns how many were ticked, already on, and unknown.
    /// </summary>
    internal (int Ticked, int AlreadyOn, int Unknown) ImportConfig(string json)
    {
        var parsed = ConfigFile.Parse(json, AllTweaks.Select(t => t.Tweak.Id));
        var byId = AllTweaks.ToDictionary(t => t.Tweak.Id, StringComparer.Ordinal);

        int ticked = 0, alreadyOn = 0, removalsLeft = 0;
        foreach (var id in parsed.KnownIds)
        {
            var tweak = byId[id];
            if (tweak.IsSelected) { alreadyOn++; continue; }
            if (!tweak.FullyReversible) { removalsLeft++; continue; }
            tweak.IsSelected = true;
            ticked++;
        }
        ActivePreset = null;

        // Kept short: the status bar shares its row with the apply hint.
        StatusText = Loc.F("Imported: {0} ticked", ticked)
            + (alreadyOn > 0 ? Loc.F(", {0} already on", alreadyOn) : "")
            + (removalsLeft == 1 ? Loc.T(", 1 removal left unticked")
               : removalsLeft > 1 ? Loc.F(", {0} removals left unticked", removalsLeft) : "")
            + (parsed.UnknownIds.Count > 0 ? Loc.F(", {0} unknown skipped", parsed.UnknownIds.Count) : "")
            + Loc.T(" — review, then Apply Changes.");
        return (ticked, alreadyOn, parsed.UnknownIds.Count);
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
            Loc.F("Before you {0}, WinPure found:\n\n{1}\n\nContinue anyway?", what, SystemGuards.Describe(relevant)),
            Loc.T("WinPure — Check before continuing"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        bool go = answer == MessageBoxResult.Yes;
        LogService.Log($"Guards shown before '{what}' ({string.Join(", ", relevant.Select(g => g.Id))}): user chose {(go ? "to continue" : "to stop")}");
        return go;
    }

    // ---------------------------------------------------------------- apply

    public async Task ApplyChangesAsync()
    {
        // The Startup page batches its own toggles into one backup; everything else applies dirty tweaks.
        if (CurrentPage is StartupViewModel startupPage)
        {
            await ApplyStartupChangesAsync(startupPage);
            return;
        }

        var changes = AllTweaks
            .Where(t => t.IsDirty)
            .Select(t => (t.Tweak, apply: t.IsSelected))
            .ToList();
        if (changes.Count == 0) return;
        if (!ConfirmDespiteGuards(Loc.T("apply these changes"))) return;

        var irreversible = changes.Where(c => c.apply && !c.Tweak.FullyReversible).ToList();
        if (irreversible.Count > 0)
        {
            var names = string.Join("\n  • ", irreversible.Select(c => Loc.T(c.Tweak.Name)));
            var answer = MessageBox.Show(
                Loc.F("These changes uninstall apps, and WinPure cannot undo them. To get an app back you would reinstall it yourself, from the Microsoft Store (OneDrive from microsoft.com):\n\n  • {0}\n\nRemove them?", names),
                Loc.T("WinPure — Confirm app removal"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        bool futureUsers = ApplyToFutureUsers && FutureUsers.WritesFor(changes).Count > 0;
        if (futureUsers)
        {
            var answer = MessageBox.Show(
                Loc.T("These per-user settings will also be written into the Default profile, so accounts created later start with them. This changes C:\\Users\\Default and is undone from Restore. Continue?"),
                Loc.T("WinPure — Apply to future users"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes) return;
        }

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusText = msg);
        try
        {
            var results = await Task.Run(() => _engine.ApplyChanges(changes, progress, futureUsers));
            if (results.Any(r => r.Success && r.Tweak.NotifiesThemeChange))
                NativeMethods.BroadcastThemeChange();
            if (results.Any(r => r.Success && r.Tweak.NotifiesMouseChange))
                NativeMethods.ApplyMouseSettings();
            int failed = results.Count(r => !r.Success);
            bool needsExplorer = results.Any(r => r.Success && r.Tweak.RequiresExplorerRestart);
            bool needsReboot = results.Any(r => r.Success && r.Tweak.RequiresRestart);

            StatusText = failed == 0
                ? (results.Count == 1 ? Loc.T("Done — 1 change applied.") : Loc.F("Done — {0} changes applied.", results.Count))
                : failed == 1
                ? Loc.T("Finished with 1 error — see the log in %AppData%\\WinPure\\Logs.")
                : Loc.F("Finished with {0} errors — see the log in %AppData%\\WinPure\\Logs.", failed);

            if (needsExplorer)
            {
                var answer = MessageBox.Show(
                    Loc.T("Some changes need File Explorer to restart to take effect.\nRestart Explorer now?"),
                    "WinPure", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                    await Task.Run(() => PowerShellRunner.Run("Stop-Process -Name explorer -Force"));
            }
            else if (needsReboot)
            {
                MessageBox.Show(Loc.T("Some changes will take full effect after a reboot."), "WinPure",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            IsBusy = false;
        }
        await ScanAsync();
    }

    /// <summary>
    /// Applies all the pending Startup toggles at once — one backup for the whole batch, not one per switch.
    /// Asks the guards once (every entry lives under the signed-in user's profile), then rescans.
    /// </summary>
    private async Task ApplyStartupChangesAsync(StartupViewModel startupPage)
    {
        if (startupPage.PendingCount == 0) return;
        if (!ConfirmDespiteGuards(Loc.T("change startup apps"), SystemGuards.ForStartup)) return;

        IsBusy = true;
        StatusText = Loc.T("Applying startup changes…");
        try
        {
            var (applied, failed) = await Task.Run(() => startupPage.ApplyPending());
            RefreshBackupCount();
            StatusText = failed == 0
                ? (applied == 1 ? Loc.T("Done — 1 startup change applied.") : Loc.F("Done — {0} startup changes applied.", applied))
                : Loc.F("{0} applied, {1} could not be changed — see the log in %AppData%\\WinPure\\Logs.", applied, failed);
            if (failed > 0)
                MessageBox.Show(Loc.F("{0} startup change(s) could not be applied. See the log in %AppData%\\WinPure\\Logs.", failed),
                    "WinPure", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (!ConfirmDespiteGuards(Loc.T("restore this backup"), SystemGuards.ForRestore)) return;

        var answer = MessageBox.Show(
            Loc.F("Restore the snapshot from {0}?\nAll {1} captured values will be written back.", vm.Title, vm.Session.Entries.Count),
            Loc.T("WinPure — Restore backup"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = Loc.T("Restoring backup…");
        try
        {
            int failures = await Task.Run(() => _backupManager.RestoreSession(vm.Session));
            // a snapshot may include theme or mouse values — make open apps repaint and re-read the pointer
            NativeMethods.BroadcastThemeChange();
            NativeMethods.ApplyMouseSettings();
            StatusText = failures == 0 ? Loc.T("Backup restored.")
                : failures == 1 ? Loc.T("Backup restored with 1 error (see log).")
                : Loc.F("Backup restored with {0} errors (see log).", failures);
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
        if (!ConfirmDespiteGuards(Loc.T("delete this backup"), SystemGuards.ForRestore)) return;

        var answer = MessageBox.Show(
            Loc.F("Delete the backup from {0}? This cannot be undone.", vm.Title),
            Loc.T("WinPure — Delete backup"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
