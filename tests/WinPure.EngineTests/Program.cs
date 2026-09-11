using System.Diagnostics;
using System.IO;
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
ReportCatalogDeadWeightOnThisMachine();

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

// Catalog sanity that does not depend on which machine this runs on: duplicate ids, registry
// paths with a hive nobody can parse, and two tweaks writing different data to the same value.
bool CatalogIsInternallyConsistent()
{
    var tweaks = TweakCatalog.Build();
    var problems = new List<string>();

    foreach (var group in tweaks.GroupBy(t => t.Id).Where(g => g.Count() > 1))
        problems.Add($"duplicate id '{group.Key}'");

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
