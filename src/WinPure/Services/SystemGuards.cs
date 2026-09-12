using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace WinPure.Services;

public enum GuardSeverity { Low, Medium, High }

/// <summary>Something about this system that makes applying changes right now unwise.</summary>
public sealed record GuardWarning(string Id, GuardSeverity Severity, string Title, string Detail);

/// <summary>
/// The raw facts the guards judge. Gathered once per scan; kept separate from the judging so
/// every guard can be tested against made-up facts without touching a real machine.
/// A null fact means "could not tell" — and a guard never fires on something it could not see.
/// </summary>
public sealed record GuardInputs
{
    /// <summary>DOMAIN\user signed in to the session this process runs in.</summary>
    public string? SessionUser { get; init; }
    /// <summary>DOMAIN\user this process is actually running as.</summary>
    public string? ProcessUser { get; init; }
    /// <summary>Human-readable reasons a restart is pending. Empty = none found.</summary>
    public IReadOnlyList<string> PendingRebootSignals { get; init; } = Array.Empty<string>();
    /// <summary>BitLocker VolumeStatus of the system drive, e.g. "FullyEncrypted".</summary>
    public string? BitLockerStatus { get; init; }
    /// <summary>Status of the EventLog service, e.g. "Running".</summary>
    public string? EventLogStatus { get; init; }
    public bool AppsQueryOk { get; init; }
    public IReadOnlySet<string> InstalledPackages { get; init; } = new HashSet<string>();

    /// <summary>
    /// Reads the real system. Never throws: anything unreadable stays null.
    /// Pass a <paramref name="bitLocker"/> task already started alongside the scan to avoid
    /// waiting for that query in sequence; without one it is read here.
    /// </summary>
    public static GuardInputs Gather(ScanContext ctx, Task<string?>? bitLocker = null)
    {
        string? processUser = null;
        try { processUser = WindowsIdentity.GetCurrent().Name; } catch { }

        string? sessionUser = null;
        try { sessionUser = NativeMethods.GetSessionUser(Process.GetCurrentProcess().SessionId); } catch { }

        return new GuardInputs
        {
            ProcessUser = processUser,
            SessionUser = sessionUser,
            PendingRebootSignals = ReadPendingRebootSignals().ToList(),
            BitLockerStatus = bitLocker is not null ? bitLocker.Result : ReadBitLockerStatus(),
            EventLogStatus = ctx.Extras.TryGetValue("eventLog", out var ev) && ev.Length > 0 ? ev : null,
            AppsQueryOk = ctx.AppsQueryOk,
            InstalledPackages = ctx.InstalledPackages,
        };
    }

    /// <summary>
    /// Its own short PowerShell call, outside the shared scan pass — on purpose. Measured on
    /// 2026-09-12: Get-BitLockerVolume added ~10 s over a bare PowerShell start, because it goes
    /// through WMI (and without elevation spends that time only to answer "access denied").
    /// Inside the single scan pass, one slow optional fact could push the whole scan past its
    /// timeout and turn every tweak into Unknown — the exact failure the per-section flags exist
    /// to prevent. Out here, the worst case is that this one fact stays unknown and its guard
    /// stays silent.
    /// </summary>
    public static string? ReadBitLockerStatus()
    {
        var result = PowerShellRunner.Run(
            "[string](Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop).VolumeStatus",
            timeoutMs: 15_000);
        string status = result.Output.Trim();
        return result.Success && status.Length > 0 ? status : null;
    }

    private static IEnumerable<string> ReadPendingRebootSignals()
    {
        const string cbs = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
        const string wu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";

        var keys = new (string Path, string Reason)[]
        {
            ($@"{cbs}\RebootPending", "Windows servicing is waiting for a restart"),
            ($@"{cbs}\RebootInProgress", "a Windows servicing restart is in progress"),
            ($@"{cbs}\PackagesPending", "Windows packages are pending installation"),
            ($@"{wu}\RebootRequired", "Windows Update needs a restart"),
            ($@"{wu}\PostRebootReporting", "Windows Update is finishing after a restart"),
        };

        foreach (var (path, reason) in keys)
        {
            bool exists = false;
            try { using var key = Registry.LocalMachine.OpenSubKey(path); exists = key is not null; } catch { }
            if (exists) yield return reason;
        }

        // The most common signal of all, and the one Sophia Script leaves out: files an
        // installer or update queued to be replaced when Windows next starts.
        bool renames = false;
        try
        {
            using var sm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            renames = sm?.GetValue("PendingFileRenameOperations") is string[] ops && ops.Any(o => o.Length > 0);
        }
        catch { }
        if (renames) yield return "files are queued to be replaced at the next restart";
    }
}

/// <summary>
/// Checks run before WinPure touches the system. They WARN and ask; they never refuse to run —
/// someone repairing another person's machine, or who switched a service off on purpose, must
/// still be able to go ahead. What they must never do is let a known-bad moment pass in silence.
/// </summary>
public static class SystemGuards
{
    public const string DifferentUserId = "different-user";

    public static List<GuardWarning> Evaluate(GuardInputs f)
    {
        var warnings = new List<GuardWarning>();

        // 40 tweaks and the whole Startup page write under HKEY_CURRENT_USER — which, for an
        // elevated process, is the profile of the account that elevated it. "Run as" with a
        // second admin account sends every one of those changes to that account instead, and
        // the backup would restore that account too.
        if (f.SessionUser is { Length: > 0 } session && f.ProcessUser is { Length: > 0 } process
            && !session.Equals(process, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new GuardWarning(DifferentUserId, GuardSeverity.High,
                "WinPure is running as a different user",
                $"Signed in: {session}. WinPure is running as: {process}. Per-user settings and Startup apps "
              + $"would change for {process}, not for {session}."));
        }

        // ANY signal is enough. Sophia's version requires all five keys at once
        // ([Array]::TrueForAll), which practically never happens — copied literally, it is a
        // guard that can never fire.
        if (f.PendingRebootSignals.Count > 0)
        {
            warnings.Add(new GuardWarning("reboot-pending", GuardSeverity.Medium,
                "A restart is pending",
                string.Join("; ", f.PendingRebootSignals) + ". Changes applied now can be overwritten when "
              + "Windows finishes, and would then show as not applied."));
        }

        // Only the read-only half of Sophia's BitLocker check. Offering to decrypt a drive —
        // the other half — is exactly what this app must never do.
        if (f.BitLockerStatus is { Length: > 0 } bl
            && !bl.Equals("FullyEncrypted", StringComparison.OrdinalIgnoreCase)
            && !bl.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new GuardWarning("bitlocker-busy", GuardSeverity.High,
                "BitLocker is working on the system drive",
                $"Drive status: {bl}. Wait until encryption or decryption finishes before changing system settings."));
        }

        // Only judged when the app listing actually worked: an empty list from a failed query
        // is not evidence that anything is missing.
        if (f.AppsQueryOk)
        {
            var missing = new[] { "Microsoft.WindowsStore", "MicrosoftWindows.Client.CBS" }
                .Where(p => !f.InstalledPackages.Contains(p)).ToList();
            if (missing.Count > 0)
            {
                warnings.Add(new GuardWarning("core-apps-missing", GuardSeverity.Medium,
                    "Core Windows app components are missing",
                    $"Not installed: {string.Join(", ", missing)}. This usually means another tool already "
                  + "removed them. Removing more apps on top of that can leave Start or Settings not working."));
            }
        }

        // WinPure does not read the event log, so this is not about detection. A stopped Event
        // Log is almost never deliberate and is a strong sign another tool has been here.
        if (f.EventLogStatus is { Length: > 0 } ev && !ev.Equals("Running", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new GuardWarning("eventlog-stopped", GuardSeverity.Low,
                "The Windows Event Log service is not running",
                $"Service status: {ev}. That is rarely done on purpose, and usually means another tool already modified this system."));
        }

        return warnings.OrderByDescending(w => w.Severity).ToList();
    }

    /// <summary>The text of the "continue anyway?" dialog.</summary>
    public static string Describe(IEnumerable<GuardWarning> warnings) =>
        string.Join("\n\n", warnings.Select(w => $"• {w.Title}\n   {w.Detail}"));
}
