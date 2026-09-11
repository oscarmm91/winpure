# Changelog

## Unreleased

- **Fix (important):** turning a tweak off now restores the value **your machine actually had**, taken from the backup. It used to write a "stock default" hand-written in the catalog, which could switch on a setting you never had enabled.
- **Fix:** turning a tweak off is now backed up too — previously only applying was.
- **Fix:** the backup snapshot is written to disk *before* each change instead of once at the end of a batch, so force-closing WinPure (or a crash, or a power cut) mid-apply no longer leaves changes with no backup at all. If the backup cannot be written, nothing is changed.
- **Fix:** scheduled-task tweaks record whether the task was really enabled instead of assuming it was; reverting no longer re-enables telemetry tasks that were already off or absent.
- **Fix:** a system scan that partly fails now reports *"Couldn't detect"* instead of silently showing **Already optimized** — a failed app or task query used to be indistinguishable from "nothing installed".
- **Fix:** a hung PowerShell call could freeze the app forever; output is now drained in the background and the timeout really kills the process. Output is read as UTF-8, so accented names no longer come back mangled.
- **Fix:** service, scheduled-task and Store-app operations no longer report success when the underlying command failed.
- Backups are written atomically, and unreadable backup files are logged instead of quietly disappearing from the Restore page.
- New `tests/WinPure.EngineTests` project covering the backup/revert engine and the scan, run on every CI build.

### Tweaks fixed (verified against Windows 11 24H2/25H2, build 26200)

- **Remove Dev Home** never removed anything: the package is `Microsoft.Windows.DevHome`, and the old pattern did not match it — so it also reported itself as already done.
- **Remove 'Share'** pointed at a context-menu key that no longer exists; the handler now lives under `AllFileSystemObjects`. Same story: it claimed to be applied without doing anything.
- **Disable Compatibility Telemetry Tasks** now also covers *Microsoft Compatibility Appraiser Exp*, the task that replaced the two originals on 24H2/25H2 (the old ones are kept for machines upgraded from Windows 10).
- **Remove 'Give access to'** now clears all six handlers — the two library-folder ones were missed, so the entry still showed up on Documents, Pictures and friends.
- **Remove Get Help & Tips** also covers `Microsoft.StartExperiencesApp`, which replaced `Microsoft.Getstarted`.
- **Disable Connected Devices Platform** moved from Balanced to Manual: it stops Night Light from working, which nobody would trace back to this setting. Now says so.
- **Disable Remote Desktop** moved from Balanced to Manual: on Windows 11 Pro that is a working feature, not dead weight.
- **Disable Background Apps** and **Disable Telemetry** now spell out what they really do — Mail and Calendar stop notifying in the background, and telemetry level 0 is clamped to 1 on Home and Pro.

## v1.1.1 — 2026-06-12

- **Fix:** toggling Dark Mode now broadcasts `WM_SETTINGCHANGE (ImmersiveColorSet)`, so File Explorer and the taskbar repaint instantly instead of staying half-dark until Explorer restarts. Restoring a backup with theme values triggers the same refresh.
- App icon (blue shield + star) embedded in the executable.
- Repair page icons fixed.
- README overhauled: badges, real screenshots of every page, full tweak reference in `docs/tweaks.md`.

## v1.1.0 — 2026-06-11

- New **Repair & Maintenance** page (inspired by Chris Titus WinUtil): create restore point, SFC + DISM system file repair, reset Windows Update components, reset network stack, clean temporary files.
- 10 new tweaks ported from WinUtil: Consumer Features, Delivery Optimization, Background Apps, Windows AI & Recall, Fullscreen Optimizations, Mouse Acceleration, Long Paths, End Task on taskbar right-click.
- **About** dialog with author info and social links (sidebar button).

## v1.0.0 — 2026-06-11

Initial release.

- 7 tweak categories: Privacy & Telemetry, Bloatware & Apps, Services, Performance, UI & Personalization, Context Menu, plus scheduled-task tweaks under Privacy.
- Safe / Balanced / Aggressive presets, freely mixable with manual tweak selection.
- Automatic registry/service/task snapshot before every apply; full Restore page with per-session rollback and backup management.
- System state detection on launch ("Already optimized" vs "Not applied").
- Dark Fluent Design UI, custom title bar, Segoe Fluent iconography.
- Portable single-file executable, automatic UAC elevation, session logs in `%AppData%\WinPure\Logs`.
