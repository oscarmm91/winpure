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
    /// <summary>DOMAIN\user signed in to this process's session. For the message only.</summary>
    public string? SessionUser { get; init; }
    /// <summary>DOMAIN\user this process runs as. For the message only.</summary>
    public string? ProcessUser { get; init; }
    /// <summary>SID of the signed-in user — what the comparison actually uses.</summary>
    public string? SessionSid { get; init; }
    /// <summary>SID of the account this process runs as.</summary>
    public string? ProcessSid { get; init; }
    /// <summary>Human-readable reasons a restart is pending. Empty = none found.</summary>
    public IReadOnlyList<string> PendingRebootSignals { get; init; } = Array.Empty<string>();
    /// <summary>BitLocker VolumeStatus of the system drive, e.g. "FullyEncrypted".</summary>
    public string? BitLockerStatus { get; init; }
    /// <summary>Status of the EventLog service, e.g. "Running".</summary>
    public string? EventLogStatus { get; init; }
    public bool AppsQueryOk { get; init; }
    /// <summary>The app listing covered every user, not the current-user fallback.</summary>
    public bool AppsListedForAllUsers { get; init; }
    /// <summary>An LTSC edition, which ships without the Microsoft Store.</summary>
    public bool EditionIsLtsc { get; init; }
    public IReadOnlySet<string> InstalledPackages { get; init; } = new HashSet<string>();

    /// <summary>
    /// Reads the real system. Never throws: anything unreadable stays null.
    /// Pass a <paramref name="bitLocker"/> task already started alongside the scan to avoid
    /// waiting for that query in sequence; without one it is read here.
    /// </summary>
    public static GuardInputs Gather(ScanContext ctx, Task<string?>? bitLocker = null)
    {
        string? processUser = null, processSid = null;
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            processUser = identity.Name;
            processSid = identity.User?.Value;
        }
        catch { }

        // Compared by SID, never by name. Two APIs format names differently — a Microsoft or
        // Entra account can come back as an alias from one and a domain-qualified name from
        // the other — and a guard that fires on the same person trains everyone to click past it.
        string? sessionUser = null, sessionSid = null;
        try
        {
            sessionUser = NativeMethods.GetSessionUser(Process.GetCurrentProcess().SessionId);
            if (sessionUser is not null)
                sessionSid = new NTAccount(sessionUser).Translate(typeof(SecurityIdentifier)).Value;
        }
        catch { sessionSid = null; }

        // .Result on a faulted task rethrows; this method promises not to, and the scan it runs
        // inside has no catch of its own.
        string? bitLockerStatus = null;
        try { bitLockerStatus = bitLocker is not null ? bitLocker.GetAwaiter().GetResult() : ReadBitLockerStatus(); }
        catch { }

        return new GuardInputs
        {
            ProcessUser = processUser,
            ProcessSid = processSid,
            SessionUser = sessionUser,
            SessionSid = sessionSid,
            PendingRebootSignals = ReadPendingRebootSignals().ToList(),
            BitLockerStatus = bitLockerStatus,
            EventLogStatus = ctx.Extras.TryGetValue("eventLog", out var ev) && ev.Length > 0 ? ev : null,
            AppsQueryOk = ctx.AppsQueryOk,
            AppsListedForAllUsers = ctx.AppsListedForAllUsers,
            EditionIsLtsc = ReadEditionIsLtsc(),
            InstalledPackages = ctx.InstalledPackages,
        };
    }

    /// <summary>
    /// Its own short PowerShell call, outside the shared scan pass — on purpose. Measured on
    /// 2026-09-12: Get-BitLockerVolume added ~10 s over a bare PowerShell start, because it goes
    /// through WMI (and without elevation spends that time only to answer "access denied").
    /// Inside the single scan pass, one slow optional fact could push the whole scan past its
    /// timeout and turn every tweak into Unknown. Out here, the worst case is that this one fact
    /// stays unknown and its guard stays silent.
    /// </summary>
    public static string? ReadBitLockerStatus()
    {
        var result = PowerShellRunner.Run(
            "[string](Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop).VolumeStatus",
            timeoutMs: 15_000);
        string status = result.Output.Trim();
        return result.Success && status.Length > 0 ? status : null;
    }

    /// <summary>
    /// ProductName is the one string here Windows never translates (it even still says
    /// "Windows 10 Pro" on Windows 11), and on every LTSC edition it contains "LTSC" — the same
    /// test Sophia Script's own installers use. EditionID is kept as a second signal.
    /// </summary>
    private static bool ReadEditionIsLtsc()
    {
        try
        {
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = cv?.GetValue("ProductName") as string ?? "";
            string edition = cv?.GetValue("EditionID") as string ?? "";
            return product.Contains("LTSC", StringComparison.OrdinalIgnoreCase)
                || new[] { "EnterpriseS", "EnterpriseSN", "IoTEnterpriseS" }
                    .Any(e => e.Equals(edition, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static IEnumerable<string> ReadPendingRebootSignals()
    {
        const string cbs = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
        const string wu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";

        // Only servicing and Windows Update: those are what can overwrite registry changes when
        // Windows finishes. PendingFileRenameOperations was left out on purpose — files an
        // installer queued for replacement do not touch registry tweaks, and third-party software
        // can keep entries there indefinitely, which would turn this into a warning that fires
        // every day and teaches people to click past it.
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
    public const string RebootPendingId = "reboot-pending";
    public const string BitLockerBusyId = "bitlocker-busy";
    public const string CoreAppsMissingId = "core-apps-missing";
    public const string EventLogStoppedId = "eventlog-stopped";

    private const string StorePackage = "Microsoft.WindowsStore";
    private const string ShellPackage = "MicrosoftWindows.Client.CBS";

    public static List<GuardWarning> Evaluate(GuardInputs f)
    {
        var warnings = new List<GuardWarning>();

        // ~40 tweaks and the whole Startup page write under HKEY_CURRENT_USER — which, for an
        // elevated process, is the profile of the account that elevated it. "Run as" with a
        // second admin account sends every one of those changes to that account instead, and
        // the backup would restore that account too.
        if (f.SessionSid is { Length: > 0 } session && f.ProcessSid is { Length: > 0 } process
            && !session.Equals(process, StringComparison.OrdinalIgnoreCase))
        {
            string signedIn = f.SessionUser ?? session;
            string runningAs = f.ProcessUser ?? process;
            warnings.Add(new GuardWarning(DifferentUserId, GuardSeverity.High,
                "WinPure is running as a different user",
                $"You are signed in as {signedIn}, but WinPure is running as {runningAs}. Personal settings and "
              + $"Startup apps would change for {runningAs}, not for you."));
        }

        // ANY signal is enough. Sophia's version requires all five keys at once
        // ([Array]::TrueForAll), which practically never happens — copied literally, it is a
        // guard that can never fire.
        if (f.PendingRebootSignals.Count > 0)
        {
            warnings.Add(new GuardWarning(RebootPendingId, GuardSeverity.Medium,
                "Windows is waiting for a restart",
                string.Join("; ", f.PendingRebootSignals) + ". Changes made now can be undone when Windows "
              + "finishes, and would then show as not applied. Restarting first is safer."));
        }

        // Only the read-only half of Sophia's BitLocker check. Offering to decrypt a drive —
        // the other half — is exactly what this app must never do.
        if (f.BitLockerStatus is { Length: > 0 } bl
            && !bl.Equals("FullyEncrypted", StringComparison.OrdinalIgnoreCase)
            && !bl.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new GuardWarning(BitLockerBusyId, GuardSeverity.High,
                "BitLocker is still working on your system drive",
                $"The drive is {DescribeBitLocker(bl)}. Wait until that finishes before changing system settings."));
        }

        // Only judged when the listing covered every user. An empty list from a failed query is
        // not evidence, and neither is the current-user fallback: run as another account, it
        // lists THAT account's apps. LTSC ships without the Store by design, so there only the
        // shell package is required — matching Sophia Script's own LTSC version of this check.
        if (f.AppsQueryOk && f.AppsListedForAllUsers)
        {
            var required = f.EditionIsLtsc ? new[] { ShellPackage } : new[] { StorePackage, ShellPackage };
            var missing = required.Where(p => !f.InstalledPackages.Contains(p)).ToList();
            if (missing.Count > 0)
            {
                warnings.Add(new GuardWarning(CoreAppsMissingId, GuardSeverity.Medium,
                    "Part of Windows' app platform is missing",
                    $"Not installed: {string.Join(", ", missing.Select(FriendlyPackage))}. This usually means another "
                  + "tool already removed it. Removing more apps on top of that can leave Start, search or Settings not working."));
            }
        }

        // WinPure does not read the event log, so this is not about detection. A stopped Event
        // Log is almost never deliberate and is a strong sign another tool has been here.
        if (f.EventLogStatus is { Length: > 0 } ev && !ev.Equals("Running", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new GuardWarning(EventLogStoppedId, GuardSeverity.Low,
                "The Windows Event Log is switched off",
                "That is rarely done on purpose, and usually means another tool already changed this system."));
        }

        return warnings.OrderByDescending(w => w.Severity).ToList();
    }

    // ---- which guards each action asks about
    // A guard shown where it does not apply is noise, and noise is what makes people stop
    // reading these dialogs. Each action asks only about what can actually go wrong for it.

    /// <summary>Apply touches everything the catalog touches: every guard applies.</summary>
    public static List<GuardWarning> ForApply(IEnumerable<GuardWarning> all) => all.ToList();

    /// <summary>A restore writes back the per-user half of a backup — only the account matters.</summary>
    public static List<GuardWarning> ForRestore(IEnumerable<GuardWarning> all) =>
        all.Where(g => g.Id == DifferentUserId).ToList();

    /// <summary>
    /// Startup entries live in the signed-in user's registry and Startup folder. Servicing,
    /// BitLocker and missing app components do not affect them.
    /// </summary>
    public static List<GuardWarning> ForStartup(IEnumerable<GuardWarning> all) =>
        all.Where(g => g.Id == DifferentUserId).ToList();

    /// <summary>
    /// Repair tools are system-wide (SFC/DISM, Windows Update, the network stack) plus the temp
    /// folder, which is per-user. A pending restart and BitLocker matter to them; missing app
    /// components and a stopped Event Log would only nag someone who is trying to fix their PC.
    /// </summary>
    public static List<GuardWarning> ForRepair(IEnumerable<GuardWarning> all) =>
        all.Where(g => g.Id is DifferentUserId or RebootPendingId or BitLockerBusyId).ToList();

    /// <summary>The text of the "continue anyway?" dialog.</summary>
    public static string Describe(IEnumerable<GuardWarning> warnings) =>
        string.Join("\n\n", warnings.Select(w => $"• {w.Title}\n   {w.Detail}"));

    private static string DescribeBitLocker(string status) => status switch
    {
        "EncryptionInProgress" => "being encrypted",
        "DecryptionInProgress" => "being decrypted",
        "EncryptionPaused" => "part-way through encryption (paused)",
        "DecryptionPaused" => "part-way through decryption (paused)",
        _ => $"in state '{status}'",
    };

    private static string FriendlyPackage(string package) => package switch
    {
        StorePackage => "Microsoft Store",
        ShellPackage => "Windows Feature Experience Pack (Start, taskbar and search)",
        _ => package,
    };
}
