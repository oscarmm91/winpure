# WinPure — Roadmap

Where this comes from: five reference debloaters were read end to end (winutil, Win11Debloat,
Sophia Script, Win-Debloat-Tools, SophiApp), producing **289 proposals**. After deduplication,
**285 unique candidates** were each verified against WinPure's source *and* against a real
Windows 11 Pro 25H2 machine (build 26200) — does WinPure already do it, does the key/feature
still exist, is it genuinely reversible.

| Verdict | Count | Meaning |
|---|---|---|
| **Include** | 42 | Clearly good, reversible, not already here |
| **Needs a product decision** | 96 | Valuable but carries a trade-off that is the owner's call |
| **Excluded** | 111 | Already here (31), obsolete, irreversible, or a security regression |
| **Unverified** | 36 | Ran out of verification budget — re-check before building |

## What we learned about the competition

All five references revert a tweak by writing a **"factory default" typed by hand into their
catalog** — the exact bug WinPure fixed on 2026-09-11. Win11Debloat has already started
migrating to real JSON snapshots, which is the road WinPure took. None of the five has a third
"could not detect" state, so a failed query reads as "already optimized". And **SophiApp — the
closest competitor, also a C# desktop app — has no snapshot and no restore page at all**.

That is the product's position, and everything below has to protect it:

1. **Every change is reversible from the value this machine really had.** Anything that cannot
   be undone lives in its own clearly-marked section, never inside a preset.
2. **Zero NuGet dependencies.** A good idea that needs a library needs a different design.

## Status

Phases 1, 2 and 3 are built, each followed by a separate commit applying its review — `git log
--oneline` is the record, not this paragraph. Phase 3 also turned up a bug this roadmap had not
listed: the power plan and hibernation tweaks were undone with hand-written defaults. Fixed as
Phase 3b (`SystemStateAction`), together with Reserved Storage. Still open from Phase 3: **Edge's
new-tab policies**, waiting on the product decision described there. From Phase 4, Windows
optional features are built; OEM bloatware is not. From Phase 5, export/import and the winget
installer are built. **Apply to future users** is waiting: loading the Default profile hive needs
administrator rights and cannot be tested from an unelevated session, and a mistake leaves new profiles
broken. **The "no way back" section** is waiting on a product decision: moving app removal out of the
presets changes what Balanced and Aggressive promise today. Phase 6 search and pending badges are built.

---

## Phase 1 — Fix what we already ship

Before adding anything. The Widgets tweak proved the risk is real: it sat in the **Safe** preset
and did nothing on current Windows, because Microsoft's UCPD driver blocks the value it wrote.

- **Audit the 21 tweaks that write under `\Policies\`.** Some may apply and then be quietly
  reverted by a group-policy refresh. SophiApp ships Microsoft's `LGPO.exe` to prevent exactly
  this; we need to measure whether it affects us before deciding we need anything.
- **Verify every hand-written default against a real machine.** Those defaults are a guess about
  a factory Windows, and they are what a revert falls back on when no backup exists.
- **Reconcile `WaitToKillAppTimeout`**: our value is better than the one proposed, but the
  conflict needs settling so the catalog stays coherent.
- **Guard: catalog vs `docs/tweaks.md` vs `README.md`.** Today they are synchronized by hand and
  by discipline. Make it a test, like the XAML guard.

## Phase 2 — Refuse to act on a broken system

Sophia runs 18 checks before touching anything; we run none. Each of these is a few lines and
prevents a class of damage we currently cannot even detect.

- Running elevated **as a different user** than the one at the keyboard — personal settings would
  land in the wrong profile.
- A **pending reboot** — Windows is mid-update and may undo the changes.
- **BitLocker actively encrypting or decrypting.**
- **Core UWP components missing** — another debloater already broke the app platform.
- **The EventLog service stopped** — state detection is blind without it.

## Phase 3 — New tweaks: high value, low risk, reversible

Around twenty, all verified as absent from our catalog and still valid on 25H2.

**Privacy and AI** — Click to Do (the 24H2 selection overlay), `Microsoft.Windows.AIHub` on
Copilot+ machines, Paint's five AI policies, Windows Spotlight ads on the desktop, search
highlights and search history, blocking web access to the system language list.

**System and everyday annoyances** — Fast Startup off (so "Shut down" really shuts down),
Reserved Storage off (**~7 GB back**), NumLock on at the sign-in screen, the context menu on more
than 15 selected files, the `ms-gamebar` popup when a controller is connected, the "What's new"
screens after each update, Xbox Game Bar overlay, daily registry backup, NTP time sync.

**Edge** — 13 policies that clean the new-tab page of news, shopping and campaigns. High visible
value; it is a product decision because it configures another vendor's app.

## Phase 4 — New categories

- **Windows optional features** (DISM). The one thing five of the ten reports independently
  called our biggest gap: PowerShell 2.0 (a real security liability), WordPad, XPS, Fax — and
  the other direction, turning **Sandbox and WSL on**. Needs a `FeatureAction` plus a
  `FeaturesQueryOk` flag, so a failed query reads as *unknown* and not as *already optimized* —
  the same mistake we already paid for once.
  **Built**, as the *Windows Features* page with eight tweaks. Measured on 25H2 build 26200:
  PowerShell 2.0, WordPad, Fax and Recall no longer exist there as optional features, so only
  PowerShell 2.0 was kept (for older builds, where it still ships).
- **OEM bloatware** (Dell, HP, Lenovo, Samsung). ~20 verified AppIds. The honest caveat: none of
  it can be tested here, and some of those apps deliver real driver updates, so they never go in
  a preset.
- ~~**Diagnostic scheduled tasks.**~~ Done in Phase 3 (`privacy-diagnostic-tasks`).

## Phase 5 — The big features you approved

- **Export / import configuration** to JSON — which tweaks you have on, to reapply on another
  machine. Different from Restore, which rolls back *this* machine. Importing must only tick the
  boxes; the user still presses Apply.
- **winget app installer**, in its own section. It is **not reversible** and it is not debloating,
  so it must never share a page or a preset with the reversible catalog.
- **Apply to future users** via the `Default` profile hive. Genuinely useful and genuinely
  dangerous: a badly unloaded hive can leave a profile unusable. Needs the same
  capture-before-touching discipline as the engine.
- **The "no way back" section** itself — the home for app removal, Edge removal and anything
  else that cannot be undone, with explicit confirmation and never pre-ticked.

## Phase 6 — Interface

- **Global search** across every tweak. With 100+ tweaks, category-by-category browsing stops
  working — SophiApp has this and we do not.
- **Pending-change badge per category** in the sidebar.
- **Grey out what does not apply to this machine, with the reason** — not silently hiding it.
  Careful: SophiApp disables the control outright, which conflicts with our deliberate decision
  that an undetectable tweak must stay applicable.
- **Per-app risk badge** (Safe / Optional / Risky) instead of hiding risk in a tooltip.

## Phase 7 — Spanish

Deferred on purpose: every feature above adds strings. `.resx` with a key convention per tweak
(`{Category}_{Name}_{Title}`) and English fallback, so a missing translation shows English
instead of an empty control. No dependency needed.

---

## Not doing, and why

- **Turning Windows Update off entirely.** Leaves the machine unpatched; and `WaaSMedicSvc`
  re-enables it by itself, so it does not even hold.
- **Force-removing Edge** with the fake-stub trick. Irreversible by construction.
- **Disabling UAC.** A security regression, whatever the performance argument.
- **Copying `powershell.exe` under another name** to defeat the UCPD driver (how Sophia writes
  `TaskbarDa`). It writes into System32 and can trip a user's antivirus. We use the supported
  policy instead — already done.
- **Sweeping OEM bloatware by publisher.** Measured: it would take the Realtek audio panel, the
  NVIDIA one and Intel's with it.
- **DNS provider switching** as the references do it. Their revert is "back to DHCP", which is
  not the same as "back to what you had" — it breaks rule 1. Reconsider only with a real capture
  per adapter.
- **File-association hash (`UserChoice`)** — reverse-engineered, fragile, and Microsoft has been
  actively closing it.
- **ViVeTool feature flags** — needs a third-party binary, and the source itself says it no
  longer works on current builds.

## Known gap

**ASUS bloatware.** None of the five references covers it — they only handle Dell and Samsung,
and only Store apps, while MyASUS and Armoury Crate are Win32 services and scheduled tasks.
Nothing to copy; it needs its own research. Worth noting the machine this was verified on is an
ASUS with no such bloat present, so it cannot be tested here either.
