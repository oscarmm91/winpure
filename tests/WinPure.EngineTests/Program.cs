using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinPure.Models;
using WinPure.Services;

// Regression tests for the backup/revert engine — the part of WinPure that can leave a
// machine in a state its owner never chose. Run them with:
//
//     dotnet run --project tests\WinPure.EngineTests
//
// Exit code = number of failures, so CI fails on a red test.
//
// They touch nothing real: a toy registry key under HKCU\Software\WinPureTests and a
// scratch backup folder under %TEMP%. No Windows setting, service or task is involved,
// and they do not need administrator rights.
//
// All three go RED against the pre-2026-09-11 engine, which reverted tweaks to defaults
// hand-written in the catalog instead of to the value the machine actually had.

const string ToyKey = @"HKCU\Software\WinPureTests";
const string ToySub = @"Software\WinPureTests";

string scratch = Path.Combine(Path.GetTempPath(), "winpure-engine-tests");
BackupManager.BackupDirectory = scratch;
// Toy tweaks restore under ToyKey, which no real tweak touches; the app itself never sets this.
BackupEntryPolicy.TestKeyPrefix = ToyKey;

if (args.Length > 0 && args[0] == "crash-child")
{
    RunCrashChild();
    return 0;
}

if (args.Length > 1 && args[0] == "orphan-child")
{
    RunOrphanChild(args[1]);
    return 0;
}

int failures = 0;
Console.WriteLine("=== WinPure engine tests ===");
Console.WriteLine($"scratch backups: {scratch}");
Console.WriteLine();

failures += RevertRestoresThisMachinesValue() ? 0 : 1;
failures += RevertDeletesAValueThatNeverExisted() ? 0 : 1;
failures += SnapshotReachesDiskBeforeTheSystemIsTouched() ? 0 : 1;
failures += FailedAppScanIsUnknownNotOptimized() ? 0 : 1;
failures += FailedTaskScanIsUnknownNotOptimized() ? 0 : 1;
failures += RealScanOfThisMachineWorks() ? 0 : 1;
failures += CatalogIsInternallyConsistent() ? 0 : 1;
failures += ATimeoutReallyStopsAHungCommand() ? 0 : 1;
failures += CancellingReallyKillsTheProcess() ? 0 : 1;
failures += OnlySafeRepairsOfferCancel() ? 0 : 1;
failures += PresetKeepsManualSelections() ? 0 : 1;
failures += EveryStaticResourceAndBindingPathExists() ? 0 : 1;
failures += TogglingAStartupEntryIsByteExact() ? 0 : 1;
failures += DisablingAnUntouchedEntryIsUndoneByDeleting() ? 0 : 1;
failures += TheStartupScannerFindsWhatWindowsHas() ? 0 : 1;
failures += AnOptionalActionCannotFailTheWholeTweak() ? 0 : 1;
failures += TheDocsAgreeWithTheCatalog() ? 0 : 1;
failures += AFailedBackupIsNeverExcusedAsAnOptionalAction() ? 0 : 1;
failures += DifferentUserGuardFiresOnlyOnARealMismatch() ? 0 : 1;
failures += RebootGuardFiresOnAnySingleSignal() ? 0 : 1;
failures += BitLockerGuardFiresOnlyMidOperation() ? 0 : 1;
failures += CoreAppsGuardIgnoresAFailedListing() ? 0 : 1;
failures += EventLogGuardFiresOnlyWhenStopped() ? 0 : 1;
failures += GuardInputsReadThisMachineCorrectly() ? 0 : 1;
failures += CoreAppsGuardKnowsLtscAndPartialListings() ? 0 : 1;
failures += GatherNeverThrowsWhenTheBitLockerQueryFails() ? 0 : 1;
failures += EachActionAsksOnlyTheGuardsThatConcernIt() ? 0 : 1;
failures += AnAbsentValueCanAlreadyBeTheWantedState() ? 0 : 1;
failures += AQueryDiesWithTheAppButASystemChangeDoesNot() ? 0 : 1;
failures += AHungAccountLookupCannotStallTheScan() ? 0 : 1;
failures += EveryTweakHasAnIcon() ? 0 : 1;
failures += RevertingAPowerPlanRestoresTheOneThatWasActive() ? 0 : 1;
failures += RevertingHibernationKeepsItOffIfItWasOff() ? 0 : 1;
failures += ATamperedSettingInABackupIsRefused() ? 0 : 1;
failures += EveryScheduledTaskIsWatched() ? 0 : 1;
failures += AFailedFeatureScanIsUnknownNotOptimized() ? 0 : 1;
failures += RevertingAFeatureRestoresItsRecordedState() ? 0 : 1;
failures += ATamperedFeatureInABackupIsRefused() ? 0 : 1;
failures += AConfigFileRoundTripsAndRejectsForeignFiles() ? 0 : 1;
failures += ImportingTicksButNeverUnticksOrApplies() ? 0 : 1;
failures += SearchFindsTweaksInEveryCategory() ? 0 : 1;
failures += EachCategoryShowsItsPendingCount() ? 0 : 1;
failures += AForgedBackupCannotReachBeyondWhatWinPureChanges() ? 0 : 1;
failures += PowerShellQuotingDoublesEveryQuote() ? 0 : 1;
failures += BackupsLiveWhereOnlyAdministratorsCanWrite() ? 0 : 1;
failures += OldBackupsAreCopiedOnceAndLeftInPlace() ? 0 : 1;
failures += WingetIdsAreCheckedAndTheExportIsReadExactly() ? 0 : 1;
failures += AnInstallIsJudgedByWhatWingetSeesAfterwards() ? 0 : 1;
ReportCatalogDeadWeightOnThisMachine();
ReportPolicyWritesNotBackedByAnAdmx();

Cleanup();
Console.WriteLine();
Console.WriteLine(failures == 0 ? "All green." : $"{failures} test(s) FAILED.");
return failures;

// ---------------------------------------------------------------- tests

// The machine already had the value at 7 — set by group policy, another debloater, or a
// different Windows build. Applying and then toggling the tweak off must give 7 back,
// not whatever the catalog calls the "stock default".
bool RevertRestoresThisMachinesValue()
{
    Reset();
    WriteToy("Tailored", 7);

    var tweak = ToyTweak("test-revert-real-value", applyValue: 0, defaultValue: 1, valueName: "Tailored");
    var engine = new TweakEngine(new BackupManager());

    engine.ApplyChanges(new[] { (tweak, true) });
    int? afterApply = ReadToy("Tailored");

    engine.ApplyChanges(new[] { (tweak, false) });
    int? afterRevert = ReadToy("Tailored");

    return Report("revert restores the value this machine had",
        afterRevert == 7,
        $"was 7, applied={afterApply}, reverted={afterRevert} (expected 7; the old engine wrote 1)");
}

// The other half: a value that was absent must be DELETED on revert, not written with
// the catalog's default — which would leave a setting the user never had.
bool RevertDeletesAValueThatNeverExisted()
{
    Reset(); // note: no WriteToy — the value does not exist

    var tweak = ToyTweak("test-revert-absent-value", applyValue: 1, defaultValue: 1, valueName: "Ghost");
    var engine = new TweakEngine(new BackupManager());

    engine.ApplyChanges(new[] { (tweak, true) });
    int? afterApply = ReadToy("Ghost");

    engine.ApplyChanges(new[] { (tweak, false) });
    int? afterRevert = ReadToy("Ghost");

    return Report("revert deletes a value that never existed",
        afterRevert is null,
        $"applied={afterApply}, reverted={(afterRevert?.ToString() ?? "(absent)")} (expected absent; the old engine left 1)");
}

// A Settings toggle that is off by default is only written once someone turns it on, so a
// stock machine has no value at all. For that tweak "missing" must read as applied — and
// every other tweak must keep reading a missing value as not applied.
bool AnAbsentValueCanAlreadyBeTheWantedState()
{
    Reset(); // the value does not exist

    var offByDefault = new RegistryValueAction { KeyPath = ToyKey, ValueName = "OptedIn", ApplyValue = 0, AbsentMeansApplied = true };
    var ordinary = new RegistryValueAction { KeyPath = ToyKey, ValueName = "OptedIn", ApplyValue = 0 };
    var ctx = new ScanContext();

    bool? absentFlagged = offByDefault.IsApplied(ctx);
    bool? absentOrdinary = ordinary.IsApplied(ctx);
    WriteToy("OptedIn", 1);
    bool? turnedOn = offByDefault.IsApplied(ctx);

    return Report("a value that is off by default can already count as applied",
        absentFlagged == true && absentOrdinary == false && turnedOn == false,
        $"absent with flag={absentFlagged} (expected True), absent without={absentOrdinary} (expected False), turned on={turnedOn} (expected False)");
}

// The snapshot must be on disk before the system is modified — simulated the way it
// actually happens to a user: the process dies mid-batch (force close, crash, power cut).
bool SnapshotReachesDiskBeforeTheSystemIsTouched()
{
    Reset();
    WriteToy("Crash", 42);

    var psi = new ProcessStartInfo(Environment.ProcessPath!, "crash-child") { UseShellExecute = false };
    using (var child = Process.Start(psi)!) child.WaitForExit(120_000);

    int? valueNow = ReadToy("Crash");
    bool backedUp = AnyBackupMentions("test-crash-midbatch", "42");

    return Report("the snapshot reaches disk before the system is touched",
        backedUp,
        $"value after the crash={valueNow}, original 42 found in a backup on disk={backedUp} " +
        "(the old engine saved nothing until the whole batch had finished)");
}

// A scan whose app listing failed hands back an empty package set. That must read as
// "could not check", never as "no bloatware installed" — which showed the user
// "Already optimized" over a machine still full of the apps they wanted gone.
bool FailedAppScanIsUnknownNotOptimized()
{
    var tweak = new Tweak
    {
        Id = "test-appx",
        Category = TweakCategory.Apps,
        Name = "Remove something",
        Description = "toy",
        Icon = "",
        Actions = new TweakAction[] { new AppxRemoveAction { PackagePatterns = new[] { "Microsoft.Whatever" } } },
    };

    var broken = new ScanContext { Loaded = true, AppsQueryOk = false, TasksQueryOk = true };
    var working = new ScanContext { Loaded = true, AppsQueryOk = true, TasksQueryOk = true };
    var engine = new TweakEngine(new BackupManager());

    var whenBroken = engine.GetStatus(tweak, broken);
    var whenWorking = engine.GetStatus(tweak, working);

    return Report("a failed app scan reads as Unknown, not Optimized",
        whenBroken == TweakStatus.Unknown && whenWorking == TweakStatus.Optimized,
        $"scan failed => {whenBroken} (expected Unknown; the old code said Optimized), " +
        $"scan ok and package absent => {whenWorking} (expected Optimized)");
}

// Same for scheduled tasks: a task missing from a query that blew up is not a disabled task.
bool FailedTaskScanIsUnknownNotOptimized()
{
    var tweak = new Tweak
    {
        Id = "test-task",
        Category = TweakCategory.Privacy,
        Name = "Disable a task",
        Description = "toy",
        Icon = "",
        Actions = new TweakAction[]
        {
            new ScheduledTaskAction { TaskPath = @"\Microsoft\Windows\Nonexistent\Task" }
        },
    };

    var broken = new ScanContext { Loaded = true, AppsQueryOk = true, TasksQueryOk = false };
    var working = new ScanContext { Loaded = true, AppsQueryOk = true, TasksQueryOk = true };
    var engine = new TweakEngine(new BackupManager());

    var whenBroken = engine.GetStatus(tweak, broken);
    var whenWorking = engine.GetStatus(tweak, working);

    return Report("a failed task scan reads as Unknown, not Optimized",
        whenBroken == TweakStatus.Unknown && whenWorking == TweakStatus.Optimized,
        $"scan failed => {whenBroken} (expected Unknown; the old code said Optimized), " +
        $"scan ok and task absent => {whenWorking} (expected Optimized)");
}

// Smoke test on the machine actually running this: the real scan must come back loaded.
// The per-section results are printed rather than asserted — a CI runner is allowed to
// have a locked-down AppX stack, and that is exactly the case the two tests above cover.
bool RealScanOfThisMachineWorks()
{
    var ctx = ScanContext.Gather();
    string warnings = ctx.Warnings.Count == 0 ? "none" : string.Join("; ", ctx.Warnings);
    return Report("the real scan of this machine completes",
        ctx.Loaded,
        $"loaded={ctx.Loaded} apps={ctx.InstalledPackages.Count} (ok={ctx.AppsQueryOk}) " +
        $"watchedTasks={ctx.TaskEnabled.Count} (ok={ctx.TasksQueryOk}) warnings: {warnings}");
}

// The old runner drained stdout with ReadToEnd() before waiting on the timeout, so a command
// that never finished blocked the reader forever and the timeout was never reached — the app
// hung for as long as the child did. A 60-second sleep with a 2-second timeout must come back
// in about two seconds, not sixty.
bool ATimeoutReallyStopsAHungCommand()
{
    var clock = Stopwatch.StartNew();
    var result = PowerShellRunner.Run("Start-Sleep -Seconds 60", timeoutMs: 2_000);
    clock.Stop();

    bool ok = result.TimedOut && clock.Elapsed < TimeSpan.FromSeconds(20);
    return Report("a timeout really stops a hung command",
        ok,
        $"asked for 2s, returned after {clock.Elapsed.TotalSeconds:0.0}s, timedOut={result.TimedOut} " +
        "(the old runner waited the full 60s because ReadToEnd blocked first)");
}

// The Cancel button on the Repair page has to actually reach the process.
bool CancellingReallyKillsTheProcess()
{
    using var cts = new CancellationTokenSource();
    var clock = Stopwatch.StartNew();
    var task = Task.Run(() => PowerShellRunner.Run("Start-Sleep -Seconds 60", timeoutMs: 120_000, cts.Token));
    Thread.Sleep(1_000);
    cts.Cancel();
    var result = task.GetAwaiter().GetResult();
    clock.Stop();

    bool ok = result.Cancelled && clock.Elapsed < TimeSpan.FromSeconds(20);
    return Report("cancelling a repair really kills the process",
        ok,
        $"cancelled after 1s, returned after {clock.Elapsed.TotalSeconds:0.0}s, cancelled={result.Cancelled}");
}

// Killing SFC/DISM midway can leave the component store inconsistent, so those must not
// offer a Cancel button at all — the elapsed-time readout is what reassures the user there.
bool OnlySafeRepairsOfferCancel()
{
    var tools = RepairCatalog.Build();
    var systemFiles = tools.FirstOrDefault(t => t.Id == "repair-system-files");
    var others = tools.Where(t => t.Id != "repair-system-files").ToList();

    bool ok = systemFiles is { Cancellable: false } && others.Count > 0 && others.All(t => t.Cancellable);
    return Report("only repairs that are safe to kill offer Cancel",
        ok,
        $"repair-system-files cancellable={systemFiles?.Cancellable.ToString() ?? "(missing)"}, " +
        $"the other {others.Count} tools cancellable={others.All(t => t.Cancellable)}");
}

// ---------------------------------------------------------------- pre-apply guards
// Each guard is judged against made-up facts, so it can be tested without a real machine in a
// bad state. Every test checks both directions: a guard that fires on a healthy system trains
// users to click "Continue" without reading, which is worse than having no guard at all.

static GuardInputs HealthyInputs() => new()
{
    SessionUser = @"PC\alice",
    ProcessUser = @"PC\alice",
    SessionSid = "S-1-5-21-1-1001",
    ProcessSid = "S-1-5-21-1-1001",
    PendingRebootSignals = Array.Empty<string>(),
    BitLockerStatus = "FullyDecrypted",
    EventLogStatus = "Running",
    AppsQueryOk = true,
    AppsListedForAllUsers = true,
    EditionIsLtsc = false,
    InstalledPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Microsoft.WindowsStore", "MicrosoftWindows.Client.CBS" },
};

static bool Fires(GuardInputs facts, string id) => SystemGuards.Evaluate(facts).Any(w => w.Id == id);

bool DifferentUserGuardFiresOnlyOnARealMismatch()
{
    var h = HealthyInputs();
    bool healthyIsSilent = SystemGuards.Evaluate(h).Count == 0;
    bool otherAccount = Fires(h with { ProcessUser = @"PC\Administrator", ProcessSid = "S-1-5-21-1-500" },
        SystemGuards.DifferentUserId);
    // The same person, named differently by the two APIs — what a Microsoft or Entra account can
    // look like. Comparing names would fire here for every such user; comparing SIDs must not.
    bool sameSidOtherName = Fires(h with { SessionUser = "alice", ProcessUser = @"MicrosoftAccount\alice@outlook.com" },
        SystemGuards.DifferentUserId);
    bool unknown = Fires(h with { SessionSid = null }, SystemGuards.DifferentUserId);

    return Report("the different-user guard fires only on a real mismatch",
        healthyIsSilent && otherAccount && !sameSidOtherName && !unknown,
        $"healthy system silent={healthyIsSilent}, other account fires={otherAccount}, " +
        $"same account with differently formatted names silent={!sameSidOtherName}, unreadable session silent={!unknown}");
}

// Sophia's version needs all five keys at once ([Array]::TrueForAll) and so never fires.
bool RebootGuardFiresOnAnySingleSignal()
{
    var h = HealthyInputs();
    bool one = Fires(h with { PendingRebootSignals = new[] { "Windows Update needs a restart" } }, "reboot-pending");
    bool none = Fires(h, "reboot-pending");

    return Report("the reboot guard fires on any single pending signal",
        one && !none,
        $"one signal fires={one} (a require-all-five version never would), no signal silent={!none}");
}

bool BitLockerGuardFiresOnlyMidOperation()
{
    var h = HealthyInputs();
    bool encrypting = Fires(h with { BitLockerStatus = "EncryptionInProgress" }, "bitlocker-busy");
    bool paused = Fires(h with { BitLockerStatus = "DecryptionPaused" }, "bitlocker-busy");
    bool encrypted = Fires(h with { BitLockerStatus = "FullyEncrypted" }, "bitlocker-busy");
    bool unknown = Fires(h with { BitLockerStatus = null }, "bitlocker-busy");

    return Report("the BitLocker guard fires only while the drive is mid-operation",
        encrypting && paused && !encrypted && !unknown,
        $"encrypting fires={encrypting}, paused fires={paused}, fully encrypted silent={!encrypted}, " +
        $"unreadable (Home, or no elevation) silent={!unknown}");
}

// The same trap the scan fell into once: an empty list from a failed query is not evidence.
bool CoreAppsGuardIgnoresAFailedListing()
{
    var h = HealthyInputs();
    var empty = new HashSet<string>();
    bool failedListing = Fires(h with { AppsQueryOk = false, InstalledPackages = empty }, "core-apps-missing");
    bool reallyMissing = Fires(h with { InstalledPackages = empty }, "core-apps-missing");
    bool present = Fires(h, "core-apps-missing");

    return Report("the core-apps guard ignores a failed app listing",
        !failedListing && reallyMissing && !present,
        $"failed listing silent={!failedListing}, really missing fires={reallyMissing}, both present silent={!present}");
}

bool EventLogGuardFiresOnlyWhenStopped()
{
    var h = HealthyInputs();
    bool stopped = Fires(h with { EventLogStatus = "Stopped" }, "eventlog-stopped");
    bool unknown = Fires(h with { EventLogStatus = null }, "eventlog-stopped");
    bool running = Fires(h, "eventlog-stopped");

    return Report("the Event Log guard fires only when the service is stopped",
        stopped && !unknown && !running,
        $"stopped fires={stopped}, running silent={!running}, unreadable silent={!unknown}");
}

// The different-user guard compares two names that come from two different APIs: the session
// user from wtsapi32 and the process identity from .NET. If their formats disagreed — "alice"
// against "PC\alice" — the guard would fire on EVERY machine for every user. On the machine
// running the tests, signed in as the same account that runs them, the two must be identical.
bool GuardInputsReadThisMachineCorrectly()
{
    var facts = GuardInputs.Gather(ScanContext.Gather());
    var warnings = SystemGuards.Evaluate(facts);

    bool sameAccount = facts.SessionSid is null
        || facts.SessionSid.Equals(facts.ProcessSid, StringComparison.OrdinalIgnoreCase);

    Console.WriteLine();
    Console.WriteLine("Pre-apply guards on this machine (informational):");
    Console.WriteLine($"  session user={facts.SessionUser ?? "(unreadable)"} [{facts.SessionSid ?? "no SID"}]");
    Console.WriteLine($"  process user={facts.ProcessUser ?? "(unreadable)"} [{facts.ProcessSid ?? "no SID"}]");
    Console.WriteLine($"  BitLocker={facts.BitLockerStatus ?? "(unreadable)"}  EventLog={facts.EventLogStatus ?? "(unreadable)"}  " +
                      $"pending-restart signals={facts.PendingRebootSignals.Count}  LTSC={facts.EditionIsLtsc}  " +
                      $"apps listed for all users={facts.AppsListedForAllUsers}");
    Console.WriteLine(warnings.Count == 0 ? "  no warnings" : string.Join(Environment.NewLine, warnings.Select(w => $"  [{w.Severity}] {w.Title}: {w.Detail}")));

    return Report("guard inputs resolve the signed-in user to the same account",
        sameAccount,
        $"session SID '{facts.SessionSid}' vs process SID '{facts.ProcessSid}' — signed in and running as the same " +
        "account, they must match, or the different-user guard would fire for everyone");
}

// LTSC ships without the Store on purpose, and the current-user fallback listing is not the
// machine's list. Neither may produce a "missing components" warning.
bool CoreAppsGuardKnowsLtscAndPartialListings()
{
    var h = HealthyInputs();
    var shellOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MicrosoftWindows.Client.CBS" };
    var nothing = new HashSet<string>();

    bool ltscWithoutStore = Fires(h with { EditionIsLtsc = true, InstalledPackages = shellOnly }, SystemGuards.CoreAppsMissingId);
    bool ltscWithoutShell = Fires(h with { EditionIsLtsc = true, InstalledPackages = nothing }, SystemGuards.CoreAppsMissingId);
    bool proWithoutStore = Fires(h with { InstalledPackages = shellOnly }, SystemGuards.CoreAppsMissingId);
    bool userOnlyListing = Fires(h with { AppsListedForAllUsers = false, InstalledPackages = nothing }, SystemGuards.CoreAppsMissingId);

    return Report("the core-apps guard knows LTSC and partial listings",
        !ltscWithoutStore && ltscWithoutShell && proWithoutStore && !userOnlyListing,
        $"LTSC without Store silent={!ltscWithoutStore}, LTSC without shell fires={ltscWithoutShell}, " +
        $"Pro without Store fires={proWithoutStore}, current-user-only listing silent={!userOnlyListing}");
}

// GuardInputs.Gather promises never to throw, and the scan that calls it has no catch: a failure
// there would take the whole scan down. The BitLocker query is the one input that arrives as a
// task, and a faulted task rethrows on .Result.
bool GatherNeverThrowsWhenTheBitLockerQueryFails()
{
    var ctx = new ScanContext { Loaded = true, AppsQueryOk = true, TasksQueryOk = true };
    try
    {
        var facts = GuardInputs.Gather(ctx, Task.FromException<string?>(new InvalidOperationException("simulated WMI failure")));
        return Report("gathering guard facts survives a failed BitLocker query",
            facts.BitLockerStatus is null,
            $"returned normally with BitLocker status={facts.BitLockerStatus ?? "(unknown)"} (expected unknown)");
    }
    catch (Exception ex)
    {
        return Report("gathering guard facts survives a failed BitLocker query", false,
            $"threw {ex.GetType().Name}: {ex.Message} — in the app this would take the whole scan down");
    }
}

// A guard shown where it does not apply is noise, and noise teaches people to stop reading.
bool EachActionAsksOnlyTheGuardsThatConcernIt()
{
    var everythingWrong = HealthyInputs() with
    {
        ProcessUser = @"PC\Administrator",
        ProcessSid = "S-1-5-21-1-500",
        PendingRebootSignals = new[] { "Windows Update needs a restart" },
        BitLockerStatus = "EncryptionInProgress",
        InstalledPackages = new HashSet<string>(),
        EventLogStatus = "Stopped",
    };
    var all = SystemGuards.Evaluate(everythingWrong);

    static string Ids(IEnumerable<GuardWarning> g) => string.Join(",", g.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal));
    string apply = Ids(SystemGuards.ForApply(all));
    string restore = Ids(SystemGuards.ForRestore(all));
    string startup = Ids(SystemGuards.ForStartup(all));
    string repair = Ids(SystemGuards.ForRepair(all));
    string expectedRepair = Ids(all.Where(w => w.Id is SystemGuards.DifferentUserId or SystemGuards.RebootPendingId or SystemGuards.BitLockerBusyId));

    bool ok = all.Count == 5 && SystemGuards.ForApply(all).Count == 5
        && restore == SystemGuards.DifferentUserId
        && startup == SystemGuards.DifferentUserId
        && repair == expectedRepair && SystemGuards.ForRepair(all).Count == 3;

    return Report("each action asks only about the guards that concern it", ok,
        $"all five fired={all.Count == 5}; apply=[{apply}]; restore=[{restore}]; startup=[{startup}]; repair=[{repair}]");
}

// Closing WinPure mid-scan used to leave powershell.exe running, with the timeout that should
// have stopped it gone along with the app. A child test process starts a PowerShell and is then
// killed WITHOUT its process tree — exactly what happens when the app dies — and the PowerShell
// must not survive it.
// Two halves, because the first version got the second one wrong. A read-only query must die with
// the app (closing WinPure mid-scan used to leave powershell.exe running). A command that changes
// the system must NOT: the job also kills what PowerShell started, so an uninstall or a service
// stop cut in half would leave the machine worse off than letting it finish.
bool AQueryDiesWithTheAppButASystemChangeDoesNot()
{
    const string name = "a query dies with the app, a system change does not";
    string pidFile = Path.Combine(Path.GetTempPath(), $"winpure-orphan-{Environment.ProcessId}.pid");
    string jobFile = pidFile + ".job";
    string keepFile = pidFile + ".keep";
    void CleanFiles() { foreach (var f in new[] { pidFile, jobFile, keepFile }) { try { File.Delete(f); } catch { } } }
    CleanFiles();

    var psi = new ProcessStartInfo(Environment.ProcessPath!, $"orphan-child \"{pidFile}\"") { UseShellExecute = false };
    using var child = Process.Start(psi)!;

    int queryPid = 0, changePid = 0;
    string job = "";
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(90) && (queryPid == 0 || changePid == 0 || job.Length == 0))
    {
        if (queryPid == 0) queryPid = ReadPid(pidFile);
        if (changePid == 0) changePid = ReadPid(keepFile);
        try { if (job.Length == 0 && File.Exists(jobFile)) job = File.ReadAllText(jobFile).Trim(); } catch { }
        Thread.Sleep(250);
    }

    if (queryPid == 0 || changePid == 0 || job.Length == 0)
    {
        try { child.Kill(entireProcessTree: true); } catch { }
        CleanFiles();
        return Report(name, false,
            $"the child never reported both PowerShells within 90 s (query pid={queryPid}, change pid={changePid}, job='{job}')");
    }

    child.Kill();                     // the app dies — deliberately NOT its process tree
    child.WaitForExit(10_000);
    Thread.Sleep(3_000);

    bool queryAlive = StillPowerShell(queryPid);
    bool changeAlive = StillPowerShell(changePid);
    CleanFiles();

    // A CI runner may refuse nested job objects; locally that would be a real regression.
    if (job != "True" && Environment.GetEnvironmentVariable("CI") == "true")
        return Report(name, true, "skipped on CI: this environment would not place the child in a job object");

    return Report(name, !queryAlive && changeAlive,
        $"placed in the kill-on-close job={job}; after the app was killed: query alive={queryAlive} (expected False), " +
        $"system change alive={changeAlive} (expected True)");
}

// Alive AND still powershell.exe — a PID can be handed to an unrelated process within seconds.
// Whatever is still running is ended here, so the test leaves nothing behind.
static bool StillPowerShell(int pid)
{
    try
    {
        using var p = Process.GetProcessById(pid);
        bool alive = !p.HasExited && p.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase);
        if (alive) p.Kill();
        return alive;
    }
    catch { return false; }           // no such process
}

static int ReadPid(string file)
{
    try { return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out int pid) ? pid : 0; }
    catch { return 0; }
}

void RunOrphanChild(string pidFile)
{
    // A read-only query, the one kind that asks to die with the app.
    Task.Run(() => PowerShellRunner.Run(
        $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 120", timeoutMs: 180_000, dieWithApp: true));

    // PowerShell writes its PID only after it was started and placed in the job, so by then the
    // assignment result is final. The second call below never touches the job, so it cannot change it.
    var clock = Stopwatch.StartNew();
    while (!File.Exists(pidFile) && clock.Elapsed < TimeSpan.FromSeconds(90)) Thread.Sleep(100);
    File.WriteAllText(pidFile + ".job", PowerShellRunner.LastJobAssignOk.ToString());

    // The default, which is what every command that changes the system uses.
    Task.Run(() => PowerShellRunner.Run(
        $"Set-Content -LiteralPath '{pidFile}.keep' -Value $PID; Start-Sleep -Seconds 120", timeoutMs: 180_000));
    Thread.Sleep(Timeout.Infinite);
}

// Translating the signed-in account to a SID runs inside the scan, which has no Cancel button, and
// for a domain account away from the office network it may wait on a domain controller that is not
// there. It gets its own deadline, and an unanswered lookup reads as "could not tell".
bool AHungAccountLookupCannotStallTheScan()
{
    var clock = Stopwatch.StartNew();
    string? hung = GuardInputs.ResolveSid(@"CORP\someone", TimeSpan.FromMilliseconds(500),
        _ => { Thread.Sleep(10_000); return "S-1-5-21-1"; });
    double seconds = clock.Elapsed.TotalSeconds;
    string? quick = GuardInputs.ResolveSid(@"CORP\someone", TimeSpan.FromSeconds(3), _ => "S-1-5-21-1");

    return Report("a hung account lookup cannot stall the scan",
        hung is null && seconds < 3 && quick == "S-1-5-21-1",
        $"hung lookup returned {hung ?? "null"} after {seconds:0.0}s (expected null in under 3s), quick lookup returned {quick ?? "null"}");
}

// An empty Icon compiles fine and shows a blank square on the tweak's card. Eight tweaks — the ones
// ported from WinUtil — sat like that, because nothing checked.
bool EveryTweakHasAnIcon()
{
    var blank = TweakCatalog.Build()
        .Where(t => t.Icon.Length != 1 || t.Icon[0] < 0xE700 || t.Icon[0] > 0xF8FF)
        .Select(t => t.Id)
        .ToList();
    return Report("every tweak has an icon", blank.Count == 0,
        blank.Count == 0 ? "every tweak has one Segoe Fluent glyph" : $"no glyph: {string.Join(", ", blank)}");
}

// Settings changed through a command used to be undone with a hand-written "stock" command:
// undoing High Performance switched a custom plan to Balanced. Undo must put back the plan that
// was really active.
bool RevertingAPowerPlanRestoresTheOneThatWasActive()
{
    Reset();
    const string custom = "24b961a2-1b54-42c5-b443-20ec5ec4cfee";
    const string high = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    var plan = new FakeSetting(custom, new PowerPlanState());
    var real = SystemState.Swap(SystemStateKind.PowerPlan, plan);
    try
    {
        var tweak = StateTweak("test-power-plan",
            new SystemStateAction { Kind = SystemStateKind.PowerPlan, AppliedState = high, DefaultState = balanced });
        var engine = new TweakEngine(new BackupManager());
        engine.ApplyChanges(new[] { (tweak, true) });
        string afterApply = plan.Current;
        engine.ApplyChanges(new[] { (tweak, false) });

        return Report("reverting a power plan restores the one that was active",
            afterApply == high && plan.Current == custom,
            $"was {Short(custom)} (custom), applied={Short(afterApply)}, reverted={Short(plan.Current)} " +
            $"(expected {Short(custom)}; the old action switched to Balanced {Short(balanced)})");
    }
    finally { SystemState.Swap(SystemStateKind.PowerPlan, real); }
}

// The same bug from the other side: undoing Disable Hibernation turned hibernation ON, even on a
// PC where it had been off before WinPure ever touched it.
bool RevertingHibernationKeepsItOffIfItWasOff()
{
    Reset();
    var hibernation = new FakeSetting("off", new HibernationState());
    var real = SystemState.Swap(SystemStateKind.Hibernation, hibernation);
    try
    {
        var tweak = StateTweak("test-hibernation",
            new SystemStateAction { Kind = SystemStateKind.Hibernation, AppliedState = "off", DefaultState = "on" });
        var engine = new TweakEngine(new BackupManager());
        engine.ApplyChanges(new[] { (tweak, true) });
        engine.ApplyChanges(new[] { (tweak, false) });

        return Report("reverting hibernation keeps it off if it was off",
            hibernation.Current == "off" && !hibernation.Writes.Contains("on"),
            $"was off, writes=[{string.Join(",", hibernation.Writes)}], now={hibernation.Current} " +
            "(expected off and never on; the old action ran powercfg /hibernate on)");
    }
    finally { SystemState.Swap(SystemStateKind.Hibernation, real); }
}

// Backups are plain JSON in the user's own profile, and WinPure runs elevated. A setting read back
// from one must never decide what runs: it is checked against what could really have been recorded.
bool ATamperedSettingInABackupIsRefused()
{
    var plan = new FakeSetting("381b4222-f694-41f0-9685-ff5bb260df2e", new PowerPlanState());
    var real = SystemState.Swap(SystemStateKind.PowerPlan, plan);
    try
    {
        var tampered = new BackupEntry
        {
            Type = "system-state", TweakId = "test-tamper", ValueName = "PowerPlan", Existed = true,
            Value = "381b4222-f694-41f0-9685-ff5bb260df2e; Remove-Item $env:TEMP -Recurse",
        };
        var unknownSetting = new BackupEntry
        {
            Type = "system-state", TweakId = "test-tamper", ValueName = "SomethingElse", Existed = true, Value = "on",
        };
        int failures = new BackupManager().RestoreEntries(new[] { tampered, unknownSetting });

        return Report("a tampered setting in a backup is refused",
            failures == 2 && plan.Writes.Count == 0,
            $"failures={failures} (expected 2), writes that reached the setting={plan.Writes.Count} (expected 0)");
    }
    finally { SystemState.Swap(SystemStateKind.PowerPlan, real); }
}

Tweak StateTweak(string id, SystemStateAction action) => new()
{
    Id = id,
    Category = TweakCategory.Performance,
    Name = $"Test tweak {id}",
    Description = "toy tweak, tests only",
    Icon = "x",
    Actions = new TweakAction[] { action },
};

static string Short(string guid) => guid.Length > 8 ? guid[..8] : guid;

// A ScheduledTaskAction whose task is missing from ScanContext.WatchedTasks reads as applied forever:
// the scan never asked about it. Compatibility Appraiser Exp sat like that for a day after being
// added, while the changelog said it was covered.
bool EveryScheduledTaskIsWatched()
{
    var watched = new HashSet<string>(ScanContext.WatchedTasks, StringComparer.OrdinalIgnoreCase);
    var missing = TweakCatalog.Build()
        .SelectMany(t => t.Actions.OfType<ScheduledTaskAction>().Select(a => (t.Id, a.TaskPath)))
        .Where(x => !watched.Contains(x.TaskPath))
        .Select(x => $"{x.Id}: {x.TaskPath}")
        .ToList();

    return Report("every scheduled task a tweak changes is watched by the scan",
        missing.Count == 0,
        missing.Count == 0 ? $"{watched.Count} tasks watched, none missing" : "not watched: " + string.Join(" | ", missing));
}

// Same lesson as apps and tasks: a feature query that failed must read Unknown, never "already
// off". A feature this build does not include has nothing left to turn off — and cannot be turned on.
bool AFailedFeatureScanIsUnknownNotOptimized()
{
    var turnOff = new FeatureAction { FeatureName = "Printing-XPSServices-Features", DefaultEnabled = true };
    var turnOffAbsent = new FeatureAction { FeatureName = "MicrosoftWindowsPowerShellV2", DefaultEnabled = true };
    var turnOnAbsent = new FeatureAction { FeatureName = "Containers-DisposableClientVM", Enable = true, DefaultEnabled = false };

    var failed = new ScanContext { Loaded = true, FeaturesQueryOk = false };
    var ok = new ScanContext { Loaded = true, FeaturesQueryOk = true };
    ok.FeatureState["Printing-XPSServices-Features"] = 1; // enabled: turning it off is still to do

    bool? whenFailed = turnOff.IsApplied(failed);
    bool? whenEnabled = turnOff.IsApplied(ok);
    bool? absentOff = turnOffAbsent.IsApplied(ok);
    bool? absentOn = turnOnAbsent.IsApplied(ok);

    return Report("a failed feature scan reads as Unknown, not Optimized",
        whenFailed is null && whenEnabled == false && absentOff == true && absentOn is null,
        $"query failed => {whenFailed?.ToString() ?? "null"} (expected null), still enabled => {whenEnabled?.ToString() ?? "null"} (expected False), " +
        $"absent + turn off => {absentOff?.ToString() ?? "null"} (expected True), absent + turn on => {absentOn?.ToString() ?? "null"} (expected null)");
}

// A feature this PC already had off stays off after undo, even when the hand-written default says
// Windows ships it on. The recorded state wins, as for every other kind of action.
bool RevertingAFeatureRestoresItsRecordedState()
{
    Reset();
    const string xps = "Printing-XPSServices-Features";
    var features = new FakeFeatures();
    features.States[xps] = OptionalFeatures.Disabled;
    var real = OptionalFeatures.Backend;
    OptionalFeatures.Backend = features;
    try
    {
        var tweak = ControlTweak("test-feature", new FeatureAction { FeatureName = xps, DefaultEnabled = true });
        var engine = new TweakEngine(new BackupManager());
        engine.ApplyChanges(new[] { (tweak, true) });
        engine.ApplyChanges(new[] { (tweak, false) });

        return Report("reverting a feature restores the state it had",
            features.States[xps] == OptionalFeatures.Disabled && !features.Writes.Contains(xps + "=on"),
            $"was Disabled, writes=[{string.Join(",", features.Writes)}], now={features.States[xps]} " +
            "(expected Disabled and never turned on; without a recorded state the default would turn it on)");
    }
    finally { OptionalFeatures.Backend = real; }
}

// A feature name read back from a backup ends up inside a DISM command line, so it is checked first.
bool ATamperedFeatureInABackupIsRefused()
{
    var features = new FakeFeatures();
    var real = OptionalFeatures.Backend;
    OptionalFeatures.Backend = features;
    try
    {
        var badName = new BackupEntry
        {
            Type = "optional-feature", TweakId = "test-tamper", Existed = true,
            ValueName = "NetFx3' ; Remove-Item $env:TEMP -Recurse ; '", Value = "Enabled",
        };
        var badState = new BackupEntry
        {
            Type = "optional-feature", TweakId = "test-tamper", Existed = true,
            ValueName = "NetFx3", Value = "Enabled; shutdown /s",
        };
        int failures = new BackupManager().RestoreEntries(new[] { badName, badState });

        return Report("a tampered feature in a backup is refused",
            failures == 2 && features.Writes.Count == 0,
            $"failures={failures} (expected 2), writes that reached DISM={features.Writes.Count} (expected 0)");
    }
    finally { OptionalFeatures.Backend = real; }
}

// A configuration file can come from anyone, so everything that is not a WinPure configuration this
// version can read is refused with a message, and ids this version does not know are set aside.
bool AConfigFileRoundTripsAndRejectsForeignFiles()
{
    var catalog = new[] { "privacy-telemetry", "ui-dark-mode", "apps-xbox" };
    var problems = new List<string>();

    var back = ConfigFile.Parse(ConfigFile.Serialize(new[] { "ui-dark-mode", "privacy-telemetry", "from-a-newer-version" }, "1.2.0"), catalog);
    if (!back.KnownIds.OrderBy(i => i).SequenceEqual(new[] { "privacy-telemetry", "ui-dark-mode" }))
        problems.Add($"round trip gave [{string.Join(",", back.KnownIds)}]");
    if (!back.UnknownIds.SequenceEqual(new[] { "from-a-newer-version" }))
        problems.Add($"unknown ids were [{string.Join(",", back.UnknownIds)}]");

    var foreign = new (string what, string json)[]
    {
        ("not JSON", "this is not json"),
        ("another app's file", """{"App":"SomethingElse","Format":1,"Tweaks":["ui-dark-mode"]}"""),
        ("no app name", """{"Format":1,"Tweaks":["ui-dark-mode"]}"""),
        ("a future format", """{"App":"WinPure","Format":2,"Tweaks":["ui-dark-mode"]}"""),
        ("a wrong type", """{"App":"WinPure","Format":"one"}"""),
        ("a huge file", "{\"App\":\"WinPure\",\"Format\":1,\"Tweaks\":[\"" + new string('a', ConfigFile.MaxChars) + "\"]}"),
    };
    foreach (var (what, json) in foreign)
    {
        try
        {
            ConfigFile.Parse(json, catalog);
            problems.Add($"{what} was accepted");
        }
        catch (InvalidDataException) { }
        catch (Exception ex) { problems.Add($"{what} threw {ex.GetType().Name} instead of a readable refusal"); }
    }

    return Report("a configuration file round-trips and foreign files are refused",
        problems.Count == 0,
        problems.Count == 0 ? $"round trip exact, 1 unknown id set aside, {foreign.Length} kinds of foreign file refused" : string.Join(" | ", problems));
}

// Importing ticks boxes and nothing else. Unticking what the file does not list would schedule a
// revert on the next Apply, and applying on import would take the decision away from the user.
bool ImportingTicksButNeverUnticksOrApplies()
{
    Reset();
    var main = new WinPure.ViewModels.MainViewModel();
    var handNotInFile = main.AllTweaks[0];   // ticked by hand, absent from the file: must stay ticked
    var handInFile = main.AllTweaks[1];      // ticked by hand and listed: counts as already on
    var inFile = main.AllTweaks[2];          // listed and not ticked: gets ticked
    handNotInFile.IsSelected = true;
    handInFile.IsSelected = true;

    int backupsBefore = Directory.Exists(scratch) ? Directory.GetFiles(scratch).Length : 0;
    var (ticked, alreadyOn, unknown) = main.ImportConfig(
        ConfigFile.Serialize(new[] { inFile.Tweak.Id, handInFile.Tweak.Id, "not-in-this-version" }, "1.2.0"));
    int backupsAfter = Directory.Exists(scratch) ? Directory.GetFiles(scratch).Length : 0;

    bool untouchedStayOff = main.AllTweaks.Skip(3).All(t => !t.IsSelected);
    return Report("importing ticks boxes but never unticks or applies",
        inFile.IsSelected && handNotInFile.IsSelected && handInFile.IsSelected && untouchedStayOff &&
        ticked == 1 && alreadyOn == 1 && unknown == 1 && backupsAfter == backupsBefore,
        $"listed tweak ticked={inFile.IsSelected}, hand-ticked but not listed still ticked={handNotInFile.IsSelected} (expected True), " +
        $"nothing else ticked={untouchedStayOff}, counts ticked/alreadyOn/unknown={ticked}/{alreadyOn}/{unknown} (expected 1/1/1), " +
        $"backups written={backupsAfter - backupsBefore} (expected 0)");
}

// Search looks in every category, replaces the page while it has text, and shows the same tweaks
// as their own pages: ticking a result is the same toggle. Clearing it goes back where you were.
bool SearchFindsTweaksInEveryCategory()
{
    var main = new WinPure.ViewModels.MainViewModel();
    var start = main.CurrentPage;

    main.SearchText = "GAME";
    var page = main.CurrentPage as WinPure.ViewModels.CategoryPageViewModel;
    var results = page?.Tweaks.ToList() ?? new List<WinPure.ViewModels.TweakViewModel>();
    bool allMatch = results.Count > 0 && results.All(t =>
        (t.Name + " " + t.Description + " " + t.Tweak.Help).Contains("game", StringComparison.OrdinalIgnoreCase));
    bool narrowed = results.Count < main.AllTweaks.Count;
    int categories = results.Select(t => t.Category).Distinct().Count();

    if (results.Count > 0) results[0].IsSelected = !results[0].IsSelected;
    bool sameToggle = main.PendingCount == 1;

    main.SearchText = "";
    bool backToStart = ReferenceEquals(main.CurrentPage, start);

    return Report("search finds tweaks in every category",
        page is not null && allMatch && narrowed && categories >= 2 && sameToggle && backToStart,
        $"'GAME' => {results.Count} of {main.AllTweaks.Count} tweaks from {categories} categories, all mention it={allMatch}, " +
        $"ticking a result counts as pending={sameToggle}, cleared search returns to the start page={backToStart}");
}

// Each category in the sidebar shows how many of its tweaks are waiting for Apply Changes.
bool EachCategoryShowsItsPendingCount()
{
    var main = new WinPure.ViewModels.MainViewModel();
    WinPure.ViewModels.NavItem Nav(TweakCategory category) => main.NavItems.First(n =>
        n.Page is WinPure.ViewModels.CategoryPageViewModel p && p.Category == category);

    var privacy = main.AllTweaks.Where(t => t.Category == TweakCategory.Privacy).Take(2).ToList();
    foreach (var tweak in privacy) tweak.IsSelected = true;
    main.AllTweaks.First(t => t.Category == TweakCategory.Performance).IsSelected = true;
    int privacyCount = Nav(TweakCategory.Privacy).PendingCount;
    int performanceCount = Nav(TweakCategory.Performance).PendingCount;
    int appsCount = Nav(TweakCategory.Apps).PendingCount;

    privacy[0].IsSelected = false;
    int privacyAfterUntick = Nav(TweakCategory.Privacy).PendingCount;

    return Report("each category shows its pending count",
        privacyCount == 2 && performanceCount == 1 && appsCount == 0 && privacyAfterUntick == 1 && Nav(TweakCategory.Privacy).HasPending,
        $"privacy={privacyCount} (expected 2), performance={performanceCount} (expected 1), apps={appsCount} (expected 0), " +
        $"privacy after unticking one={privacyAfterUntick} (expected 1)");
}

// Backups are plain JSON that any program can edit without elevation, and WinPure restores them
// elevated. A review found service names and task paths going straight into PowerShell, and registry
// paths restored wherever the file pointed. An entry may now only touch what WinPure changes itself.
bool AForgedBackupCannotReachBeyondWhatWinPureChanges()
{
    const string forgedKey = @"HKCU\Software\WinPureForgedTest";
    const string forgedSub = @"Software\WinPureForgedTest";
    try { Registry.CurrentUser.DeleteSubKeyTree(forgedSub, throwOnMissingSubKey: false); } catch { }

    var forged = new[]
    {
        new BackupEntry { Type = "registry-value", TweakId = "forged", KeyPath = forgedKey, ValueName = "Userinit", Kind = "String", Value = "evil.exe", Existed = true },
        new BackupEntry { Type = "registry-key", TweakId = "forged", KeyPath = forgedKey + @"\Created", Existed = true, Value = "x" },
        new BackupEntry { Type = "startup-entry", TweakId = "forged", KeyPath = forgedKey, ValueName = "NotASwitch", Value = "020000000000000000000000", Existed = true },
        new BackupEntry { Type = "service", TweakId = "forged", ServiceName = "Spooler'; exit 0; '", StartMode = 3 },
        new BackupEntry { Type = "scheduled-task", TweakId = "forged", TaskPath = @"\Vendor\Task'; exit 0; '", TaskWasEnabled = true, Existed = true },
    };
    int failures = new BackupManager().RestoreEntries(forged);

    bool touched;
    using (var key = Registry.CurrentUser.OpenSubKey(forgedSub)) touched = key is not null;
    try { Registry.CurrentUser.DeleteSubKeyTree(forgedSub, throwOnMissingSubKey: false); } catch { }

    // The second review's entries use paths and names the catalog really has, with forged data. They are
    // checked against the policy itself: restoring them unelevated would fail anyway, and hide a broken policy.
    var catalogService = TweakCatalog.Build().SelectMany(t => t.Actions).OfType<ServiceAction>().First().ServiceName;
    var catalogHandler = TweakCatalog.Build().SelectMany(t => t.Actions).OfType<RegistryKeyAction>().First(k => k.KeyDefaultValue is { Length: > 0 });
    var forgedData = new[]
    {
        new BackupEntry { Type = "service", TweakId = "forged", ServiceName = catalogService, StartMode = 999 },
        new BackupEntry { Type = "registry-key", TweakId = "forged", KeyPath = catalogHandler.KeyPath, Existed = true, Value = "{11111111-2222-3333-4444-555555555555}" },
        new BackupEntry { Type = "startup-entry", TweakId = "forged", KeyPath = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\StartupApproved\Run", ValueName = "X", Value = "020000000000000000000000", Existed = true },
        new BackupEntry { Type = "scheduled-task", TweakId = "forged", TaskPath = @"\\Microsoft\Windows\Feedback\Siuf\DmClient", Existed = true, TaskWasEnabled = true },
    };
    var slipped = forgedData.Where(e => BackupEntryPolicy.IsAllowed(e, out _)).Select(e => e.Type).ToList();
    failures += forgedData.Length - slipped.Count;   // counted as refused only when the policy said no

    // What WinPure really writes must still restore.
    var real = TweakCatalog.Build().SelectMany(t => t.Actions).OfType<RegistryValueAction>().First();
    bool genuineAllowed =
        BackupEntryPolicy.IsAllowed(new BackupEntry { Type = "registry-value", TweakId = "t", KeyPath = real.KeyPath, ValueName = real.ValueName }, out _) &&
        BackupEntryPolicy.IsAllowed(new BackupEntry
        {
            Type = "startup-entry", TweakId = "t", ValueName = "Discord", Value = "030000000000000000000000", Existed = true,
            KeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        }, out _) &&
        BackupEntryPolicy.IsAllowed(new BackupEntry { Type = "scheduled-task", TweakId = "t", TaskPath = @"\GoogleUpdateTaskMachineUA{1B2C3D4E}", Existed = true }, out _);

    int total = forged.Length + forgedData.Length;
    return Report("a forged backup cannot reach beyond what WinPure changes",
        failures == total && slipped.Count == 0 && !touched && genuineAllowed,
        $"refused {failures} of {total} forged entries (expected all; slipped past the policy: [{string.Join(",", slipped)}]), " +
        $"forged registry key created={touched} (expected False), genuine catalog value, Startup switch and third-party logon task still allowed={genuineAllowed}");
}

// The real defence against forged backups: only SYSTEM and Administrators can write the folder, and the
// owner cannot rewrite its permissions. Checked on a real folder from this unelevated process, which is
// exactly the kind of program that must not be able to plant a backup. The probe folder stays behind in
// %TEMP%: once locked, an unelevated test can no longer delete it, which is the point.
bool BackupsLiveWhereOnlyAdministratorsCanWrite()
{
    var problems = new List<string>();

    if (!BackupStore.IsProtected(BackupStore.ProtectedSecurity()))
        problems.Add("the permissions WinPure sets are not judged protected");
    var open = new System.Security.AccessControl.DirectorySecurity();
    open.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
        new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
        System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
    if (BackupStore.IsProtected(open)) problems.Add("a folder any user can modify is judged protected");
    if (!BackupStore.DefaultDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase))
        problems.Add($"backups default to {BackupStore.DefaultDirectory}, not under ProgramData");

    string probe = Path.Combine(Path.GetTempPath(), "winpure-acl-probe");
    var info = new DirectoryInfo(probe);
    if (info.Exists)
    {
        try { info.Delete(recursive: true); info.Refresh(); } catch { /* locked by an earlier run: reuse it */ }
    }
    if (!info.Exists) info.Create(BackupStore.ProtectedSecurity());

    bool wrote = false, rewrotePermissions = false;
    try { File.WriteAllText(Path.Combine(probe, "backup_planted.json"), "{}"); wrote = true; }
    catch (UnauthorizedAccessException) { }
    try
    {
        var sec = new DirectoryInfo(probe).GetAccessControl();
        sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(probe).SetAccessControl(sec);
        rewrotePermissions = true;
    }
    catch (UnauthorizedAccessException) { }
    catch (InvalidOperationException) { }
    if (wrote) problems.Add("an unelevated process wrote a file into the protected folder");
    if (rewrotePermissions) problems.Add("an unelevated owner granted itself access to the protected folder");

    return Report("backups live where only administrators can write",
        problems.Count == 0,
        problems.Count == 0
            ? "ProgramData by default; this unelevated test could neither write into a protected folder nor change its permissions"
            : string.Join(" | ", problems));
}

// Backups from before the move live in the user's AppData. They are copied into the protected folder
// once — never moved, so nothing is lost if anything goes wrong — and not copied again on the next run.
bool OldBackupsAreCopiedOnceAndLeftInPlace()
{
    string root = Path.Combine(scratch, "migration");
    string legacy = Path.Combine(root, "legacy"), target = Path.Combine(root, "target");
    try { Directory.Delete(root, recursive: true); } catch { }
    Directory.CreateDirectory(legacy);
    Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(legacy, "backup_20260612_064833.json"), "{\"Id\":\"a\"}");
    File.WriteAllText(Path.Combine(legacy, "backup_20260912_150221.json"), "{\"Id\":\"b\"}");
    File.WriteAllText(Path.Combine(legacy, "notes.txt"), "not a backup");

    int first = BackupStore.MigrateLegacy(legacy, target);
    File.WriteAllText(Path.Combine(legacy, "backup_20260913_000000.json"), "{\"Id\":\"c\"}");
    int second = BackupStore.MigrateLegacy(legacy, target);

    bool originalsKept = Directory.GetFiles(legacy, "backup_*.json").Length == 3;
    bool copiedExactly = File.ReadAllText(Path.Combine(target, "backup_20260612_064833.json")) == "{\"Id\":\"a\"}";
    int inTarget = Directory.GetFiles(target, "backup_*.json").Length;
    try { Directory.Delete(root, recursive: true); } catch { }

    return Report("old backups are copied once and left in place",
        first == 2 && second == 0 && originalsKept && copiedExactly && inTarget == 2,
        $"first run copied {first} (expected 2), second run {second} (expected 0), originals kept={originalsKept}, content identical={copiedExactly}, backups in the new folder={inTarget} (expected 2)");
}

// Every service and task name reaches PowerShell inside single quotes. PowerShell treats the typographic
// single quotes as quotes too, so all of them must be doubled, or a name can close the string early.
bool PowerShellQuotingDoublesEveryQuote()
{
    char q = (char)39, left = (char)0x2018, right = (char)0x2019;
    string name = "it" + q + "s " + left + "x" + right;
    string expected = q + "it" + q + q + "s " + left + left + "x" + right + right + q;
    string actual = PowerShellRunner.Quote(name);
    return Report("PowerShell quoting doubles every kind of quote",
        actual == expected,
        $"quoted length {actual.Length} (expected {expected.Length}), exact={actual == expected}");
}

// Installed apps are read from winget's JSON export, whose ids are the same in every language, and
// compared ignoring case: with --exact, "Voidtools.Everything" missed an app that was installed. A package
// id only reaches winget's command line if it looks like one.
bool WingetIdsAreCheckedAndTheExportIsReadExactly()
{
    var problems = new List<string>();
    var catalog = AppInstallerCatalog.Build();
    foreach (var app in catalog)
        if (!Winget.IsValidId(app.Id)) problems.Add($"catalog id '{app.Id}' is rejected");
    if (catalog.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Count)
        problems.Add("the catalog lists an id twice");
    foreach (var bad in new[] { "Git.Git'; Remove-Item $env:TEMP -Recurse; '", "Git Git", "", "-h", ".Git", "Git.Git;calc" })
        if (Winget.IsValidId(bad)) problems.Add($"'{bad}' is accepted");

    var ids = Winget.ParseExport("""{"Sources":[{"Packages":[{"PackageIdentifier":"voidtools.Everything"},{"PackageIdentifier":"Git.Git"},{"Version":"1"}]},{"Packages":null}]}""");
    if (!(ids.Count == 2 && ids.Contains("Voidtools.Everything") && ids.Contains("Git.Git")))
        problems.Add($"export read as [{string.Join(",", ids)}]");

    return Report("winget ids are checked and the export is read exactly",
        problems.Count == 0,
        problems.Count == 0 ? $"{catalog.Count} catalog ids valid and unique, 6 malformed ids refused, export ids read ignoring case" : string.Join(" | ", problems));
}

// An installer can report success and leave nothing, or fail after installing. The card follows what
// winget sees afterwards, never the exit code alone — and the page never runs a real winget in a test.
bool AnInstallIsJudgedByWhatWingetSeesAfterwards()
{
    var fake = new FakeWinget();
    var real = Winget.Backend;
    Winget.Backend = fake;
    try
    {
        var main = new WinPure.ViewModels.MainViewModel();
        var page = main.NavItems.Select(n => n.Page).OfType<WinPure.ViewModels.InstallerViewModel>().Single();
        page.RefreshAsync().GetAwaiter().GetResult();
        var git = page.Apps.First(a => a.App.Id == "Git.Git");
        var vlc = page.Apps.First(a => a.App.Id == "VideoLAN.VLC");
        bool detected = git.IsInstalled && !vlc.IsInstalled && page.HasChecked;

        page.InstallConfirmedAsync(vlc).GetAwaiter().GetResult();            // exit code 0, nothing installed
        bool successThatInstalledNothing = !vlc.IsInstalled && vlc.StatusText.StartsWith("Not installed", StringComparison.Ordinal);

        fake.ReallyInstalls.Add("VideoLAN.VLC");
        fake.ExitCodes["VideoLAN.VLC"] = unchecked((int)0x8A150011);       // a failure code, yet the app arrives
        page.InstallConfirmedAsync(vlc).GetAwaiter().GetResult();
        bool failureThatInstalled = vlc.IsInstalled && vlc.StatusText.StartsWith("Installed", StringComparison.Ordinal);

        return Report("an install is judged by what winget sees afterwards",
            detected && successThatInstalledNothing && failureThatInstalled && fake.Installs.Count == 2 && !main.IsBusy,
            $"detected before={detected}, 'success' with nothing installed shown as not installed={successThatInstalledNothing}, " +
            $"'failure' that installed shown as installed={failureThatInstalled}, installs asked={fake.Installs.Count} (expected 2), busy afterwards={main.IsBusy}");
    }
    finally { Winget.Backend = real; }
}

// The forgiveness granted to an optional action must never extend to the backup itself.
// Both failures arrive as an exception on the same action, and the first version of this
// feature could not tell them apart: with the snapshot flush inside the guard, a full disk
// or a locked backup folder read as "Windows rejected this legacy value", and the tweak
// reported success having written nothing and saved nothing.
bool AFailedBackupIsNeverExcusedAsAnOptionalAction()
{
    Reset();
    string goodDir = BackupManager.BackupDirectory;

    // Point the backup directory at a path that cannot be created: an existing FILE.
    string blocker = Path.Combine(Path.GetTempPath(), "winpure-backup-blocker");
    File.WriteAllText(blocker, "not a directory");
    BackupManager.BackupDirectory = Path.Combine(blocker, "backups");

    var tweak = new Tweak
    {
        Id = "test-backup-fails",
        Category = TweakCategory.UI,
        Name = "Tweak whose backup cannot be written",
        Description = "toy",
        Icon = "",
        Actions = new TweakAction[]
        {
            new RegistryValueAction
            {
                KeyPath = @"HKCU\Software\WinPureTests", ValueName = "ShouldNotBeWritten",
                Kind = RegistryValueKind.DWord, ApplyValue = 1, DefaultValue = null,
                Optional = true,
            },
        },
    };

    var result = new TweakEngine(new BackupManager()).ApplyChanges(new[] { (tweak, true) })[0];
    int? written = ReadToy("ShouldNotBeWritten");

    BackupManager.BackupDirectory = goodDir;
    try { File.Delete(blocker); } catch { }
    Reset();

    return Report("a failed backup is never excused as an optional action",
        !result.Success && written is null,
        $"reported success={result.Success} (expected False), value written anyway={written is not null} " +
        $"(expected False). Message: '{result.Message}'");
}

// A tweak lives in three places: the catalog, the table in docs/tweaks.md and the per-category
// count in README.md. Keeping them in step by hand did not work — the README advertised 16
// Privacy tweaks while the catalog had 18, and nobody noticed for weeks, because the earlier
// check only ever compared the catalog against tweaks.md.
bool TheDocsAgreeWithTheCatalog()
{
    string root = FindRepoRoot();
    if (root.Length == 0)
        return Report("the docs agree with the catalog", true, "skipped: repo not next to the test binary");

    var catalog = TweakCatalog.Build()
        .GroupBy(t => t.Category)
        .ToDictionary(g => g.Key, g => g.Count());

    string tweaksMd = File.ReadAllText(Path.Combine(root, "docs", "tweaks.md"));
    string readme = File.ReadAllText(Path.Combine(root, "README.md"));

    // docs/tweaks.md: one data row per tweak, per section (Repair has its own section).
    var mdRows = new Dictionary<string, int>();
    foreach (var section in Regex.Split(tweaksMd, @"\n## ").Skip(1))
    {
        string title = section.Split('\n')[0];
        int rows = section.Split('\n').Count(l =>
            l.StartsWith("| ") && !l.Contains("---") &&
            !l.StartsWith("| Tweak") && !l.StartsWith("| Tool"));
        mdRows[title] = rows;
    }

    var problems = new List<string>();
    // Category name as it appears in the docs → the enum it maps to.
    var titles = new (string Doc, TweakCategory Cat)[]
    {
        ("Privacy & Telemetry", TweakCategory.Privacy),
        ("Bloatware & Apps", TweakCategory.Apps),
        ("Services", TweakCategory.Services),
        ("Performance", TweakCategory.Performance),
        ("UI & Personalization", TweakCategory.UI),
        ("Context Menu", TweakCategory.ContextMenu),
        ("Windows Features", TweakCategory.Features),
    };

    foreach (var (doc, cat) in titles)
    {
        int inCatalog = catalog.TryGetValue(cat, out int n) ? n : 0;

        var mdKey = mdRows.Keys.FirstOrDefault(k => k.Contains(doc, StringComparison.Ordinal));
        if (mdKey is null) problems.Add($"docs/tweaks.md has no '{doc}' section");
        else if (mdRows[mdKey] != inCatalog)
            problems.Add($"{doc}: catalog {inCatalog} vs tweaks.md {mdRows[mdKey]}");

        var m = Regex.Match(readme, @"<b>" + Regex.Escape(doc) + @"</b>[^0-9]*(\d+) tweaks");
        if (!m.Success) problems.Add($"README has no count for '{doc}'");
        else if (int.Parse(m.Groups[1].Value) != inCatalog)
            problems.Add($"{doc}: catalog {inCatalog} vs README {m.Groups[1].Value}");
    }

    // Counts alone let a wrong preset or a renamed tweak through: a review injected both into
    // tweaks.md and this test stayed green. So every row's name and preset are compared as well.
    var allTweaks = TweakCatalog.Build();
    var sections = Regex.Split(tweaksMd, @"\n## ").Skip(1).ToList();
    foreach (var (doc, cat) in titles)
    {
        var section = sections.FirstOrDefault(s => s.Split('\n')[0].Contains(doc, StringComparison.Ordinal));
        if (section is null) continue;
        var inDocs = section.Split('\n')
            .Where(l => l.StartsWith("| ") && !l.Contains("---") && !l.StartsWith("| Tweak"))
            .Select(l => l.Split('|'))
            .Where(cells => cells.Length > 3)
            .Select(cells => (Name: cells[1].Trim(), Preset: cells[2].Trim()))
            .ToList();
        var inCatalog = allTweaks.Where(t => t.Category == cat)
            .Select(t => (Name: t.Name, Preset: t.Preset.ToString()))
            .ToList();
        foreach (var t in inCatalog.Except(inDocs))
            problems.Add($"{doc}: the catalog has '{t.Name}' ({t.Preset}) and tweaks.md does not");
        foreach (var t in inDocs.Except(inCatalog))
            problems.Add($"{doc}: tweaks.md has '{t.Name}' ({t.Preset}) and the catalog does not");
    }

    return Report("the docs agree with the catalog",
        problems.Count == 0,
        problems.Count == 0
            ? $"{catalog.Values.Sum()} tweaks: every category count, and every tweak's name and preset, match in the catalog, docs/tweaks.md and README.md"
            : string.Join(" | ", problems));
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
            Directory.Exists(Path.Combine(dir.FullName, "src", "WinPure")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return "";
}

// Some tweaks carry a legacy fallback that current Windows refuses to write — the Widgets
// button is the real case: the UCPD driver blocks TaskbarDa on 24H2+, while the Dsh policy
// still works. A blocked legacy value must not fail the tweak, and must not keep it pinned
// to "Not applied" once the part that matters has been applied.
bool AnOptionalActionCannotFailTheWholeTweak()
{
    Reset();
    const string toyKey = @"HKCU\Software\WinPureTests";

    var tweak = new Tweak
    {
        Id = "test-optional",
        Category = TweakCategory.UI,
        Name = "Tweak with a legacy fallback",
        Description = "toy",
        Icon = "",
        Actions = new TweakAction[]
        {
            // The one that matters — a normal, writable value.
            new RegistryValueAction
            {
                KeyPath = toyKey, ValueName = "Real",
                Kind = RegistryValueKind.DWord, ApplyValue = 1, DefaultValue = null,
            },
            // The legacy one: HKLM\SECURITY is unwritable even elevated, standing in for
            // a value Windows refuses. Marked Optional, so it must be survivable.
            new RegistryValueAction
            {
                KeyPath = @"HKLM\SECURITY\WinPureTests", ValueName = "Blocked",
                Kind = RegistryValueKind.DWord, ApplyValue = 1, DefaultValue = null,
                Optional = true,
            },
        },
    };

    var engine = new TweakEngine(new BackupManager());
    var result = engine.ApplyChanges(new[] { (tweak, true) })[0];
    int? realValue = ReadToy("Real");
    var status = engine.GetStatus(tweak, new ScanContext { Loaded = true, AppsQueryOk = true, TasksQueryOk = true });

    Reset();
    return Report("a blocked legacy action cannot fail the whole tweak",
        result.Success && realValue == 1 && status == TweakStatus.Optimized,
        $"tweak reported success={result.Success} (message: '{result.Message}'), " +
        $"the value that matters was written={realValue == 1}, status={status} (expected Optimized)");
}

// Clicking a preset used to silently untick every Manual tweak the user had chosen by hand,
// with nothing on screen to say so: the click felt like it undid your work.
bool PresetKeepsManualSelections()
{
    var main = new WinPure.ViewModels.MainViewModel();
    var manual = main.AllTweaks.First(t => t.Preset == PresetLevel.Manual && !t.IsOptimized);
    manual.IsSelected = true;

    main.SelectPreset(PresetLevel.Safe);

    bool kept = manual.IsSelected;
    bool announced = main.StatusText.Contains("manual selection", StringComparison.OrdinalIgnoreCase);
    bool presetRecorded = main.ActivePreset == PresetLevel.Safe;

    return Report("choosing a preset keeps what you ticked by hand",
        kept && announced && presetRecorded,
        $"'{manual.Name}' still selected={kept}, status mentions it={announced}, " +
        $"ActivePreset={main.ActivePreset} (the old code cleared it without a word)");
}

// Startup entries are toggled through a 12-byte value Microsoft never documented, where only
// the first byte is the on/off state and the rest is Windows' own timestamp. Turning an entry
// off and undoing it has to give back those bytes EXACTLY — a plausible reconstruction would
// quietly rewrite state that was never ours.
// The toy mirror key below keeps this off the real startup entries of whoever runs the tests.
bool TogglingAStartupEntryIsByteExact()
{
    const string approved = @"HKCU\Software\WinPureTests\StartupApproved\Run";
    Reset();

    // Enabled, and previously touched from Task Manager, so it carries a timestamp.
    byte[] original = { 0x02, 0, 0, 0, 0xFC, 0x75, 0xCE, 0x86, 0xB4, 0x38, 0xDC, 0x01 };
    WriteApproved(approved, "ToyApp", original);

    var tweak = StartupTweak("startup-toy-1", approved, "ToyApp");
    var backups = new BackupManager();
    var engine = new TweakEngine(backups);

    engine.ApplyChanges(new[] { (tweak, true) });      // turn the entry OFF
    byte[]? afterDisable = StartupScanner.ReadApproved(approved, "ToyApp");
    bool enabledAfterDisable = StartupScanner.IsEnabled(approved, "ToyApp");

    // Undo it the way the Restore page does.
    var session = backups.ListSessions().First(s => s.Entries.Any(e => e.TweakId == "startup-toy-1"));
    backups.RestoreSession(session);
    byte[]? afterRestore = StartupScanner.ReadApproved(approved, "ToyApp");

    bool timestampKept = afterDisable is { Length: 12 } && afterDisable.Skip(4).SequenceEqual(original.Skip(4));
    bool exact = afterRestore is not null && afterRestore.SequenceEqual(original);

    Reset();
    return Report("turning a startup entry off and back is byte-exact",
        !enabledAfterDisable && timestampKept && exact,
        $"disabled={!enabledAfterDisable}, Windows' timestamp kept while off={timestampKept}, " +
        $"restored bytes identical={exact} ({(afterRestore is null ? "(deleted)" : Convert.ToHexString(afterRestore))} vs {Convert.ToHexString(original)})");
}

// An entry nobody ever toggled has no value in the mirror key at all — and that absence is
// exactly what Windows reads as "enabled". Undoing must delete it again, not leave an
// invented "enabled" value behind.
bool DisablingAnUntouchedEntryIsUndoneByDeleting()
{
    const string approved = @"HKCU\Software\WinPureTests\StartupApproved\Run";
    Reset();   // no value written: this is a fresh entry an installer just created

    bool enabledBefore = StartupScanner.IsEnabled(approved, "FreshApp");

    var tweak = StartupTweak("startup-toy-2", approved, "FreshApp");
    var backups = new BackupManager();
    var engine = new TweakEngine(backups);

    engine.ApplyChanges(new[] { (tweak, true) });
    byte[]? afterDisable = StartupScanner.ReadApproved(approved, "FreshApp");

    var session = backups.ListSessions().First(s => s.Entries.Any(e => e.TweakId == "startup-toy-2"));
    backups.RestoreSession(session);
    byte[]? afterRestore = StartupScanner.ReadApproved(approved, "FreshApp");

    Reset();
    return Report("disabling an untouched entry is undone by deleting it",
        enabledBefore && afterDisable is not null && afterRestore is null,
        $"no value means enabled={enabledBefore}, value created when disabled={afterDisable is not null}, " +
        $"value gone again after restore={afterRestore is null}");
}

// Reports what the scanner actually sees on this machine, and asserts the parts that must
// hold anywhere: per-user Run entries exist, and every entry knows how to toggle itself.
bool TheStartupScannerFindsWhatWindowsHas()
{
    var ctx = ScanContext.Gather();
    var entries = StartupScanner.Scan(ctx);

    var bySource = entries.GroupBy(e => e.SourceLabel)
        .Select(g => $"{g.Key}: {g.Count()} ({g.Count(e => e.Enabled)} on)")
        .ToList();

    bool everyEntryToggleable = entries.All(e => e.ApprovedKeyPath is not null || e.TaskPath is not null);
    bool foundSomething = entries.Any(e => e.Source == StartupSource.RegistryRun);

    Console.WriteLine();
    Console.WriteLine("Startup entries found on this machine:");
    foreach (var e in entries)
        Console.WriteLine($"  [{(e.Enabled ? "on " : "OFF")}] {e.SourceLabel,-15} {e.Scope,-18} {e.Name}" +
                          (e.Publisher.Length > 0 ? $"  ({e.Publisher})" : "") + (e.IsOrphan ? "  <- file is gone" : ""));

    return Report("the startup scanner finds what Windows has",
        foundSomething && everyEntryToggleable,
        $"{entries.Count} entries — {string.Join(", ", bySource)}; every one knows how to toggle itself={everyEntryToggleable}");
}

Tweak StartupTweak(string id, string approvedKeyPath, string entryName) => new()
{
    Id = id,
    Category = TweakCategory.Apps,
    Name = $"Disable {entryName}",
    Description = "toy startup entry, tests only",
    Icon = "",
    Actions = new TweakAction[]
    {
        new StartupEntryAction { ApprovedKeyPath = approvedKeyPath, EntryName = entryName }
    },
};

void WriteApproved(string keyPath, string valueName, byte[] bytes)
{
    string sub = keyPath[(keyPath.IndexOf('\\') + 1)..];
    using var key = Registry.CurrentUser.CreateSubKey(sub, writable: true)!;
    key.SetValue(valueName, bytes, RegistryValueKind.Binary);
}

// A WPF binding with a typo fails silently at runtime: no crash, no message, the control just
// sits there empty. These two checks catch the two ways that happens in this project —
// a {StaticResource} key that nobody defines, and a binding to a property that does not exist.
bool EveryStaticResourceAndBindingPathExists()
{
    string srcDir = FindSourceDir();
    if (srcDir.Length == 0)
        return Report("every StaticResource key and binding path exists", true,
            "skipped: the source tree is not next to the test binary (packaged run)");

    var xamlFiles = Directory.GetFiles(srcDir, "*.xaml", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .ToList();

    var defined = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in xamlFiles)
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"x:Key=""([^""]+)"""))
            defined.Add(m.Groups[1].Value);

    var problems = new List<string>();
    foreach (var file in xamlFiles)
    {
        string text = File.ReadAllText(file);
        string name = Path.GetFileName(file);

        foreach (Match m in Regex.Matches(text, @"\{StaticResource\s+([^}\s,]+)\s*\}"))
            if (!defined.Contains(m.Groups[1].Value))
                problems.Add($"{name}: undefined resource '{m.Groups[1].Value}'");

        // Binding paths that go through the page's Main view-model.
        foreach (Match m in Regex.Matches(text, @"Binding\s+(?:Path=)?Main\.([A-Za-z_][A-Za-z0-9_]*)"))
        {
            string member = m.Groups[1].Value;
            if (typeof(WinPure.ViewModels.MainViewModel).GetProperty(member) is null)
                problems.Add($"{name}: MainViewModel has no '{member}'");
        }
    }

    // Defined somewhere is not enough. A dictionary merged into App.xaml can only see its own keys and
    // those of dictionaries merged before it, not App.xaml's own resources, and the failure comes at
    // startup rather than at build time. The sidebar badge asked Styles.xaml for BoolToVisibility,
    // declared in App.xaml, and the app died on launch while this test was green.
    string appXaml = Path.Combine(srcDir, "App.xaml");
    if (File.Exists(appXaml))
    {
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match merged in Regex.Matches(File.ReadAllText(appXaml), @"ResourceDictionary\s+Source=""([^""]+)"""))
        {
            string source = merged.Groups[1].Value;
            string path = Path.Combine(srcDir, source.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                problems.Add($"App.xaml merges a dictionary that does not exist: '{source}'");
                continue;
            }
            string text = File.ReadAllText(path);
            foreach (Match k in Regex.Matches(text, @"x:Key=""([^""]+)""")) visible.Add(k.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"\{StaticResource\s+([^}\s,]+)\s*\}"))
                if (!visible.Contains(m.Groups[1].Value))
                    problems.Add($"{Path.GetFileName(path)}: '{m.Groups[1].Value}' is not defined in it or in a dictionary merged before it, so the app fails when it starts");
        }
    }

    return Report("every StaticResource key and binding path exists",
        problems.Count == 0,
        problems.Count == 0
            ? $"{xamlFiles.Count} XAML files, {defined.Count} resource keys, all references resolve"
            : string.Join(" | ", problems));
}

static string FindSourceDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine(dir.FullName, "src", "WinPure");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return "";
}

// Catalog sanity that does not depend on which machine this runs on: duplicate ids, registry
// paths with a hive nobody can parse, and two tweaks writing different data to the same value.
bool CatalogIsInternallyConsistent()
{
    var tweaks = TweakCatalog.Build();
    var problems = new List<string>();

    foreach (var group in tweaks.GroupBy(t => t.Id).Where(g => g.Count() > 1))
        problems.Add($"duplicate id '{group.Key}'");

    // A tweak whose every action is optional can report success while doing nothing at all,
    // and then read as Pending on the next scan. Optional is for a fallback beside something
    // that really works, never for the whole tweak.
    foreach (var t in tweaks.Where(t => t.Actions.Count > 0 && t.Actions.All(a => a.Optional)))
        problems.Add($"{t.Id}: every action is optional, so it can succeed without doing anything");

    foreach (var tweak in tweaks)
        foreach (var action in tweak.Actions)
        {
            string? path = action switch
            {
                RegistryValueAction r => r.KeyPath,
                RegistryKeyAction k => k.KeyPath,
                _ => null,
            };
            if (path is not null && !ParsesAsRegistryPath(path))
                problems.Add($"{tweak.Id}: unparseable registry path '{path}'");
        }

    problems.AddRange(FindConflictingWrites(tweaks));

    // The conflict check has to catch what it claims to, so it is fed one known conflict of each
    // kind. Without these, a check that silently stopped working would read as "no conflicts".
    var controls = new[]
    {
        ControlTweak("control-hkcr", new RegistryValueAction { KeyPath = @"HKCR\WinPureControl", ValueName = "V", ApplyValue = 1 }),
        ControlTweak("control-hkcu-classes", new RegistryValueAction { KeyPath = @"HKCU\Software\Classes\WinPureControl", ValueName = "V", ApplyValue = 2 }),
        ControlTweak("control-delete", new RegistryKeyAction { KeyPath = @"HKCU\Software\WinPureControlZone", DeleteOnApply = true }),
        ControlTweak("control-write-inside", new RegistryValueAction { KeyPath = @"HKCU\Software\WinPureControlZone\Inner", ValueName = "X", ApplyValue = 1 }),
    };
    var caught = FindConflictingWrites(controls);
    if (!caught.Any(p => p.Contains("control-hkcr") && p.Contains("control-hkcu-classes")))
        problems.Add("the conflict check missed an HKCR write colliding with the same key under HKCU Classes");
    if (!caught.Any(p => p.Contains("control-delete") && p.Contains("control-write-inside")))
        problems.Add("the conflict check missed a value written inside a key another tweak deletes");

    return Report("the catalog is internally consistent",
        problems.Count == 0,
        problems.Count == 0
            ? $"{tweaks.Count} tweaks: ids unique, registry paths parseable, no conflicting writes (HKCR aliases and deleted keys included, both checks proven on a known conflict)"
            : string.Join(" | ", problems));
}

Tweak ControlTweak(string id, TweakAction action) => new()
{
    Id = id,
    Category = TweakCategory.UI,
    Name = id,
    Description = "known conflict, tests only",
    Icon = "x",
    Actions = new TweakAction[] { action },
};

// Two tweaks writing different data to one value undo each other, and each one's backup records
// the other's value. Compared on the key Windows really writes: HKCR is a merged view of the user's
// and the machine's Classes keys, and a value inside a key that another tweak deletes is lost too.
static List<string> FindConflictingWrites(IEnumerable<Tweak> tweaks)
{
    var problems = new List<string>();
    var writes = new Dictionary<string, (string tweakId, string value, string shown)>();
    var valueKeys = new List<(string tweakId, string key, string shown)>();
    var deletedKeys = new List<(string tweakId, string key, string shown)>();

    foreach (var tweak in tweaks)
        foreach (var action in tweak.Actions)
        {
            if (action is RegistryValueAction rv)
            {
                string shown = $"{rv.KeyPath}!{rv.ValueName}";
                string value = rv.ApplyValue.ToString() ?? "";
                foreach (var key in RegistryAliases(rv.KeyPath))
                {
                    string slot = key + "!" + rv.ValueName.ToUpperInvariant();
                    if (writes.TryGetValue(slot, out var prev) && prev.value != value)
                        problems.Add($"{prev.tweakId} and {tweak.Id} both write {prev.shown} / {shown} with different data ({prev.value} vs {value})");
                    else if (!writes.ContainsKey(slot))
                        writes[slot] = (tweak.Id, value, shown);
                    valueKeys.Add((tweak.Id, key, shown));
                }
            }
            else if (action is RegistryKeyAction { DeleteOnApply: true } rk)
            {
                foreach (var key in RegistryAliases(rk.KeyPath))
                    deletedKeys.Add((tweak.Id, key, rk.KeyPath));
            }
        }

    foreach (var deleted in deletedKeys)
        foreach (var write in valueKeys.Where(w => w.tweakId != deleted.tweakId &&
                     (w.key == deleted.key || w.key.StartsWith(deleted.key + "\\", StringComparison.Ordinal))))
            problems.Add($"{deleted.tweakId} deletes {deleted.shown}, inside which {write.tweakId} writes {write.shown}");

    return problems.Distinct().ToList();
}

// Upper-cased, short hive names, with HKCR expanded to both keys it can land in.
static IEnumerable<string> RegistryAliases(string keyPath)
{
    int idx = keyPath.IndexOf('\\');
    string hive = (idx < 0 ? keyPath : keyPath[..idx]).ToUpperInvariant();
    string rest = (idx < 0 ? "" : keyPath[(idx + 1)..]).ToUpperInvariant();
    hive = hive switch
    {
        "HKEY_LOCAL_MACHINE" => "HKLM",
        "HKEY_CURRENT_USER" => "HKCU",
        "HKEY_CLASSES_ROOT" => "HKCR",
        "HKEY_USERS" => "HKU",
        _ => hive,
    };
    if (hive == "HKCR")
    {
        yield return @"HKCU\SOFTWARE\CLASSES\" + rest;
        yield return @"HKLM\SOFTWARE\CLASSES\" + rest;
    }
    else
    {
        yield return hive + "\\" + rest;
    }
}

static bool ParsesAsRegistryPath(string keyPath)
{
    int idx = keyPath.IndexOf('\\');
    string hive = (idx < 0 ? keyPath : keyPath[..idx]).ToUpperInvariant();
    return hive is "HKLM" or "HKEY_LOCAL_MACHINE" or "HKCU" or "HKEY_CURRENT_USER"
        or "HKCR" or "HKEY_CLASSES_ROOT" or "HKU" or "HKEY_USERS";
}

// Not a test — a report. Which tweaks have nothing to act on *on this machine*: a Store app
// that is not installed, a scheduled task Windows dropped, a context-menu key that moved.
// Some entries are legitimate (the app was already removed); a whole tweak with every action
// dead is worth a look, which is exactly how the Dev Home and 'Share' bugs looked.
void ReportCatalogDeadWeightOnThisMachine()
{
    var ctx = ScanContext.Gather();
    if (!ctx.Loaded || !ctx.AppsQueryOk) { Console.WriteLine("\n(skipping the dead-weight report: the scan was incomplete)"); return; }

    var dead = new List<string>();
    foreach (var tweak in TweakCatalog.Build())
    {
        var missing = new List<string>();
        int checkable = 0;
        foreach (var action in tweak.Actions)
        {
            switch (action)
            {
                case AppxRemoveAction appx:
                    checkable++;
                    if (!ctx.AnyPackageInstalled(appx.PackagePatterns))
                        missing.Add("apps " + string.Join("/", appx.PackagePatterns));
                    break;
                case ScheduledTaskAction task:
                    checkable++;
                    if (!ScheduledTaskAction.ReadState(task.TaskPath).exists)
                        missing.Add("task " + task.TaskPath);
                    break;
                case RegistryKeyAction key when key.DeleteOnApply:
                    checkable++;
                    if (!RegistryKeyExists(key.KeyPath)) missing.Add("key " + key.KeyPath);
                    break;
                case FeatureAction feature when ctx.FeaturesQueryOk:
                    checkable++;
                    if (!ctx.FeatureState.ContainsKey(feature.FeatureName)) missing.Add("feature " + feature.FeatureName);
                    break;
            }
        }
        if (checkable > 0 && missing.Count == checkable)
            dead.Add($"  {tweak.Id}: nothing to act on ({string.Join("; ", missing)})");
    }

    Console.WriteLine();
    Console.WriteLine("Tweaks with nothing to act on, on this machine (informational, not a failure):");
    Console.WriteLine(dead.Count == 0 ? "  none" : string.Join(Environment.NewLine, dead));
}

// A value under \Policies\ is only a real policy if Windows declares it in one of the .admx
// files under PolicyDefinitions. Anything else is a name that looks official and may be
// ignored — which is exactly how the dead Widgets tweak looked, and how a "TurnOffSavingSnapshots"
// copied from a reference repo turned out not to exist on this build.
// Informational, not a failure: a CI image can carry a different set of ADMX than a desktop.
void ReportPolicyWritesNotBackedByAnAdmx()
{
    string admxDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "PolicyDefinitions");
    if (!Directory.Exists(admxDir))
    {
        Console.WriteLine("\n(skipping the policy audit: no PolicyDefinitions on this machine)");
        return;
    }

    // Every <policy> declares the key it lives under, the value name, and whether it is a
    // Machine or User setting. Matching the value name alone is not enough: the dead Widgets
    // tweak wrote a real value name under a path Windows does not read.
    var declared = new List<(string Key, string Value, string Class)>();
    foreach (var file in Directory.EnumerateFiles(admxDir, "*.admx"))
    {
        string text;
        try { text = File.ReadAllText(file); } catch { continue; }
        foreach (Match p in Regex.Matches(text, @"<policy\b(?<head>[^>]*)>(?<body>.*?)</policy>", RegexOptions.Singleline))
        {
            string head = p.Groups["head"].Value;
            string key = Regex.Match(head, @"key=""([^""]+)""").Groups[1].Value;
            string cls = Regex.Match(head, @"class=""([^""]+)""").Groups[1].Value;
            if (key.Length == 0) continue;
            foreach (Match v in Regex.Matches(head + p.Groups["body"].Value, @"valueName=""([^""]+)"""))
                declared.Add((key, v.Groups[1].Value, cls));
        }
    }

    var problems = new List<string>();
    int checkedCount = 0;
    foreach (var tweak in TweakCatalog.Build())
        foreach (var action in tweak.Actions.OfType<RegistryValueAction>())
        {
            if (!action.KeyPath.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase)) continue;
            checkedCount++;

            int slash = action.KeyPath.IndexOf('\\');
            string hive = action.KeyPath[..slash];
            string sub = action.KeyPath[(slash + 1)..];

            var sameName = declared.Where(d => d.Value.Equals(action.ValueName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameName.Count == 0)
            {
                problems.Add($"  {tweak.Id}: '{action.ValueName}' is declared by no ADMX at all  ({action.KeyPath})");
                continue;
            }

            if (!sameName.Any(d => d.Key.Equals(sub, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"  {tweak.Id}: '{action.ValueName}' exists, but under {string.Join(" / ", sameName.Select(d => d.Key).Distinct())} — we write it to {sub}");

            // A Machine-scoped policy written to HKCU (or the reverse) is simply ignored.
            bool hkcu = hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase);
            var scopes = sameName.Select(d => d.Class).Distinct().ToList();
            if (hkcu && scopes.All(s => s.Equals("Machine", StringComparison.OrdinalIgnoreCase)))
                problems.Add($"  {tweak.Id}: '{action.ValueName}' is Machine-scoped but written to HKCU");
            if (!hkcu && scopes.All(s => s.Equals("User", StringComparison.OrdinalIgnoreCase)))
                problems.Add($"  {tweak.Id}: '{action.ValueName}' is User-scoped but written to {hive}");
        }

    Console.WriteLine();
    Console.WriteLine($"Policy writes that do not match this machine's ADMX definitions "
                    + $"({checkedCount} checked against {declared.Count} declared values, informational):");
    Console.WriteLine(problems.Count == 0 ? "  none" : string.Join(Environment.NewLine, problems));
}

static bool RegistryKeyExists(string keyPath)
{
    int idx = keyPath.IndexOf('\\');
    string hive = (idx < 0 ? keyPath : keyPath[..idx]).ToUpperInvariant();
    string sub = idx < 0 ? "" : keyPath[(idx + 1)..];
    RegistryKey? root = hive switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        "HKU" or "HKEY_USERS" => Registry.Users,
        _ => null,
    };
    if (root is null) return false;
    using var key = root.OpenSubKey(sub);
    return key is not null;
}

// Child process: tweak A is a normal registry change, tweak B kills this process from the
// outside. The engine dies between two tweaks of one batch.
void RunCrashChild()
{
    var a = ToyTweak("test-crash-midbatch", applyValue: 0, defaultValue: 1, valueName: "Crash");
    var killer = new Tweak
    {
        Id = "test-crash-killer",
        Category = TweakCategory.Privacy,
        Name = "Kill the test runner",
        Description = "ends this process mid-batch",
        Icon = "",
        Actions = new TweakAction[]
        {
            new CommandAction
            {
                ApplyScript = $"Stop-Process -Id {Environment.ProcessId} -Force",
                RevertScript = "",
                Detect = _ => false,
                TimeoutMs = 30_000,
            }
        },
    };
    new TweakEngine(new BackupManager()).ApplyChanges(new[] { (a, true), (killer, true) });
}

// ---------------------------------------------------------------- helpers

Tweak ToyTweak(string id, int applyValue, int defaultValue, string valueName) => new()
{
    Id = id,
    Category = TweakCategory.Privacy,
    Name = $"Test tweak {id}",
    Description = "toy tweak, tests only",
    Icon = "",
    Actions = new TweakAction[]
    {
        new RegistryValueAction
        {
            KeyPath = ToyKey,
            ValueName = valueName,
            Kind = RegistryValueKind.DWord,
            ApplyValue = applyValue,
            DefaultValue = defaultValue,
        }
    },
};

void WriteToy(string name, int value)
{
    using var key = Registry.CurrentUser.CreateSubKey(ToySub, writable: true)!;
    key.SetValue(name, value, RegistryValueKind.DWord);
}

int? ReadToy(string name)
{
    using var key = Registry.CurrentUser.OpenSubKey(ToySub);
    return key?.GetValue(name) as int?;
}

bool AnyBackupMentions(string tweakId, string value)
{
    if (!Directory.Exists(scratch)) return false;
    foreach (var file in Directory.EnumerateFiles(scratch, "backup_*.json"))
    {
        string text = File.ReadAllText(file);
        if (text.Contains(tweakId) && text.Contains($"\"Value\": \"{value}\"")) return true;
    }
    return false;
}

void Reset()
{
    Cleanup();
    if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
}

void Cleanup()
{
    try { Registry.CurrentUser.DeleteSubKeyTree(ToySub, throwOnMissingSubKey: false); } catch { }
}

bool Report(string name, bool ok, string detail)
{
    // Plain ASCII on purpose: this runs in consoles that choke on anything else.
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
    Console.WriteLine($"       {detail}");
    return ok;
}

/// <summary>A command-driven setting held in memory, so tests never run powercfg or DISM.</summary>
sealed class FakeSetting : ISystemStateHandler
{
    private readonly ISystemStateHandler _real;

    public FakeSetting(string initial, ISystemStateHandler real)
    {
        Current = initial;
        _real = real;
    }

    public string Current { get; private set; }
    public List<string> Writes { get; } = new();

    public string? Read() => Current;

    // The real handler's check, so a test also catches a broken validator.
    public bool IsValid(string state) => _real.IsValid(state);

    public void Write(string state)
    {
        Writes.Add(state);
        Current = state;
    }
}

/// <summary>A winget that never touches the network or installs anything.</summary>
sealed class FakeWinget : IWingetBackend
{
    public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase) { "Git.Git" };
    public HashSet<string> ReallyInstalls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ExitCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Installs { get; } = new();

    public IReadOnlySet<string>? ReadInstalledIds() => new HashSet<string>(Present, StringComparer.OrdinalIgnoreCase);

    public int Install(string id)
    {
        Installs.Add(id);
        if (ReallyInstalls.Contains(id)) Present.Add(id);
        return ExitCodes.TryGetValue(id, out int code) ? code : 0;
    }
}

/// <summary>Windows features held in memory, so tests never run DISM.</summary>
sealed class FakeFeatures : IFeatureBackend
{
    public Dictionary<string, string> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes { get; } = new();

    public string Read(string featureName) =>
        States.TryGetValue(featureName, out var state) ? state : OptionalFeatures.Missing;

    public void Write(string featureName, bool enable)
    {
        Writes.Add($"{featureName}={(enable ? "on" : "off")}");
        States[featureName] = enable ? OptionalFeatures.Enabled : OptionalFeatures.Disabled;
    }
}
