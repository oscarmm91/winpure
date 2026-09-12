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
failures += AChildPowerShellDiesWithTheApp() ? 0 : 1;
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
bool AChildPowerShellDiesWithTheApp()
{
    const string name = "a child PowerShell dies with the app";
    string pidFile = Path.Combine(Path.GetTempPath(), $"winpure-orphan-{Environment.ProcessId}.pid");
    string jobFile = pidFile + ".job";
    foreach (var f in new[] { pidFile, jobFile }) { try { File.Delete(f); } catch { } }

    var psi = new ProcessStartInfo(Environment.ProcessPath!, $"orphan-child \"{pidFile}\"") { UseShellExecute = false };
    using var child = Process.Start(psi)!;

    int pid = 0;
    string job = "";
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(90) && (pid == 0 || job.Length == 0))
    {
        try { if (pid == 0 && File.Exists(pidFile)) int.TryParse(File.ReadAllText(pidFile).Trim(), out pid); } catch { }
        try { if (job.Length == 0 && File.Exists(jobFile)) job = File.ReadAllText(jobFile).Trim(); } catch { }
        Thread.Sleep(250);
    }

    if (pid == 0 || job.Length == 0)
    {
        try { child.Kill(entireProcessTree: true); } catch { }
        return Report(name, false, $"the child never reported its PowerShell within 90 s (pid={pid}, job='{job}')");
    }

    child.Kill();                     // the app dies — deliberately NOT its process tree
    child.WaitForExit(10_000);
    Thread.Sleep(3_000);

    bool survived = false;
    try
    {
        using var ps = Process.GetProcessById(pid);
        survived = !ps.HasExited;
        if (survived) ps.Kill();      // clean up whatever the test left behind
    }
    catch (ArgumentException) { }     // no such process: it died, as it should
    foreach (var f in new[] { pidFile, jobFile }) { try { File.Delete(f); } catch { } }

    // A CI runner may refuse nested job objects; locally that would be a real regression.
    if (job != "True" && Environment.GetEnvironmentVariable("CI") == "true")
        return Report(name, true, "skipped on CI: this environment would not place the child in a job object");

    return Report(name, !survived,
        $"placed in the kill-on-close job={job}; PowerShell pid {pid} still alive after the app was killed={survived} (expected False)");
}

void RunOrphanChild(string pidFile)
{
    Task.Run(() => PowerShellRunner.Run(
        $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 120", timeoutMs: 180_000));

    // PowerShell writes its PID only after it was started and placed in the job, so by then the
    // assignment result is final.
    var clock = Stopwatch.StartNew();
    while (!File.Exists(pidFile) && clock.Elapsed < TimeSpan.FromSeconds(90)) Thread.Sleep(100);
    File.WriteAllText(pidFile + ".job", PowerShellRunner.LastJobAssignOk.ToString());
    Thread.Sleep(Timeout.Infinite);
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

    return Report("the docs agree with the catalog",
        problems.Count == 0,
        problems.Count == 0
            ? $"{catalog.Values.Sum()} tweaks: every category matches in the catalog, docs/tweaks.md and README.md"
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

    var writes = new Dictionary<string, (string tweakId, string value)>(StringComparer.OrdinalIgnoreCase);
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

            if (action is RegistryValueAction rv)
            {
                string slot = $"{rv.KeyPath}!{rv.ValueName}";
                string value = rv.ApplyValue.ToString() ?? "";
                if (writes.TryGetValue(slot, out var prev) && prev.value != value)
                    problems.Add($"{prev.tweakId} and {tweak.Id} both write {slot} with different data ({prev.value} vs {value})");
                else
                    writes[slot] = (tweak.Id, value);
            }
        }

    return Report("the catalog is internally consistent",
        problems.Count == 0,
        problems.Count == 0
            ? $"{tweaks.Count} tweaks: ids unique, registry paths parseable, no conflicting writes"
            : string.Join(" | ", problems));
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
