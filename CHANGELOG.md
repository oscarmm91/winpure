# Changelog

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
