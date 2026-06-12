# WinPure

**A modern, safe, open-source Windows 11 debloater and optimizer.**

WinPure lets you clean, optimize and take control of Windows 11 from a single Fluent-Design app: privacy & telemetry, bloatware, services, performance, UI and context-menu tweaks — with presets for one-click use and an always-available **Restore** that snapshots every change before it is made.

![WinPure — Privacy & Telemetry page](docs/screenshots/privacy.png)

## Highlights

- 🛡 **Never breaks your system** — every change is captured in a registry/service snapshot *before* it is applied. Roll back any session from the Restore page.
- ⚡ **Three presets** — *Safe* (recommended, fully reversible), *Balanced* (+ optional services & third-party bloatware) and *Aggressive* (+ OneDrive, Copilot/AI, Xbox, DiagTrack).
- 🎛 **Tweak-by-tweak control** — every tweak is a card with a description, detailed help, its real current state (already optimized / not applied) and a toggle. Mix presets with manual selection freely.
- 🔍 **State detection** — on launch WinPure scans the system and shows what is already optimized.
- 📴 **Fully offline** — no servers, no telemetry of its own, single portable .exe (runs from a USB stick).

## Download & run

1. Grab `WinPure.exe` from [Releases](../../releases) — no installer, no .NET required (self-contained).
2. Run it. Windows asks for administrator permission (needed for HKLM/services/tasks).
3. Pick a preset or toggle individual tweaks → **Apply Changes**.

> Backups land in `%AppData%\WinPure\Backups\`, session logs in `%AppData%\WinPure\Logs\`.

## Tweak categories

| Category | Examples |
|---|---|
| **Privacy & Telemetry** | AllowTelemetry → 0, DiagTrack service, telemetry firewall block, Bing in Start, Activity History, Advertising ID, CEIP/Compatibility-Appraiser tasks |
| **Bloatware & Apps** | Candy Crush, TikTok/Facebook/Instagram, Netflix/Disney+/Prime/Spotify, Skype, Clipchamp, Zune apps, Xbox suite, OneDrive, Copilot |
| **Services** | SysMain, Search Indexing, Print Spooler, Remote Registry, Error Reporting, CDP, Geolocation, Fax, Bluetooth |
| **Performance** | App/shutdown timeouts, foreground priority, animations, hibernation, power plan, Remote Desktop |
| **UI & Personalization** | Dark mode, Start suggestions, taskbar Widgets/Task View/Chat, file extensions, hidden files, taskbar alignment |
| **Context Menu** | Classic Windows 10 menu, remove "Edit with Clipchamp/Notepad/Photos", "Ask Copilot", "Share", "Give access to" |

## Building from source

```powershell
# requires .NET 8 SDK
dotnet build WinPure.sln                                  # debug build
dotnet publish src/WinPure/WinPure.csproj -c Release      # portable single-file exe
```

## Credits

Tweak research builds on the excellent work of
[Win11Debloat](https://github.com/Raphire/Win11Debloat),
[Sophia Script](https://github.com/farag2/Sophia-Script-for-Windows),
[CrapFixer](https://github.com/builtbybel/Crapfixer) and
[Win-Debloat-Tools](https://github.com/LeDragoX/Win-Debloat-Tools).

## License

[MIT](LICENSE) — free and open source.
