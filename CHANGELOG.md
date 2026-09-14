# Changelog

## 2.1.0 — more of the toolbox

More of the utility toolbox, each page reversible where it can be and clearly "no undo" where it cannot:

- **Move folder** — move a big folder to another drive and leave a junction behind, so programs still find it. The data is copied and verified BEFORE the original is removed, so an interrupted move never loses anything. It refuses system folders, whole user profiles, same-drive moves, and moves without enough free space, and never follows junctions inside the folder.
- **Uninstall Apps** — remove any installed program by running its own uninstaller. Judged by whether the program is actually gone afterwards, never by an exit code. Not undone by Restore.
- **Hardware** — a read-only look at the processor, memory, graphics, drives and motherboard, read from the registry — no driver and no extra dependency.
- **PATH editor** — review the user or machine PATH and remove the dead, duplicate and empty entries, reversibly (the whole PATH is backed up first). A folder on a drive that is not connected is never treated as dead.
- **Safe Mode** — restart into Windows Safe Mode (with or without networking) and back. Restoring normal boot is always one click and works from inside Safe Mode too.
- **Context menu** — added "Take ownership" (files and folders) and a "Run with priority" submenu for programs.
- **Disable mouse acceleration** now takes effect immediately, without signing out.
- **Install** — added the DirectX End-User Runtime.

Reversible tweaks: **139** (up from 137). Everything reversible still rolls back from the Restore page.

## 2.0.0 — WinPure, all-in-one

WinPure grows from a debloater into an all-in-one Windows 11 toolbox. On top of **137 reversible tweaks** (up from ~111, every key verified on a real machine and every policy cross-checked against its `.admx`):

- **Clean up** — delete regenerable junk (temp, Windows Update and Delivery Optimization caches, browser and GPU shader caches, game-launcher caches, crash dumps, the Recycle Bin, and a previous `Windows.old`) with a size preview. Deletion is permanent by design and clearly separated from everything reversible.
- **DNS** — switch every network adapter to Cloudflare, Quad9, AdGuard, Google, OpenDNS or back to automatic; your current servers are captured first, so Restore puts them back.
- **Hosts** — edit the Windows hosts file with a multi-level timestamped backup ("undo last save") and a one-click reset to the Windows default.
- **Free up memory** — an honest RAM trim that says plainly the gain is usually brief.
- **Power** — schedule a shutdown or restart (Windows' own timer, so it persists if you close WinPure), or sleep, hibernate, lock and sign out now. The delay is bounded so a mistyped value can never fire an immediate shutdown.
- **Diagnostics** — a read-only summary of the PC and a one-click support bundle (the WinPure logs plus the summary) you can save and share.
- **Home** now shows live memory and disk use, and its count cards open filtered lists.
- **Repair** — added component-store cleanup (`DISM /StartComponentCleanup`), re-register Store apps, update all apps (winget), and install the Visual C++ redistributables.
- **Install** — a winget search box to install any app, not just the curated list.
- **Startup** — applies in one batch (one backup instead of one per toggle), with an On/Off filter.
- **Apply to new user accounts** — a checkbox that also writes reversible per-user tweaks into the default profile.

Everything reversible stays reversible through the Restore page; the delete, install, remove-apps and power sections are separated out and confirm before acting. Backups now live in an administrators-only folder whose trust is proven by the file's owner, not by the shape of its permissions.

### New: Startup Apps

A page that lists everything that starts with Windows and lets you switch any of it off without uninstalling anything:

- Registry `Run` entries for this user, for all users, and the separate 32-bit ones most tools miss.
- Shortcuts and scripts in both Startup folders (yours and All Users).
- Third-party scheduled tasks that run at logon — a real source of startup apps that never shows up in the registry.
- Friendly name, publisher and the actual command line for each entry, plus a **file missing** flag for entries pointing at something that no longer exists.
- Switching an entry flips the same `StartupApproved` bit Task Manager uses, so WinPure and Task Manager always show the same thing. The original bytes are backed up verbatim, so Restore puts back exactly what was there — including entries that had no setting at all, which are removed again rather than left behind.
- Changes are staged in memory and written in one batch: a single **Apply Changes** makes one backup for everything, instead of applying (and backing up) on every toggle. The status bar says how many are pending.

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

- **Running as a different user.** Elevating with a second administrator account sends every per-user tweak and every Startup change to *that* account's profile, and a restore would follow it there. Also checked before restoring or deleting a backup (the backups listed belong to the account WinPure runs as), before the first Startup change, and before a repair tool. The two accounts are compared by SID, not by name, so a Microsoft or work account whose name is formatted differently by different parts of Windows is never mistaken for someone else.
- **A restart is pending** from Windows servicing or Windows Update. Changes applied now can be overwritten when Windows finishes. (Sophia Script's version of this check requires all five of its signals at once, so it effectively never fires; this one fires on any. Files an installer queued for replacement are deliberately *not* counted: they do not affect settings, and some software leaves them there indefinitely, which would make the warning appear every day.)
- **BitLocker is encrypting or decrypting** the system drive. Read-only: WinPure will never offer to decrypt a drive.
- **Core Windows app components are missing** — a sign another tool already removed them, and that removing more apps may break Start or Settings. Only judged when the app listing succeeded and covered every user. On LTSC editions, which ship without the Microsoft Store by design, only the shell package is required.
- Repair tools now also pass through these checks where they apply, and **Clean Temporary Files asks before deleting**.
- **Scans no longer leave PowerShell behind.** Closing WinPure mid-scan used to leave `powershell.exe` running with nothing left to stop it; Windows now ends read-only queries together with the app. Anything that changes the system — removing an app, stopping a service, uninstalling OneDrive, repairing Windows with SFC + DISM — is deliberately left to finish, because cutting it off halfway is worse than letting it complete.
- Looking up the signed-in account can no longer stall the scan: it gets a three-second deadline, after which the different-user check simply stays silent.
- **The Event Log service is stopped**, which is rarely deliberate and usually means another tool has been here.

### Interface

- Long repairs now show **elapsed time**, so a 25-minute SFC no longer looks like a frozen app, and the ones that are safe to interrupt gained a **Cancel** button. SFC + DISM deliberately has none: killing it midway can leave Windows' component store inconsistent.
- Choosing a preset no longer silently clears tweaks you ticked by hand — they are kept, and the status bar says how many.
- The preset buttons now show which preset is active, and keep showing it when you move between pages.
- Buttons no longer stay greyed out after a long operation finishes until you move the mouse.
- Eight tweaks — the ones ported from WinUtil — showed a blank square where their icon should be.

### Undo puts back what you had — power settings too

- **High Performance Power Plan** was undone with a fixed command that always switched to Balanced, even if you had been on a custom plan. **Disable Hibernation** was undone by turning hibernation on, even on PCs where it had been off. Both now record the setting before changing it, and undo puts that back.
- New: **Disable Reserved Storage** (in no preset), with the same record-and-restore undo.
- A setting read back from a backup file is checked before anything runs, so an edited backup cannot make WinPure — which runs as administrator — execute something else.

### New: Install Apps

A separate page installs 23 popular apps with winget — browsers, 7-Zip, Everything, PowerToys, VLC, OBS, Git, VS Code, Discord, LibreOffice, Steam and more — straight from their publishers.

- Like Remove Apps, it is a page WinPure cannot undo, so it stays apart from the tweaks, is in no preset, and asks before every install, saying that Restore will not remove the app and that installing accepts the app's license terms.
- Which apps are already installed comes from winget's own export, checked when you open the page. Apps winget cannot match to its catalog show as *Not detected* rather than *Not installed*.
- An install is judged by what winget sees afterwards, not by its exit code: an installer that reports success but leaves nothing behind is shown as not installed.

### Security: a backup file can no longer make WinPure run something else

Backups are plain files in your profile, which any program you run can edit without administrator rights, while WinPure restores them as administrator. A review found that a hand-edited backup could have turned that into a way to run commands or write anywhere in the registry as administrator: service names and scheduled-task paths went straight into PowerShell, and registry values were restored wherever the file pointed.

- A restored entry may now only touch what WinPure itself changes: the registry values, keys, services, scheduled tasks and Windows features in its catalog, and the Startup page's own switches. Anything else is refused and logged.
- Every service and task name is quoted before it reaches PowerShell, including the typographic quotes PowerShell also accepts.
- A backup made by an older version that restores a value this version no longer changes is refused too, and listed in the log.
- **Backups moved to `%ProgramData%\WinPure\Backups`**, a folder only administrators can write, with its permissions locked so that not even the account that owns a file can reopen it. A second review showed why checking entries is not enough on its own: a forged file could still ask for things a genuine undo asks for — SMB 1.0 back on, a disabled service back to Automatic — and hijack a plain Undo with a future date. Only a file's origin tells them apart.
- Your existing backups are copied into the new folder the first time this version runs, and the originals in `%AppData%\WinPure\Backups` are left where they were. Backups are now shared by every administrator account on the PC.
- The entry checks stay as a second layer, and got stricter: service start modes, the exact value restored into a context-menu handler, the exact Startup switch keys, and scheduled-task paths with doubled backslashes.

### Finding your way around

- **Search** at the top of the sidebar looks through every tweak's name, description and help, across all categories. Case and accents are ignored; clicking any page in the sidebar leaves the results.
- Each category in the sidebar shows **how many changes are waiting** for Apply Changes.
- Tweaks that only take full effect after a restart now say so with an icon on their card, next to the existing one for app removals.

### New: export and import your configuration

Two buttons on the Dashboard save which tweaks are switched on to a small JSON file, and tick the same ones on another PC.

- The file holds tweak names only — no values, paths or commands — so a configuration from anyone can at most tick boxes.
- Importing never unticks anything and never applies anything: you review the ticks and click Apply Changes, with the usual checks and the confirmation for app removals. Tweaks this version does not know are skipped and counted.
- Different from a backup, which rolls back this PC.

### New: Windows Features

A new page for Windows' optional features, switched with DISM and undone to the state each one had:

- **Turn off:** PowerShell 2.0 (in Balanced — recent Windows 11 builds no longer include it), SMB 1.0, the XPS Document Writer, Windows Media Player Legacy and the Work Folders client.
- **Turn on:** Windows Sandbox, Windows Subsystem for Linux and .NET Framework 3.5.
- A feature query that fails reads as *Couldn't detect*, never *Already optimized*. A feature your Windows build does not include is left alone, and a feature read back from a backup file is checked before DISM ever runs.

### Presets no longer remove apps: a Remove Apps page

Uninstalling an app is the one tweak WinPure cannot undo, so it no longer hides inside a preset.

- All 18 removals — the Store apps, Xbox and OneDrive — moved to their own page, **Remove Apps**, at the bottom of the sidebar next to Install Apps. It shows no preset buttons, and a red warning in their place.
- **Balanced no longer removes any app — it used to remove 14, most of them Microsoft's own (Skype, Clipchamp, Phone Link, Dev Home…) — and Aggressive no longer removes OneDrive or the Xbox apps.** Everything a preset does can now be switched back off. Game DVR, which Remove Xbox Apps also switches off, left Aggressive with it.
- Importing a configuration never ticks a removal; the status bar says how many it left for you on Remove Apps. A file can list removals just because those apps were already gone on the PC that exported it.
- A removal that is already done cannot be switched back off: its toggle stays on instead of promising an undo that does not exist.
- The confirmation before removing now defaults to *No* and names OneDrive's own way back; the apply bar says when pending changes include a removal; and on Restore, a session that removed an app lists it first and says *(app not reinstalled)*.
- **Fix:** a backup now records the name of the last tweak of each apply too. It was never saved, so on Restore every session named one tweak fewer, and an apply that only removed a Store app did not appear at all.
- Search results that include an app removal show the same red warning, and no preset buttons.
- The Apps page is now **Apps & AI**: Copilot, Windows AI & Recall, Click to Do, Paint AI and Game Bar.

### New: Microsoft Edge page

Seven tweaks that clean up Edge's new tab page and stop its promotions, written as Edge policies under `HKLM\SOFTWARE\Policies\Microsoft\Edge`: news and trending searches, default tiles, the Microsoft 365 launcher, feature tips and the Acrobat upsell, default-browser and PDF prompts, the shopping assistant, and Microsoft Rewards.

- All Manual, in no preset, and the page shows no preset buttons. While any Edge policy is set, Edge says *"Managed by your organization"*, and every tweak's help says so.
- Researched against Microsoft's Edge policy documentation and this PC's Edge 152, then checked by two independent reviews. The roadmap's "13 policies" came from a single Win11Debloat file, and only seven of those still hold up; one more policy was dropped because it only touches Edge's Enterprise new tab page.
- Not verified yet: Microsoft documents several of these as not applying to a profile signed in with a personal Microsoft account. `edge://policy` shows whether Edge applied each one.

### WinPure in Spanish

WinPure now speaks Spanish. It follows the language Windows is set to: Spanish Windows, Spanish app; any other language stays in English.

- Everything on screen: every tweak's name, description and help, the presets, Startup Apps, Repair, Restore, Install Apps, and every check and confirmation shown before a change.
- Search looks in both languages, so a setting you know by its English name is still found.
- A tweak title too long for its card now wraps, and its help and restart icons move to the next line instead of being pushed out of sight.
- Backups still record tweak names in English, so a backup reads the same whichever language made it; the Restore page shows the names in yours.
- Text without a translation shows in English rather than as an empty space, and a broken translation falls back to English instead of stopping the app. The test suite fails whenever something on screen has no Spanish, a translation is left over from text that has changed, or a translation's placeholders differ from the English.

### 17 new tweaks

Registry keys, scheduled tasks and policies were checked against a real Windows 11 25H2 machine (build 26200) before being added, and every one reverts to the value the machine actually had.

- **Privacy:** search history; the language list websites can read; diagnostic data tasks (disk diagnostics, Autochk, startup scan, offline maps — MareBackup is left alone because Windows Backup relies on it).
- **Apps:** preinstalled casual games (Asphalt, FarmVille, Royal Revolt…) and AI Hub on Copilot+ PCs, both now on Remove Apps; Game Bar capture, as a lighter option than removing the Xbox apps; Game Bar integration, which also silences the "you'll need a new app" popup for ms-gamebar links once those apps are gone.
- **Performance:** Fast Startup; early optional updates (security updates unaffected); daily registry backup; syncing the clock with pool.ntp.org.
- **Interface:** search highlights; NumLock on at sign-in; Spotlight as desktop background; the "What's new" screens after updates; the F1 help key.
- **Context menu:** keep right-click options when more than 15 files are selected.

### Tweaks fixed (verified against Windows 11 24H2/25H2, build 26200)

- **Remove Widgets Button did nothing on current Windows — and it is in the Safe preset.** It only wrote `TaskbarDa`, which Microsoft's UCPD driver now blocks for every known executable (measured on build 26200: the write is refused outright). It now applies the `Dsh!AllowNewsAndInterests` policy, which current Windows honours, and keeps `TaskbarDa` as an optional fallback for builds before 24H2.
- **Windows AI & Recall was only half blocked.** Added `AllowRecallEnablement = 0` and `AllowRecallExport = 0`, both verified against Windows' own `WindowsCopilot.admx` on build 26200 — without the first, Recall can simply be switched back on. The tweak now also warns that snapshots Recall already saved are deleted on the next restart, which is what Microsoft's own policy text says.
- **Two new AI tweaks, taken from that same ADMX rather than from a reference repo:** Click to Do (the overlay that appears when you select text or an image on 24H2+) and Paint's Cocreator, Image Creator and generative fill.
- **Disable Copilot no longer writes a machine-wide value Windows never reads** — `TurnOffWindowsCopilot` is declared as a per-user policy, so the HKLM copy that other debloaters also write did nothing.
- Every policy write is now checked against the machine's ADMX definitions as part of the test run — matching the path and the user/machine scope, not just the value name.
- Actions can now be marked optional: a legacy value the OS refuses to write is logged and skipped instead of failing the whole tweak or pinning it to "Not applied" forever.

- **Remove Dev Home** never removed anything: the package is `Microsoft.Windows.DevHome`, and the old pattern did not match it — so it also reported itself as already done.
- **Remove 'Share'** pointed at a context-menu key that no longer exists; the handler now lives under `AllFileSystemObjects`. Same story: it claimed to be applied without doing anything.
- **Disable Compatibility Telemetry Tasks** now also covers *Microsoft Compatibility Appraiser Exp*, the task that replaced the two originals on 24H2/25H2 (the old ones are kept for machines upgraded from Windows 10). The first version of that fix forgot to tell the scan about the new task, so the tweak read as already applied whatever the task's real state; a test now fails for any scheduled task the scan does not watch.
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
