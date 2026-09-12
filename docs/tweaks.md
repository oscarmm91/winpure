# WinPure — Tweak Reference

Every tweak, what it touches, and which preset includes it. **Manual** = never auto-selected by a preset.
All registry/service changes are snapshotted to `%AppData%\WinPure\Backups\` before being applied.

## 🔒 Privacy & Telemetry

| Tweak | Preset | What it does |
|---|---|---|
| Disable Telemetry | Safe | `HKLM …\Policies\DataCollection!AllowTelemetry = 0`. Only Enterprise/Education honour level 0; Home and Pro clamp it to 1 (Required), their lowest |
| Disable Diagnostics Data | Safe | Tailored experiences off + feedback notifications hidden |
| Disable DiagTrack Service | Aggressive | Stops & disables the Connected User Experiences and Telemetry service |
| Block Telemetry Client (Firewall) | Aggressive | Outbound firewall rule blocking the DiagTrack service |
| Disable Bing in Start Menu | Safe | `DisableSearchBoxSuggestions = 1` — local search only |
| Disable Silent App Installs | Safe | `SilentInstalledAppsEnabled = 0` — no sponsored auto-installs |
| Disable Consumer Features | Safe | `DisableWindowsConsumerFeatures = 1` (no Store games/links auto-install) |
| Disable Delivery Optimization | Balanced | `DODownloadMode = 0` — stop uploading updates with your bandwidth |
| Disable Background Apps | Balanced | `BackgroundAccessApplications!GlobalUserDisabled = 1`. Mail/Calendar stop fetching and notifying until opened; UWP push, live tiles and Photos sync stop |
| Disable Activity History | Safe | Activity feed off; no publishing/uploading user activities |
| Disable Location Tracking | Balanced | `LocationAndSensors!DisableLocation = 1` |
| Disable App Launch Tracking | Safe | `Start_TrackProgs = 0` |
| Disable Advertising ID | Safe | `AdvertisingInfo!Enabled = 0` |
| Disable Windows Feedback | Safe | `Siuf\Rules!NumberOfSIUFInPeriod = 0` |
| Disable Online Speech Recognition | Balanced | `OnlineSpeechPrivacy!HasAccepted = 0` |
| Disable Inking & Typing Personalization | Balanced | InputPersonalization restrictions + TIPC off |
| Disable Compatibility Telemetry Tasks | Aggressive | Disables the *Microsoft Compatibility Appraiser*, *Appraiser Exp* (its 24H2/25H2 successor) & *ProgramDataUpdater* scheduled tasks |
| Disable Search History | Safe | `SearchSettings!IsDeviceSearchHistoryEnabled = 0` |
| Don't Share Your Language List With Websites | Balanced | `International\User Profile!HttpAcceptLanguageOptOut = 1` |
| Disable Diagnostic Data Tasks | Aggressive | Disables *StartupAppTask*, *Autochk\Proxy*, *DiskDiagnosticDataCollector*, *MapsToastTask* & *MapsUpdateTask* (MareBackup left alone: it feeds Windows Backup) |
| Disable CEIP & Feedback Tasks | Aggressive | Disables *Consolidator*, *UsbCeip*, *DmClient* scheduled tasks |

## 📦 Bloatware & Apps

App removals use `Remove-AppxPackage -AllUsers` + de-provisioning. **Undo = reinstall from the Microsoft Store** (WinPure warns before applying).

| Tweak | Preset | Packages / keys |
|---|---|---|
| Remove Candy Crush | Balanced | `king.com.*` |
| Remove Social Media Apps | Balanced | TikTok, Facebook, Twitter/X, Instagram |
| Remove Streaming Apps | Balanced | Netflix, Disney+, Prime Video, Spotify |
| Remove Skype | Balanced | `Microsoft.SkypeApp` |
| Remove Clipchamp | Balanced | `Clipchamp.Clipchamp` |
| Remove Paint 3D & 3D Viewer | Balanced | `Microsoft.MSPaint`, `Microsoft.Microsoft3DViewer` |
| Remove Microsoft To Do | Manual | `Microsoft.Todos` |
| Remove Groove Music & Movies + TV | Balanced | `Microsoft.ZuneMusic`, `Microsoft.ZuneVideo` |
| Remove Solitaire Collection | Balanced | `Microsoft.MicrosoftSolitaireCollection` |
| Remove Wallet | Balanced | `Microsoft.Wallet` |
| Remove Whiteboard | Manual | `Microsoft.Whiteboard` |
| Remove Phone Link | Balanced | `Microsoft.YourPhone` |
| Remove Dev Home | Balanced | `Microsoft.Windows.DevHome` |
| Remove Get Help & Tips | Balanced | `Microsoft.GetHelp`, `Microsoft.Getstarted`, `Microsoft.StartExperiencesApp` (Tips on 24H2/25H2) |
| Remove Xbox Apps & Overlay | Aggressive | Xbox app family + Game DVR off (`GameDVR_Enabled = 0`, `AllowGameDVR = 0`) |
| Disable Copilot / Windows AI | Aggressive | `TurnOffWindowsCopilot = 1` (HKLM + HKCU) + taskbar button off |
| Disable Windows AI & Recall | Aggressive | `WindowsAI!DisableAIDataAnalysis = 1` (HKLM + HKCU), `AllowRecallEnablement = 0`, `AllowRecallExport = 0`, Notepad AI off, AI Settings page hidden |
| Disable Click to Do | Balanced | `WindowsAI!DisableClickToDo = 1` (HKLM + HKCU) - the AI overlay on selected text/images |
| Disable Paint AI Features | Manual | `CurrentVersion\Policies\Paint!DisableCocreator / DisableImageCreator / DisableGenerativeFill = 1` |
| Remove Preinstalled Casual Games | Balanced | `Asphalt8Airborne`, `CaesarsSlotsFreeCasino`, `COOKINGFEVER`, `DisneyMagicKingdoms`, `FarmVille2CountryEscape`, `HiddenCity`, `MarchofEmpires`, `NYTCrossword`, `RoyalRevolt` |
| Remove AI Hub | Balanced | `Microsoft.Windows.AIHub` (Copilot+ PCs) |
| Disable Game Bar Capture | Manual | `GameDVR!AppCaptureEnabled = 0` + `GameBar!ShowStartupPanel = 0` — nothing uninstalled; an alternative to removing Xbox |
| Disable Game Bar Integration | Manual | `GameBar!UseNexusForGameBarEnabled = 0` + do-nothing handler under `HKCU\SOFTWARE\Classes\ms-gamebar` — silences the ms-gamebar popup after removing Xbox |
| Remove OneDrive | Aggressive | `OneDriveSetup /uninstall` + `DisableFileSyncNGSC = 1` (files stay on disk) |

## ⚙️ Services

Disabling sets registry `Start = 4` and stops the service; the original start mode is captured in the backup.

| Tweak | Preset | Service | Stock mode |
|---|---|---|---|
| Disable SysMain (SuperFetch) | Manual | `SysMain` | Automatic |
| Disable Search Indexing | Manual | `WSearch` | Automatic |
| Disable Print Spooler | Manual | `Spooler` | Automatic |
| Disable Remote Registry | Balanced | `RemoteRegistry` | Disabled |
| Disable Windows Error Reporting | Balanced | `WerSvc` | Manual |
| Disable Connected Devices Platform | Manual | `CDPSvc` - also breaks Night Light | Automatic |
| Disable Geolocation Service | Balanced | `lfsvc` | Manual |
| Disable Fax Service | Balanced | `Fax` | Manual |
| Disable Bluetooth Support | Manual | `bthserv` | Manual |

## 🚀 Performance

| Tweak | Preset | What it does |
|---|---|---|
| Faster App Timeouts | Safe | `WaitToKillAppTimeout 5000→2000`, `HungAppTimeout 5000→1000` |
| Faster System Shutdown | Balanced | `WaitToKillServiceTimeout 5000→2000` |
| Disable Window Animations | Manual | `MinAnimate = 0`, taskbar animations off |
| Prioritize Foreground Apps | Balanced | `Win32PrioritySeparation = 38` |
| Disable Hibernation | Manual | `powercfg /hibernate off` (frees hiberfil.sys); undo restores the setting this PC had |
| High Performance Power Plan | Manual | `powercfg /setactive` High Performance (not for laptops); undo returns to the plan that was active |
| Disable Reserved Storage | Manual | `Set-WindowsReservedStorageState -State Disabled` (frees the update reserve) |
| Disable Fullscreen Optimizations | Manual | `GameDVR_DXGIHonorFSEWindowsCompatible = 1` |
| Disable Mouse Acceleration | Manual | `MouseSpeed/Threshold1/Threshold2 = 0` |
| Enable Long Paths | Manual | `FileSystem!LongPathsEnabled = 1` |
| Disable Remote Desktop | Manual | `fDenyTSConnections = 1` — on Pro this disables a feature that works |
| Disable Fast Startup | Balanced | `Session Manager\Power!HiberbootEnabled = 0` |
| Don't Get Updates As Soon As They're Available | Balanced | `WindowsUpdate\UX\Settings!IsContinuousInnovationOptedIn = 0` — security updates unaffected |
| Enable Daily Registry Backup | Manual | `Configuration Manager!EnablePeriodicBackup = 1` |
| Sync the Clock With pool.ntp.org | Manual | `W32Time\Parameters!NtpServer = pool.ntp.org,0x9` (same flags as stock: only the server changes) |

## 🎨 UI & Personalization

| Tweak | Preset | What it does |
|---|---|---|
| Enable Dark Mode | Safe | `AppsUseLightTheme = 0`, `SystemUsesLightTheme = 0` |
| Disable Snap Assist Flyout | Manual | `EnableSnapAssistFlyout = 0` |
| Hide Suggestions in Start | Safe | Subscribed content + `Start_IrisRecommendations = 0` |
| Hide Most Used Apps in Start | Manual | `ShowOrHideMostUsedApps = 2` |
| Hide Recently Added Apps in Start | Manual | `HideRecentlyAddedApps = 1` |
| Hide 'New App Installed' Badge | Safe | `NoNewAppAlert = 1` |
| Show File Extensions | Safe | `HideFileExt = 0` |
| Show Hidden Files | Manual | `Hidden = 1` |
| Remove Widgets Button | Safe | `Dsh!AllowNewsAndInterests = 0` (the policy current Windows honours). `TaskbarDa = 0` is still written for pre-24H2 builds, but newer ones block it - it is optional and cannot fail the tweak |
| Disable Search Highlights | Safe | `SearchSettings!IsDynamicSearchBoxEnabled = 0` |
| Turn NumLock On at Startup | Manual | `Keyboard!InitialKeyboardIndicators` for your account and for the sign-in screen (`HKU\.DEFAULT`) |
| Block Spotlight as Desktop Background | Manual | `Policies\...\CloudContent!DisableSpotlightCollectionOnDesktop = 1` (per-user policy) |
| Disable 'What's New' Screens After Updates | Safe | `UserProfileEngagement!ScoobeSystemSettingEnabled = 0` + `SubscribedContent-310093Enabled = 0` |
| Disable the F1 Help Key | Manual | Empty help handler under `HKCU\Software\Classes\Typelib\{8cec5860-...}\1.0\0\win64` (64-bit programs) |
| Remove Task View Button | Safe | `ShowTaskViewButton = 0` |
| Remove Chat/Teams Button | Safe | `TaskbarMn = 0` |
| Align Taskbar Left | Manual | `TaskbarAl = 0` |
| End Task on Taskbar Right-Click | Safe | `TaskbarDeveloperSettings!TaskbarEndTask = 1` |
| Disable Aero Shake | Safe | `DisallowShaking = 1` |

## 🖱️ Context Menu

| Tweak | Preset | Mechanism |
|---|---|---|
| Restore Classic Context Menu | Safe | Empty `InprocServer32` for CLSID `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` |
| Remove 'Edit with Clipchamp' | Safe | Blocked shell extension `{8AB635F8-9A67-4698-AB99-784AD929F3B4}` |
| Remove 'Edit with Notepad' | Manual | Blocked shell extension `{CA6CC9F1-867A-481E-951E-A28C5E4F01EA}` |
| Remove 'Edit with Photos' | Manual | Blocked shell extension `{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}` |
| Remove 'Ask Copilot' | Safe | Blocked shell extension `{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}` |
| Remove 'Share' | Manual | Deletes the `ModernSharing` handler under `AllFileSystemObjects` (recreated on revert) |
| Remove 'Give access to' | Safe | Deletes the 6 `Sharing` handlers: files, folders, background, drives & both library folders |
| Allow the Context Menu on More Than 15 Files | Balanced | `CurrentVersion\Explorer!MultipleInvokePromptMinimum = 300` |

## 🧩 Windows Features

Switched with DISM (`Enable-` / `Disable-WindowsOptionalFeature -NoRestart`, never `-All`) and detected with `Win32_OptionalFeature`. Undo puts back the state each feature had; a feature this Windows build does not include is left alone. Most changes finish after a restart.

| Tweak | Preset | Feature |
|---|---|---|
| Remove PowerShell 2.0 | Balanced | `MicrosoftWindowsPowerShellV2Root`, which takes `MicrosoftWindowsPowerShellV2` with it (already gone on current 25H2) |
| Turn Off SMB 1.0 | Manual | `SMB1Protocol` |
| Remove the XPS Document Writer | Manual | `Printing-XPSServices-Features` |
| Remove Windows Media Player Legacy | Manual | `WindowsMediaPlayer` (Media Features stay on) |
| Remove the Work Folders Client | Manual | `WorkFolders-Client` |
| Turn On Windows Sandbox | Manual | `Containers-DisposableClientVM` (Pro, Enterprise, Education) |
| Turn On Windows Subsystem for Linux | Manual | `Microsoft-Windows-Subsystem-Linux` + `VirtualMachinePlatform` |
| Turn On .NET Framework 3.5 | Manual | `NetFx3` (downloaded from Windows Update) |

## 🔧 Repair & Maintenance (one-shot tools)

| Tool | What it runs |
|---|---|
| Create System Restore Point | `Enable-ComputerRestore` + `Checkpoint-Computer` (frequency limit lifted) |
| Repair System Files | `sfc /scannow` + `DISM /Online /Cleanup-Image /RestoreHealth` |
| Reset Windows Update | Stops `wuauserv/BITS/cryptsvc`, clears download cache + qmgr, restarts services |
| Reset Network | `netsh winsock reset`, `netsh int ip reset`, `ipconfig /flushdns` |
| Clean Temporary Files | Empties `%TEMP%` and `C:\Windows\Temp`, reports MB freed |
