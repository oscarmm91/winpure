using Microsoft.Win32;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// Every tweak WinPure knows about. Registry keys/values follow the behaviour of
/// Win11Debloat, Sophia Script and CrapFixer (see docs/PRD.md §7).
/// </summary>
public static class TweakCatalog
{
    public static IReadOnlyList<Tweak> Build()
    {
        var tweaks = new List<Tweak>();
        tweaks.AddRange(Privacy());
        tweaks.AddRange(Apps());
        tweaks.AddRange(Services());
        tweaks.AddRange(Performance());
        tweaks.AddRange(UI());
        tweaks.AddRange(ContextMenu());
        return tweaks;
    }

    private static RegistryValueAction Dword(string key, string name, int apply, int? revert) => new()
    {
        KeyPath = key, ValueName = name, Kind = RegistryValueKind.DWord,
        ApplyValue = apply, DefaultValue = revert,
    };

    private static RegistryValueAction Str(string key, string name, string apply, string? revert) => new()
    {
        KeyPath = key, ValueName = name, Kind = RegistryValueKind.String,
        ApplyValue = apply, DefaultValue = revert,
    };

    /// <summary>Blocks a shell extension CLSID (removes its context-menu entry).</summary>
    private static RegistryValueAction BlockShellExtension(string clsid, string note) => new()
    {
        KeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
        ValueName = clsid, Kind = RegistryValueKind.String,
        ApplyValue = note, DefaultValue = null,
    };

    // ------------------------------------------------------------------ Privacy

    private static IEnumerable<Tweak> Privacy()
    {
        yield return new Tweak
        {
            Id = "privacy-telemetry", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Telemetry",
            Description = "Prevent Windows from sending telemetry data to Microsoft.",
            Help = "Sets AllowTelemetry to 0 (Security level) via policy so Windows only sends the minimum diagnostic data the edition allows.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null),
                Dword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-diagnostics-data", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Diagnostics Data",
            Description = "Prevent Windows from sending diagnostic data to Microsoft.",
            Help = "Turns off 'tailored experiences with diagnostic data' and the feedback notifications driven by it.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1),
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-diagtrack", Category = TweakCategory.Privacy, Preset = PresetLevel.Aggressive,
            Name = "Disable DiagTrack Service",
            Description = "Stop and disable the Connected User Experiences and Telemetry service.",
            Help = "DiagTrack is the main telemetry collector (Unified Telemetry Client). Disabling it stops most background data collection.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new ServiceAction { ServiceName = "DiagTrack", DefaultStartMode = 2 },
            },
        };

        yield return new Tweak
        {
            Id = "privacy-telemetry-firewall", Category = TweakCategory.Privacy, Preset = PresetLevel.Aggressive,
            Name = "Block Telemetry Client (Firewall)",
            Description = "Block outbound connections of the Unified Telemetry Client.",
            Help = "Adds an outbound Windows Firewall rule that blocks the DiagTrack service from reaching Microsoft telemetry endpoints.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new CommandAction
                {
                    ApplyScript = "New-NetFirewallRule -DisplayName 'WinPure - Block Telemetry Client' -Direction Outbound -Action Block -Service DiagTrack -Profile Any | Out-Null",
                    RevertScript = "Remove-NetFirewallRule -DisplayName 'WinPure - Block Telemetry Client' -ErrorAction SilentlyContinue",
                    Detect = ctx => ctx.Extras.TryGetValue("fwTelemetryBlock", out var v) ? v == "1" : null,
                },
            },
        };

        yield return new Tweak
        {
            Id = "privacy-bing-search", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Bing in Start Menu",
            Description = "Stop Start Menu search from sending queries to Bing.",
            Help = "Sets DisableSearchBoxSuggestions so Start search stays local and stops showing web results.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-suggested-apps", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Silent App Installs",
            Description = "Stop Windows from silently installing suggested apps.",
            Help = "Turns off the Content Delivery Manager flag that lets Windows auto-install sponsored apps in the background.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-activity-history", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Activity History",
            Description = "Stop Windows from collecting and storing your activity history.",
            Help = "Disables the activity feed and stops publishing/uploading user activities (Timeline data).",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, null),
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-location", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Disable Location Tracking",
            Description = "Prevent Windows from tracking your location and location history.",
            Help = "Applies the LocationAndSensors policy that turns location access off system-wide.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-app-launch-tracking", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable App Launch Tracking",
            Description = "Stop Windows from tracking app launches to improve Start and search.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-advertising-id", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Advertising ID",
            Description = "Prevent apps from using your advertising ID for targeted ads.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-feedback", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Windows Feedback",
            Description = "Set feedback frequency to never and silence Feedback Hub prompts.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-speech", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Disable Online Speech Recognition",
            Description = "Stop Windows from storing your voice data and speech patterns.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-inking", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Disable Inking & Typing Personalization",
            Description = "Stop Windows from collecting handwriting and typing data.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Input\TIPC", "Enabled", 0, 1),
                Dword(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, 0),
                Dword(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, 0),
                Dword(@"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, 1),
                Dword(@"HKCU\Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-consumer-features", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Consumer Features",
            Description = "Stop Windows from auto-installing sponsored games and Store app links.",
            Help = "Sets the DisableWindowsConsumerFeatures policy (from Chris Titus WinUtil). Note: some promo-driven apps like Phone Link suggestions disappear.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-delivery-optimization", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Disable Delivery Optimization",
            Description = "Stop Windows from uploading updates to other PCs using your bandwidth.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-background-apps", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Disable Background Apps",
            Description = "Stop Microsoft Store apps from running in the background.",
            Help = "Sets the global background-access kill switch (from Chris Titus WinUtil) instead of toggling each app one by one.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-compat-telemetry-tasks", Category = TweakCategory.Privacy, Preset = PresetLevel.Aggressive,
            Name = "Disable Compatibility Telemetry Tasks",
            Description = "Disable the Microsoft Compatibility Appraiser scheduled tasks.",
            Help = "These tasks scan installed software and upload compatibility data; they are also a known cause of random CPU/disk spikes.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Application Experience\ProgramDataUpdater" },
            },
        };

        yield return new Tweak
        {
            Id = "privacy-ceip-tasks", Category = TweakCategory.Privacy, Preset = PresetLevel.Aggressive,
            Name = "Disable CEIP & Feedback Tasks",
            Description = "Disable Customer Experience Improvement Program and automatic feedback tasks.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Feedback\Siuf\DmClient" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload" },
            },
        };
    }

    // ------------------------------------------------------------------ Apps / Bloatware

    private static Tweak AppRemoval(string id, PresetLevel preset, string name, string description,
        string icon, params string[] patterns) => new()
    {
        Id = id, Category = TweakCategory.Apps, Preset = preset,
        Name = name, Description = description, Icon = icon,
        FullyReversible = false,
        Help = "Removes the Store package for all users and un-provisions it for new users. To get it back, reinstall it from the Microsoft Store.",
        Actions = new TweakAction[] { new AppxRemoveAction { PackagePatterns = patterns } },
    };

    private static IEnumerable<Tweak> Apps()
    {
        yield return AppRemoval("apps-candycrush", PresetLevel.Balanced, "Remove Candy Crush",
            "Uninstall Candy Crush games preinstalled by Windows.", "", "king.com.CandyCrush", "king.com.");
        yield return AppRemoval("apps-social", PresetLevel.Balanced, "Remove Social Media Apps",
            "Uninstall preinstalled TikTok, Facebook, Twitter/X and Instagram.", "",
            "BytedancePte.Ltd.TikTok", "Facebook", "Twitter", "Instagram");
        yield return AppRemoval("apps-streaming", PresetLevel.Balanced, "Remove Streaming Apps",
            "Uninstall preinstalled Netflix, Disney+, Prime Video and Spotify.", "",
            "Netflix", "Disney", "AmazonVideo", "PrimeVideo", "SpotifyAB.SpotifyMusic");
        yield return AppRemoval("apps-skype", PresetLevel.Balanced, "Remove Skype",
            "Uninstall the preinstalled Skype app.", "", "Microsoft.SkypeApp");
        yield return AppRemoval("apps-clipchamp", PresetLevel.Balanced, "Remove Clipchamp",
            "Uninstall the Clipchamp video editor.", "", "Clipchamp.Clipchamp");
        yield return AppRemoval("apps-3d", PresetLevel.Balanced, "Remove Paint 3D & 3D Viewer",
            "Uninstall the legacy 3D apps.", "", "Microsoft.Microsoft3DViewer", "Microsoft.MSPaint");
        yield return AppRemoval("apps-todo", PresetLevel.Manual, "Remove Microsoft To Do",
            "Uninstall the Microsoft To Do app.", "", "Microsoft.Todos");
        yield return AppRemoval("apps-zune", PresetLevel.Balanced, "Remove Groove Music & Movies + TV",
            "Uninstall the legacy Zune media apps.", "", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo");
        yield return AppRemoval("apps-solitaire", PresetLevel.Balanced, "Remove Solitaire Collection",
            "Uninstall Microsoft Solitaire Collection.", "", "Microsoft.MicrosoftSolitaireCollection");
        yield return AppRemoval("apps-wallet", PresetLevel.Balanced, "Remove Wallet",
            "Uninstall the Microsoft Wallet app.", "", "Microsoft.Wallet");
        yield return AppRemoval("apps-whiteboard", PresetLevel.Manual, "Remove Whiteboard",
            "Uninstall the Microsoft Whiteboard app.", "", "Microsoft.Whiteboard");
        yield return AppRemoval("apps-phonelink", PresetLevel.Balanced, "Remove Phone Link",
            "Uninstall the YourPhone / Phone Link app.", "", "Microsoft.YourPhone");
        yield return AppRemoval("apps-devhome", PresetLevel.Balanced, "Remove Dev Home",
            "Uninstall the Dev Home app.", "", "Microsoft.DevHome");
        yield return AppRemoval("apps-gethelp", PresetLevel.Balanced, "Remove Get Help & Tips",
            "Uninstall the Get Help and Get Started apps.", "", "Microsoft.GetHelp", "Microsoft.Getstarted");

        yield return new Tweak
        {
            Id = "apps-xbox", Category = TweakCategory.Apps, Preset = PresetLevel.Aggressive,
            Name = "Remove Xbox Apps & Overlay",
            Description = "Uninstall Xbox apps, Game Bar overlay and disable Game DVR.",
            Help = "Removes the Xbox app family and turns off Game DVR background recording. Do not apply if you play Game Pass titles.",
            Icon = "", FullyReversible = false,
            Actions = new TweakAction[]
            {
                new AppxRemoveAction { PackagePatterns = new[]
                {
                    "Microsoft.XboxApp", "Microsoft.GamingApp", "Microsoft.XboxGamingOverlay",
                    "Microsoft.XboxGameOverlay", "Microsoft.Xbox.TCUI", "Microsoft.XboxSpeechToTextOverlay",
                }},
                Dword(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, null),
            },
        };

        yield return new Tweak
        {
            Id = "apps-copilot", Category = TweakCategory.Apps, Preset = PresetLevel.Aggressive,
            Name = "Disable Copilot / Windows AI",
            Description = "Turn off Windows Copilot and remove its taskbar button.",
            Help = "Applies the TurnOffWindowsCopilot policy for the machine and current user and hides the taskbar button.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "apps-windows-ai", Category = TweakCategory.Apps, Preset = PresetLevel.Aggressive,
            Name = "Disable Windows AI & Recall",
            Description = "Turn off Recall snapshots, Notepad AI and hide AI components from Settings.",
            Help = "Applies the WindowsAI/Recall policies popularized by Chris Titus WinUtil: DisableAIDataAnalysis stops Recall screen captures, Notepad AI features are disabled and the AI components page is hidden.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                Dword(@"HKLM\SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", 1, null),
                Str(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility", "hide:aicomponents", null),
            },
        };

        yield return new Tweak
        {
            Id = "apps-onedrive", Category = TweakCategory.Apps, Preset = PresetLevel.Aggressive,
            Name = "Remove OneDrive",
            Description = "Uninstall OneDrive and block file sync via policy.",
            Help = "Runs the OneDrive uninstaller and sets the DisableFileSyncNGSC policy. Your files stay on disk; reinstall from microsoft.com to undo.",
            Icon = "", FullyReversible = false, RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new CommandAction
                {
                    ApplyScript = """
                        $ErrorActionPreference = 'SilentlyContinue'
                        Stop-Process -Name OneDrive -Force
                        $setup = "$env:SystemRoot\System32\OneDriveSetup.exe"
                        if (-not (Test-Path $setup)) { $setup = "$env:SystemRoot\SysWOW64\OneDriveSetup.exe" }
                        if (Test-Path $setup) { Start-Process $setup -ArgumentList '/uninstall' -Wait }
                        winget uninstall --id Microsoft.OneDrive --silent --accept-source-agreements 2>$null | Out-Null
                        exit 0
                        """,
                    RevertScript = "Write-Output 'Reinstall OneDrive from https://www.microsoft.com/microsoft-365/onedrive/download'; exit 0",
                    Detect = ctx => ctx.Extras.TryGetValue("onedrive", out var v) ? v == "0" : null,
                    TimeoutMs = 300_000,
                },
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1, null),
            },
        };
    }

    // ------------------------------------------------------------------ Services

    private static Tweak Service(string id, PresetLevel preset, string name, string description,
        string serviceName, int defaultStartMode, string help = "") => new()
    {
        Id = id, Category = TweakCategory.Services, Preset = preset,
        Name = name, Description = description, Help = help, Icon = "",
        Actions = new TweakAction[]
        {
            new ServiceAction { ServiceName = serviceName, DefaultStartMode = defaultStartMode },
        },
    };

    private static IEnumerable<Tweak> Services()
    {
        yield return Service("svc-sysmain", PresetLevel.Manual, "Disable SysMain (SuperFetch)",
            "Stop the prefetching service. Recommended only on SSDs.", "SysMain", 2,
            "SysMain preloads frequently used apps into RAM. On SSDs the benefit is negligible and it can cause disk/CPU spikes.");
        yield return Service("svc-search", PresetLevel.Manual, "Disable Search Indexing",
            "Stop the Windows Search indexer. File search becomes slower.", "WSearch", 2,
            "Disabling WSearch frees background I/O but Start Menu and Explorer search will scan on demand.");
        yield return Service("svc-spooler", PresetLevel.Manual, "Disable Print Spooler",
            "Disable printing support. Only if you never print.", "Spooler", 2,
            "PrintNightmare-class vulnerabilities live in the spooler; disable it if no printer is ever used.");
        yield return Service("svc-remote-registry", PresetLevel.Balanced, "Disable Remote Registry",
            "Prevent remote computers from modifying your registry.", "RemoteRegistry", 4);
        yield return Service("svc-wer", PresetLevel.Balanced, "Disable Windows Error Reporting",
            "Stop Windows from uploading crash reports to Microsoft.", "WerSvc", 3);
        yield return Service("svc-cdp", PresetLevel.Balanced, "Disable Connected Devices Platform",
            "Disable the cross-device sync service (CDPSvc).", "CDPSvc", 2,
            "Used by 'shared experiences' / device handoff. Safe to disable if you do not link devices.");
        yield return Service("svc-geolocation", PresetLevel.Balanced, "Disable Geolocation Service",
            "Stop the location service (lfsvc).", "lfsvc", 3);
        yield return Service("svc-fax", PresetLevel.Balanced, "Disable Fax Service",
            "Disable the legacy fax service.", "Fax", 3);
        yield return Service("svc-bluetooth", PresetLevel.Manual, "Disable Bluetooth Support",
            "Disable Bluetooth services. Only if you never use Bluetooth.", "bthserv", 3);
    }

    // ------------------------------------------------------------------ Performance

    private static IEnumerable<Tweak> Performance()
    {
        yield return new Tweak
        {
            Id = "perf-app-timeouts", Category = TweakCategory.Performance, Preset = PresetLevel.Safe,
            Name = "Faster App Timeouts",
            Description = "Reduce the wait before unresponsive apps are killed.",
            Help = "WaitToKillAppTimeout 5000→2000 ms and HungAppTimeout 5000→1000 ms: faster shutdown and snappier 'Not responding' handling.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Str(@"HKCU\Control Panel\Desktop", "WaitToKillAppTimeout", "2000", "5000"),
                Str(@"HKCU\Control Panel\Desktop", "HungAppTimeout", "1000", "5000"),
            },
        };

        yield return new Tweak
        {
            Id = "perf-shutdown-timeout", Category = TweakCategory.Performance, Preset = PresetLevel.Balanced,
            Name = "Faster System Shutdown",
            Description = "Reduce the time Windows waits for services on shutdown.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Str(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000"),
            },
        };

        yield return new Tweak
        {
            Id = "perf-animations", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Window Animations",
            Description = "Turn off minimize/maximize animations. For low-end hardware.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Str(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1"),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "perf-priority-programs", Category = TweakCategory.Performance, Preset = PresetLevel.Balanced,
            Name = "Prioritize Foreground Apps",
            Description = "Give programs priority over background services.",
            Help = "Sets Win32PrioritySeparation to 38 (0x26): short, variable quanta favouring the foreground application.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, 2),
            },
        };

        yield return new Tweak
        {
            Id = "perf-hibernation", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Hibernation",
            Description = "Turn off hibernation and delete hiberfil.sys to free disk space.",
            Help = "Frees several GB (hiberfil.sys) but disables Fast Startup and hibernate. Revert turns it back on.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new CommandAction
                {
                    ApplyScript = "powercfg /hibernate off",
                    RevertScript = "powercfg /hibernate on",
                    Detect = ctx => ctx.Extras.TryGetValue("hibernate", out var v) ? v == "0" : null,
                },
            },
        };

        yield return new Tweak
        {
            Id = "perf-power-plan", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "High Performance Power Plan",
            Description = "Switch the active power plan to High Performance.",
            Help = "Best for desktops. On laptops this reduces battery life; the Balanced plan is restored on revert.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new CommandAction
                {
                    ApplyScript = "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                    RevertScript = "powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e",
                    Detect = ctx => ctx.Extras.TryGetValue("powerplan", out var v)
                        ? v.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase)
                        : null,
                },
            },
        };

        yield return new Tweak
        {
            Id = "perf-fullscreen-opt", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Fullscreen Optimizations",
            Description = "Use true exclusive fullscreen in games for lower input latency.",
            Help = "From Chris Titus WinUtil. Note: disables color management in exclusive fullscreen.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0),
            },
        };

        yield return new Tweak
        {
            Id = "perf-mouse-accel", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Mouse Acceleration",
            Description = "Make cursor movement 1:1 with physical mouse movement.",
            Help = "Sets MouseSpeed and both thresholds to 0 — preferred for gaming and precise work.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Str(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", "1"),
                Str(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", "6"),
                Str(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", "10"),
            },
        };

        yield return new Tweak
        {
            Id = "perf-long-paths", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Enable Long Paths",
            Description = "Allow file paths longer than 260 characters.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, 0),
            },
        };

        yield return new Tweak
        {
            Id = "perf-remote-desktop", Category = TweakCategory.Performance, Preset = PresetLevel.Balanced,
            Name = "Disable Remote Desktop",
            Description = "Block inbound Remote Desktop connections if you don't use them.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1, 1),
            },
        };
    }

    // ------------------------------------------------------------------ UI & Personalization

    private static IEnumerable<Tweak> UI()
    {
        yield return new Tweak
        {
            Id = "ui-dark-mode", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Enable Dark Mode",
            Description = "Use dark mode for Windows and apps by default.",
            NotifiesThemeChange = true,
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0, 1),
                Dword(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-snap-flyout", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Disable Snap Assist Flyout",
            Description = "Hide the snap layout flyout when hovering the maximize button.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-start-suggestions", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Hide Suggestions in Start",
            Description = "Remove suggested apps and recommendations from the Start Menu.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0, 1),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0, 1),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-most-used", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Hide Most Used Apps in Start",
            Description = "Remove the 'Most used' apps list from the Start Menu.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", "ShowOrHideMostUsedApps", 2, null),
            },
        };

        yield return new Tweak
        {
            Id = "ui-recently-added", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Hide Recently Added Apps in Start",
            Description = "Remove the 'Recently added' apps list from the Start Menu.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", "HideRecentlyAddedApps", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "ui-new-app-alert", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Hide 'New App Installed' Badge",
            Description = "Stop highlighting newly installed apps in the Start Menu.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoNewAppAlert", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "ui-file-extensions", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Show File Extensions",
            Description = "Always show file extensions in File Explorer.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-hidden-files", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Show Hidden Files",
            Description = "Show hidden files and folders in File Explorer.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1, 2),
            },
        };

        yield return new Tweak
        {
            Id = "ui-taskbar-widgets", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Remove Widgets Button",
            Description = "Remove the Widgets button from the taskbar.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-taskbar-taskview", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Remove Task View Button",
            Description = "Remove the Task View button from the taskbar.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-taskbar-chat", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Remove Chat/Teams Button",
            Description = "Remove the Chat (Microsoft Teams) button from the taskbar.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-start-left", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Align Taskbar Left",
            Description = "Move the Start Menu and taskbar icons to the left.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-end-task", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "End Task on Taskbar Right-Click",
            Description = "Add an 'End task' option when right-clicking taskbar apps.",
            Help = "Enables the hidden taskbar developer setting (from Chris Titus WinUtil) — kill hung apps without opening Task Manager.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "ui-aero-shake", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Disable Aero Shake",
            Description = "Stop minimizing other windows when you shake a title bar.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", 1, 0),
            },
        };
    }

    // ------------------------------------------------------------------ Context Menu

    private static IEnumerable<Tweak> ContextMenu()
    {
        yield return new Tweak
        {
            Id = "ctx-classic-menu", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Safe,
            Name = "Restore Classic Context Menu",
            Description = "Bring back the full Windows 10 right-click menu.",
            Help = "Registers an empty InprocServer32 for CLSID {86ca1aa0-34aa-4e8b-a509-50c905bae2a2}, which disables the Windows 11 'Show more options' wrapper.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                new RegistryKeyAction
                {
                    KeyPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
                    DeleteOnApply = false, KeyDefaultValue = "",
                },
            },
        };

        yield return new Tweak
        {
            Id = "ctx-clipchamp", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Safe,
            Name = "Remove 'Edit with Clipchamp'",
            Description = "Remove the Clipchamp entry from the context menu.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                BlockShellExtension("{8AB635F8-9A67-4698-AB99-784AD929F3B4}", "WinPure: remove Clipchamp context entry"),
            },
        };

        yield return new Tweak
        {
            Id = "ctx-notepad", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Manual,
            Name = "Remove 'Edit with Notepad'",
            Description = "Remove the Notepad entry from the context menu.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                BlockShellExtension("{CA6CC9F1-867A-481E-951E-A28C5E4F01EA}", "WinPure: remove Notepad context entry"),
            },
        };

        yield return new Tweak
        {
            Id = "ctx-photos", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Manual,
            Name = "Remove 'Edit with Photos'",
            Description = "Remove the Photos entry from the context menu.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                BlockShellExtension("{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}", "WinPure: remove Photos context entry"),
            },
        };

        yield return new Tweak
        {
            Id = "ctx-ask-copilot", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Safe,
            Name = "Remove 'Ask Copilot'",
            Description = "Remove the Ask Copilot entry from the context menu.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                BlockShellExtension("{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}", "WinPure: remove Copilot context entry"),
            },
        };

        yield return new Tweak
        {
            Id = "ctx-share", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Manual,
            Name = "Remove 'Share'",
            Description = "Remove the Share entry from the file context menu.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                new RegistryKeyAction
                {
                    KeyPath = @"HKCR\*\shellex\ContextMenuHandlers\ModernSharing",
                    DeleteOnApply = true,
                    KeyDefaultValue = "{e2bf9676-5f8f-435c-97eb-11607a5bedf7}",
                },
            },
        };

        yield return new Tweak
        {
            Id = "ctx-give-access", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Safe,
            Name = "Remove 'Give access to'",
            Description = "Remove the network sharing entry from context menus.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                new RegistryKeyAction { KeyPath = @"HKCR\*\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
                new RegistryKeyAction { KeyPath = @"HKCR\Directory\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
                new RegistryKeyAction { KeyPath = @"HKCR\Directory\Background\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
                new RegistryKeyAction { KeyPath = @"HKCR\Drive\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
            },
        };
    }
}
