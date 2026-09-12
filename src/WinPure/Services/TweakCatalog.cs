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
        tweaks.AddRange(Features());
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
            Help = "Sets AllowTelemetry to 0 (Security level) via policy. Only Enterprise, Education and IoT honour level 0; on Home and Pro Windows clamps it to 1 (Required diagnostic data), which is the lowest those editions allow.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // The first path is the one Windows declares in DataCollection.admx. The second
                // is the Windows 10 location, kept for machines upgraded from it — the policy
                // audit in tests/ flags it on purpose, and this is the reason it is expected.
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
            Icon = "",
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
            Icon = "",
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
            Help = "Global background-access kill switch (from Chris Titus WinUtil). Store apps stop working in the background: Mail and Calendar will not fetch or notify until you open them, and UWP push notifications, live tiles and Photos sync stop.",
            Icon = "",
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
                // On 24H2/25H2 the two tasks above are gone and this one does the work
                // (verified on 2026-09-11: it is the only Appraiser task present, and Ready).
                // The old paths stay for machines upgraded from Windows 10 / 23H2.
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp" },
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
        yield return new Tweak
        {
            Id = "privacy-search-history", Category = TweakCategory.Privacy, Preset = PresetLevel.Safe,
            Name = "Disable Search History",
            Description = "Stop Windows search from keeping a history of what you searched on this device.",
            Help = "Turns off 'Search history on this device' in Settings. Past searches stop being suggested.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-language-list", Category = TweakCategory.Privacy, Preset = PresetLevel.Balanced,
            Name = "Don't Share Your Language List With Websites",
            Description = "Stop websites from reading the list of languages installed in Windows.",
            Help = "Your language list is a quiet fingerprinting signal. Sites that relied on it may pick their language from your browser settings instead.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "privacy-diagnostic-tasks", Category = TweakCategory.Privacy, Preset = PresetLevel.Aggressive,
            Name = "Disable Diagnostic Data Tasks",
            Description = "Disable scheduled tasks that collect disk and Autochk diagnostics, scan startup apps and update offline maps.",
            Help = "Offline maps stop updating on their own. The warning that a disk is about to fail comes from a different task (DiskDiagnosticResolver), which this leaves alone. MareBackup, which Sophia Script also disables, is deliberately left alone: it feeds the app list Windows Backup uses to restore your apps on a new PC.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // All five confirmed present on 25H2 build 26200 with these exact paths. They are
                // also in ScanContext.WatchedTasks: a task missing from that list always reads as
                // already applied.
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Application Experience\StartupAppTask" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Autochk\Proxy" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Maps\MapsToastTask" },
                new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Maps\MapsUpdateTask" },
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
            "Uninstall the Dev Home app.", "", "Microsoft.Windows.DevHome");
        yield return AppRemoval("apps-gethelp", PresetLevel.Balanced, "Remove Get Help & Tips",
            "Uninstall the Get Help and Get Started apps.", "", "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.StartExperiencesApp");

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
            Help = "Applies the TurnOffWindowsCopilot policy for the current user and hides the taskbar button. The policy is declared class=\"User\" in Windows' own WindowsCopilot.admx, so the machine-wide copy other debloaters also write is simply never read.",
            Icon = "", RequiresExplorerRestart = true,
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "apps-windows-ai", Category = TweakCategory.Apps, Preset = PresetLevel.Aggressive,
            Name = "Disable Windows AI & Recall",
            Description = "Turn off Recall snapshots, Notepad AI and hide AI components from Settings.",
            Help = "DisableAIDataAnalysis stops Recall from saving screen captures, AllowRecallEnablement keeps it from being switched back on, Notepad AI is disabled and the AI components page is hidden. Snapshots Recall already saved are only deleted on the next restart — Microsoft's own policy description says so.",
            RequiresRestart = true,
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                // Verified against this machine's own WindowsCopilot.admx (build 26200), which
                // is what actually defines the policy — not a reference repo. AllowRecallEnablement
                // is declared with enabled=1/disabled=0, so 0 is what blocks Recall.
                // ("TurnOffSavingSnapshots", which several debloaters still ship, is NOT declared
                // in this build's ADMX at all — dropped rather than written and hoped for.)
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, null),
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallExport", 0, null),
                Dword(@"HKLM\SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", 1, null),
                Str(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility", "hide:aicomponents", null),
            },
        };

        yield return new Tweak
        {
            Id = "apps-click-to-do", Category = TweakCategory.Apps, Preset = PresetLevel.Balanced,
            Name = "Disable Click to Do",
            Description = "Turn off the AI overlay that appears when you select text or an image.",
            Help = "Click to Do sends what you select to on-device AI to offer actions. Policy verified against this machine's WindowsCopilot.admx, where it is declared with enabled=1.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, null),
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "apps-paint-ai", Category = TweakCategory.Apps, Preset = PresetLevel.Manual,
            Name = "Disable Paint AI Features",
            Description = "Turn off Cocreator, Image Creator and generative fill in Paint.",
            Help = "Removes the AI buttons from Paint's toolbar. The three policies come from this machine's own WindowsCopilot.admx, which puts them under CurrentVersion\\Policies\\Paint (machine scope).",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableCocreator", 1, null),
                Dword(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableImageCreator", 1, null),
                Dword(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableGenerativeFill", 1, null),
            },
        };

        // "RoyalRevolt" without a space: Win11Debloat's Apps.json lists the AppId as "Royal Revolt",
        // which can never match a package name. CrapFixer has the real one, flaregamesGmbH.RoyalRevolt2,
        // and Win-Debloat-Tools matches *RoyalRevolt* — two sources agreeing.
        yield return AppRemoval("apps-casual-games", PresetLevel.Balanced, "Remove Preinstalled Casual Games",
            "Uninstall third-party games some PCs ship with: Asphalt, Caesars Slots, Cooking Fever, Disney Magic Kingdoms, FarmVille, Hidden City, March of Empires, NYT Crossword and Royal Revolt.",
            "",
            "Asphalt8Airborne", "CaesarsSlotsFreeCasino", "COOKINGFEVER", "DisneyMagicKingdoms",
            "FarmVille2CountryEscape", "HiddenCity", "MarchofEmpires", "NYTCrossword", "RoyalRevolt");

        yield return AppRemoval("apps-aihub", PresetLevel.Balanced, "Remove AI Hub",
            "Uninstall the AI Hub app that Copilot+ PCs ship with.", "", "Microsoft.Windows.AIHub");

        yield return new Tweak
        {
            Id = "apps-gamebar-capture", Category = TweakCategory.Apps, Preset = PresetLevel.Manual,
            Name = "Disable Game Bar Capture",
            Description = "Turn off Game Bar's clips, screenshots and recording, and its startup tips, without uninstalling anything.",
            Help = "An alternative to removing the Xbox apps, so it is in no preset: Aggressive removes them outright. Game Bar stays installed. GameDVR_Enabled, which Sophia Script writes alongside AppCaptureEnabled, is deliberately left to Remove Xbox Apps — two tweaks writing one value would overwrite each other's backups.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, null),
                Dword(@"HKCU\Software\Microsoft\GameBar", "ShowStartupPanel", 0, null),
            },
        };

        yield return new Tweak
        {
            // Manual, not Aggressive: Remove Xbox Apps tells Game Pass players to leave it unticked, and
            // this one ticked on its own breaks the Xbox button while Game Bar is still installed.
            Id = "apps-gamebar-integration", Category = TweakCategory.Apps, Preset = PresetLevel.Manual,
            Name = "Disable Game Bar Integration",
            Description = "Stop games and controllers from opening Game Bar — which also silences the \"You'll need a new app to open this ms-gamebar link\" popup once the Xbox apps are removed.",
            Help = "For PCs where the Xbox apps were removed, so it is in no preset. If Game Bar is still installed — say you kept it for Game Pass — the controller's Xbox button and games stop opening it. Reinstalling Game Bar is not expected to undo this; Undo here does, leaving two empty registry keys behind. The similar prompt for ms-gamingoverlay links is a different case: Sophia Script silences it with the values Disable Game Bar Capture and Remove Xbox Apps write.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // Win11Debloat's Disable_Game_Bar_Integration.reg, with two changes. HKCU, not HKCR:
                // measured on 25H2 the ms-gamebar class exists only under the user's hive, and HKCR is a
                // merged view whose write target depends on where the key already lives. And no
                // ms-gamebarservices: that class is not registered on 25H2 even with Game Bar installed
                // (measured), so writing it would register a new protocol rather than silence one.
                Dword(@"HKCU\SOFTWARE\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, null),
                Str(@"HKCU\SOFTWARE\Classes\ms-gamebar", "NoOpenWith", "", null),
                new RegistryKeyAction
                {
                    KeyPath = @"HKCU\SOFTWARE\Classes\ms-gamebar\shell\open\command",
                    DeleteOnApply = false,
                    KeyDefaultValue = "%SystemRoot%/System32/systray.exe",
                },
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
        // Manual, not Balanced: Sophia Script dropped this same tweak because disabling CDPSvc
        // stops Night Light from starting — a symptom nobody would trace back to
        // "Connected Devices Platform".
        yield return Service("svc-cdp", PresetLevel.Manual, "Disable Connected Devices Platform",
            "Disable the cross-device sync service (CDPSvc).", "CDPSvc", 2,
            "Used by 'shared experiences' / device handoff. WARNING: it also breaks Night Light — the blue-light filter will no longer turn on.");
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
            Help = "Frees several GB (hiberfil.sys), but also turns off Fast Startup and the Hibernate option. Undo puts back the setting this PC had before.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new SystemStateAction { Kind = SystemStateKind.Hibernation, AppliedState = "off", DefaultState = "on" },
            },
        };

        yield return new Tweak
        {
            Id = "perf-power-plan", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "High Performance Power Plan",
            Description = "Switch the active power plan to High Performance.",
            Help = "Best for desktops. On laptops this reduces battery life. Undo switches back to the plan that was active before, custom plans included.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new SystemStateAction
                {
                    Kind = SystemStateKind.PowerPlan,
                    AppliedState = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // High performance
                    DefaultState = "381b4222-f694-41f0-9685-ff5bb260df2e", // Balanced — only when there is no backup
                },
            },
        };

        yield return new Tweak
        {
            Id = "perf-reserved-storage", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Reserved Storage",
            Description = "Give back the several GB Windows sets aside so that updates always have room to install.",
            Help = "Without the reserve, a nearly full disk can make an update fail until you free up space yourself. Windows refuses the change while an update is using the reserve; try again after restarting. Undo turns the reserve back on only if this PC had it.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // Read by the scan because it costs a PowerShell call.
                new SystemStateAction
                {
                    Kind = SystemStateKind.ReservedStorage, AppliedState = "Disabled", DefaultState = "Enabled",
                    FromScan = ctx => ctx.Extras.TryGetValue("reservedStorage", out var v) && v.Length > 0 ? v : null,
                },
            },
        };

        yield return new Tweak
        {
            Id = "perf-fullscreen-opt", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Fullscreen Optimizations",
            Description = "Use true exclusive fullscreen in games for lower input latency.",
            Help = "From Chris Titus WinUtil. Note: disables color management in exclusive fullscreen.",
            Icon = "",
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
            Icon = "",
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
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, 0),
            },
        };

        yield return new Tweak
        {
            Id = "perf-remote-desktop", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Disable Remote Desktop",
            Description = "Block inbound Remote Desktop connections if you don't use them.",
            Help = "On Windows 11 Pro this turns off a feature that works: you will no longer be able to connect to this PC with Remote Desktop.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1, 1),
            },
        };
        yield return new Tweak
        {
            Id = "perf-fast-startup", Category = TweakCategory.Performance, Preset = PresetLevel.Balanced,
            Name = "Disable Fast Startup",
            Description = "Make Shut down actually shut Windows down instead of hibernating its kernel.",
            Help = "With Fast Startup, drivers are not reloaded after a shutdown and the system drive is left in a state other operating systems cannot safely write to. Turning it off fixes both; startup takes a few seconds longer. Disable Hibernation also stops Fast Startup from working, even though this setting then still reads as not applied.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "perf-early-updates", Category = TweakCategory.Performance, Preset = PresetLevel.Balanced,
            Name = "Don't Get Updates As Soon As They're Available",
            Description = "Leave the channel that installs optional preview updates before everyone else.",
            Help = "The same switch as 'Get the latest updates as soon as they're available' in Windows Update settings, which is off unless someone turned it on. Security updates keep arriving as usual; only early optional releases stop. On a PC managed by an organization, its Windows Update policy decides instead, and this setting may have no visible effect.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // Off unless someone turns it on: Microsoft's support page only describes switching it
                // on, and the value is absent on this 25H2 machine. So a missing value already is the
                // state this tweak wants. Only one search summary said "off by default" outright.
                new RegistryValueAction
                {
                    KeyPath = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", ValueName = "IsContinuousInnovationOptedIn",
                    Kind = RegistryValueKind.DWord, ApplyValue = 0, AbsentMeansApplied = true,
                },
            },
        };

        yield return new Tweak
        {
            Id = "perf-registry-backup", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Enable Daily Registry Backup",
            Description = "Have Windows copy the registry to RegBack during idle maintenance, as it once did by default.",
            Help = "A safety net if the registry is ever damaged. It uses some disk space — one copy of each registry hive. The RegIdleBackup task that does the copying is already enabled on current Windows; this turns on the setting it checks.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager", "EnablePeriodicBackup", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "perf-ntp-server", Category = TweakCategory.Performance, Preset = PresetLevel.Manual,
            Name = "Sync the Clock With pool.ntp.org",
            Description = "Point Windows' time sync at the public NTP pool instead of time.windows.com.",
            Help = "Useful when the clock keeps drifting. Only the server changes, not how often Windows syncs. On most home PCs the Windows Time service starts fresh for every sync, so the next one uses it; where the service runs all the time (typical on work PCs) it applies after a restart. Written as a registry value on purpose, so undo restores the server you actually had.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // 0x9 like the stock value: 0x8 is client mode, 0x1 keeps SpecialPollInterval (4.5 h
                // measured here). winutil writes 0x8, which silently changes the polling interval too.
                Str(@"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "NtpServer", "pool.ntp.org,0x9", "time.windows.com,0x9"),
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
            Help = "Uses the Dsh policy, which is what current Windows 11 honours. The old TaskbarDa value is still written for Windows 11 builds before 24H2, but newer builds refuse it: the UCPD driver blocks that value for every known executable, and the tweak works without it.",
            Actions = new TweakAction[]
            {
                Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null),
                // Legacy path. Verified on 2026-09-12 on build 26200: Windows rejects this write
                // with "invalid operation" (UCPD). Optional so it cannot fail the whole tweak.
                new RegistryValueAction
                {
                    KeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    ValueName = "TaskbarDa",
                    Kind = RegistryValueKind.DWord,
                    ApplyValue = 0,
                    DefaultValue = 1,
                    Optional = true,
                },
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
            Icon = "",
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
        yield return new Tweak
        {
            Id = "ui-search-highlights", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Disable Search Highlights",
            Description = "Remove the illustrations and trending content from the search box and search home.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-numlock-login", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Turn NumLock On at Startup",
            Description = "Start with the number pad active, on the sign-in screen as well as after you sign in.",
            Help = "Sets both the sign-in screen and your account, which can differ. With Fast Startup on, Windows may bring back the keyboard state from before the last shutdown instead.",
            Icon = "",
            Actions = new TweakAction[]
            {
                // The user key takes "0"/"2" (winutil); the sign-in screen's takes the 0x80000000 form (Sophia).
                Str(@"HKCU\Control Panel\Keyboard", "InitialKeyboardIndicators", "2", "0"),
                Str(@"HKU\.DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", "2147483650", "2147483648"),
            },
        };

        yield return new Tweak
        {
            Id = "ui-spotlight-desktop", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Block Spotlight as Desktop Background",
            Description = "Remove Windows Spotlight — rotating Microsoft images with 'learn more' links — from the background options.",
            Help = "Applied as a per-user policy (declared class=User in CloudContent.admx), so Settings shows that the option is managed. It stops Spotlight from being chosen; a picture or colour background is unaffected.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Policies\Microsoft\Windows\CloudContent", "DisableSpotlightCollectionOnDesktop", 1, null),
            },
        };

        yield return new Tweak
        {
            Id = "ui-update-welcome", Category = TweakCategory.UI, Preset = PresetLevel.Safe,
            Name = "Disable 'What's New' Screens After Updates",
            Description = "Stop the full-screen welcome and 'finish setting up your device' pages that appear after updates.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, 1),
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0, 1),
            },
        };

        yield return new Tweak
        {
            Id = "ui-f1-help", Category = TweakCategory.UI, Preset = PresetLevel.Manual,
            Name = "Disable the F1 Help Key",
            Description = "Stop F1 from opening Windows' web help page in File Explorer and other 64-bit programs.",
            Help = "Overrides the help handler for your account only, the way Sophia Script does. Programs with their own F1 help keep it. 32-bit programs still open the help page: they read a separate entry that is left alone. Undo removes the override but leaves its empty parent keys behind.",
            Icon = "",
            Actions = new TweakAction[]
            {
                new RegistryKeyAction
                {
                    KeyPath = @"HKCU\Software\Classes\Typelib\{8cec5860-07a1-11d9-b15e-000d56bfe6ee}\1.0\0\win64",
                    DeleteOnApply = false,
                    KeyDefaultValue = "",
                },
            },
        };
    }

    // ------------------------------------------------------------------ Windows Features

    // Names are DISM's identifiers, the same on every Windows language. Each was read with
    // Win32_OptionalFeature on Windows 11 Pro 25H2 build 26200 unless its comment says otherwise.
    // All of them finish after a restart, and none is switched with -All (see DismFeatureBackend).
    private static IEnumerable<Tweak> Features()
    {
        yield return new Tweak
        {
            Id = "features-powershell-v2", Category = TweakCategory.Features, Preset = PresetLevel.Balanced,
            Name = "Remove PowerShell 2.0",
            Description = "Turn off the old PowerShell 2.0 engine, which attackers use to get around PowerShell's logging.",
            Help = "PowerShell 2.0 skips the script logging and malware scanning of current PowerShell, which is why attackers downgrade to it; nothing current needs it. Recent Windows 11 builds no longer include it at all, and on those this has nothing to do.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                // Both absent on 26200 (measured); names as in Sophia Script's feature list.
                new FeatureAction { FeatureName = "MicrosoftWindowsPowerShellV2", DefaultEnabled = true },
                new FeatureAction { FeatureName = "MicrosoftWindowsPowerShellV2Root", DefaultEnabled = true },
            },
        };

        yield return new Tweak
        {
            Id = "features-smb1", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Turn Off SMB 1.0",
            Description = "Turn off the obsolete file-sharing protocol the WannaCry ransomware spread through.",
            Help = "Already off on a clean Windows 11; this catches PCs upgraded from older versions. Very old NAS drives, printers and scanners that only speak SMB 1.0 stop working without it.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new FeatureAction { FeatureName = "SMB1Protocol", DefaultEnabled = false },
            },
        };

        yield return new Tweak
        {
            Id = "features-xps", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Remove the XPS Document Writer",
            Description = "Turn off XPS printing and the 'Microsoft XPS Document Writer' printer.",
            Help = "XPS is a PDF alternative that never caught on. Microsoft Print to PDF is a separate feature and keeps working.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                // Disabled on this machine; on by default on Windows 10 and early Windows 11.
                new FeatureAction { FeatureName = "Printing-XPSServices-Features", DefaultEnabled = true },
            },
        };

        yield return new Tweak
        {
            Id = "features-media-player-legacy", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Remove Windows Media Player Legacy",
            Description = "Turn off the old Windows Media Player. The newer Media Player app is not affected.",
            Help = "Only the legacy player goes. 'Media Features' as a whole stays on: turning that off also removes the Multimedia settings from Power Options, as Sophia Script notes.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new FeatureAction { FeatureName = "WindowsMediaPlayer", DefaultEnabled = true },
            },
        };

        yield return new Tweak
        {
            Id = "features-work-folders", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Remove the Work Folders Client",
            Description = "Turn off Work Folders, which syncs files with a company's Windows Server.",
            Help = "Only useful if an organization set Work Folders up on its servers. At home nothing uses it.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new FeatureAction { FeatureName = "WorkFolders-Client", DefaultEnabled = true },
            },
        };

        yield return new Tweak
        {
            Id = "features-sandbox", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Turn On Windows Sandbox",
            Description = "Add Windows Sandbox: a throwaway desktop for trying unknown programs, wiped when you close it.",
            Help = "Needs Windows 11 Pro, Enterprise or Education, and virtualization enabled in the BIOS/UEFI. On Home the feature does not exist, so this shows as unknown.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new FeatureAction { FeatureName = "Containers-DisposableClientVM", Enable = true, DefaultEnabled = false },
            },
        };

        yield return new Tweak
        {
            Id = "features-wsl", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Turn On Windows Subsystem for Linux",
            Description = "Add the two Windows features WSL needs to run Linux distributions.",
            Help = "Adds the Windows side only: choose a distribution afterwards with 'wsl --install' or from the Microsoft Store. It uses virtualization, like Hyper-V.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                // Two separate features, not parent and child, as winutil lists them.
                new FeatureAction { FeatureName = "Microsoft-Windows-Subsystem-Linux", Enable = true, DefaultEnabled = false },
                new FeatureAction { FeatureName = "VirtualMachinePlatform", Enable = true, DefaultEnabled = false },
            },
        };

        yield return new Tweak
        {
            Id = "features-dotnet35", Category = TweakCategory.Features, Preset = PresetLevel.Manual,
            Name = "Turn On .NET Framework 3.5",
            Description = "Add .NET Framework 3.5, which includes 2.0 and 3.0, for older programs that ask for it.",
            Help = "Downloaded from Windows Update, so it needs an internet connection and can take a few minutes.",
            Icon = "", RequiresRestart = true,
            Actions = new TweakAction[]
            {
                new FeatureAction { FeatureName = "NetFx3", Enable = true, DefaultEnabled = false },
            },
        };
    }

    // ------------------------------------------------------------------ Context Menu

    private static IEnumerable<Tweak> ContextMenu()
    {
        yield return new Tweak
        {
            Id = "ctx-multi-invoke", Category = TweakCategory.ContextMenu, Preset = PresetLevel.Balanced,
            Name = "Allow the Context Menu on More Than 15 Files",
            Description = "Keep options like Open and Print in the right-click menu when more than 15 files are selected.",
            Help = "Windows hides some menu entries once a selection passes 15 files. This raises the limit to 300. The key is ...\\CurrentVersion\\Explorer itself, not a subkey.",
            Icon = "",
            Actions = new TweakAction[]
            {
                Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer", "MultipleInvokePromptMinimum", 300, null),
            },
        };

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
                    KeyPath = @"HKCR\AllFileSystemObjects\ShellEx\ContextMenuHandlers\ModernSharing",
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
                // Library folders (Documents, Pictures…) keep their own copy of the handler,
                // so without these two the entry still shows up on right-click there.
                new RegistryKeyAction { KeyPath = @"HKCR\LibraryFolder\background\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
                new RegistryKeyAction { KeyPath = @"HKCR\UserLibraryFolder\shellex\ContextMenuHandlers\Sharing", DeleteOnApply = true, KeyDefaultValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}" },
            },
        };
    }
}
