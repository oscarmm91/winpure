# Changelog

## Unreleased

### New: Startup Apps

A page that lists everything that starts with Windows and lets you switch any of it off without uninstalling anything:

- Registry `Run` entries for this user, for all users, and the separate 32-bit ones most tools miss.
- Shortcuts and scripts in both Startup folders (yours and All Users).
- Third-party scheduled tasks that run at logon — a real source of startup apps that never shows up in the registry.
- Friendly name, publisher and the actual command line for each entry, plus a **file missing** flag for entries pointing at something that no longer exists.
- Switching an entry flips the same `StartupApproved` bit Task Manager uses, so WinPure and Task Manager always show the same thing. The original bytes are backed up verbatim, so Restore puts back exactly what was there — including entries that had no setting at all, which are removed again rather than left behind.
- Changes here take effect immediately instead of waiting for Apply Changes, and the status bar says so.

- **Fix (important):** turning a tweak off now restores the value **your machine actually had**, taken from the backup. It used to write a "stock default" hand-written in the catalog, which could switch on a setting you never had enabled.
- **Fix:** turning a tweak off is now backed up too — previously only applying was.
- **Fix:** the backup snapshot is written to disk *before* each change instead of once at the end of a batch, so force-closing WinPure (or a crash, or a power cut) mid-apply no longer leaves changes with no backup at all. If the backup cannot be written, nothing is changed.
- **Fix:** scheduled-task tweaks record whether the task was really enabled instead of assuming it was; reverting no longer re-enables telemetry tasks that were already off or absent.
- **Fix:** a system scan that partly fails now reports *"Couldn't detect"* instead of silently showing **Already optimized** — a failed app or task query used to be indistinguishable from "nothing installed".
- **Fix:** a hung PowerShell call could freeze the app forever; output is now drained in the background and the timeout really kills the process. Output is read as UTF-8, so accented names no longer come back mangled.
- **Fix:** service, scheduled-task and Store-app operations no longer report success when the underlying command failed.
- Backups are written atomically, and unreadable backup files are logged instead of quietly disappearing from the Restore page.
- New `tests/WinPure.EngineTests` project covering the backup/revert engine, the scan, the catalog and the repair tools, run on every CI build.

### Checks before changing anything

WinPure now looks at the state of the system on every scan and, if something is wrong, tells you **before** applying — with the default answer set to *No*. It never refuses to run: someone fixing another person's PC, or who turned a service off on purpose, can still go ahead.

- **Running as a different user.** Elevating with a second administrator account sends every per-user tweak and every Startup change to *that* account's profile, and a restore would follow it there. Also checked before restoring a backup, before the first Startup change, and before a repair tool. The two accounts are compared by SID, not by name, so a Microsoft or work account whose name is formatted differently by different parts of Windows is never mistaken for someone else.
- **A restart is pending** from Windows servicing or Windows Update. Changes applied now can be overwritten when Windows finishes. (Sophia Script's version of this check requires all five of its signals at once, so it effectively never fires; this one fires on any. Files an installer queued for replacement are deliberately *not* counted: they do not affect settings, and some software leaves them there indefinitely, which would make the warning appear every day.)
- **BitLocker is encrypting or decrypting** the system drive. Read-only: WinPure will never offer to decrypt a drive.
- **Core Windows app components are missing** — a sign another tool already removed them, and that removing more apps may break Start or Settings. Only judged when the app listing succeeded and covered every user. On LTSC editions, which ship without the Microsoft Store by design, only the shell package is required.
- Repair tools now also pass through these checks where they apply, and **Clean Temporary Files asks before deleting**.
- **PowerShell helpers no longer outlive the app.** Closing WinPure mid-scan used to leave `powershell.exe` running with nothing left to stop it; Windows now ends them together with the app. The one exception is SFC + DISM, which keeps running if you close the window, because interrupting a Windows repair halfway is worse than letting it finish.
- **The Event Log service is stopped**, which is rarely deliberate and usually means another tool has been here.

### Interface

- Long repairs now show **elapsed time**, so a 25-minute SFC no longer looks like a frozen app, and the ones that are safe to interrupt gained a **Cancel** button. SFC + DISM deliberately has none: killing it midway can leave Windows' component store inconsistent.
- Choosing a preset no longer silently clears tweaks you ticked by hand — they are kept, and the status bar says how many.
- The preset buttons now show which preset is active, and keep showing it when you move between pages.
- Buttons no longer stay greyed out after a long operation finishes until you move the mouse.

### Tweaks fixed (verified against Windows 11 24H2/25H2, build 26200)

- **Remove Widgets Button did nothing on current Windows — and it is in the Safe preset.** It only wrote `TaskbarDa`, which Microsoft's UCPD driver now blocks for every known executable (measured on build 26200: the write is refused outright). It now applies the `Dsh!AllowNewsAndInterests` policy, which current Windows honours, and keeps `TaskbarDa` as an optional fallback for builds before 24H2.
- **Windows AI & Recall was only half blocked.** Added `AllowRecallEnablement = 0` and `AllowRecallExport = 0`, both verified against Windows' own `WindowsCopilot.admx` on build 26200 — without the first, Recall can simply be switched back on. The tweak now also warns that snapshots Recall already saved are deleted on the next restart, which is what Microsoft's own policy text says.
- **Two new AI tweaks, taken from that same ADMX rather than from a reference repo:** Click to Do (the overlay that appears when you select text or an image on 24H2+) and Paint's Cocreator, Image Creator and generative fill.
- **Disable Copilot no longer writes a machine-wide value Windows never reads** — `TurnOffWindowsCopilot` is declared as a per-user policy, so the HKLM copy that other debloaters also write did nothing.
- Every policy write is now checked against the machine's ADMX definitions as part of the test run — matching the path and the user/machine scope, not just the value name.
- Actions can now be marked optional: a legacy value the OS refuses to write is logged and skipped instead of failing the whole tweak or pinning it to "Not applied" forever.

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
