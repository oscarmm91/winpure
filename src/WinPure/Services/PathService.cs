using System.IO;
using Microsoft.Win32;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>Which PATH is being edited: the per-user PATH or the machine-wide one (needs elevation to write).</summary>
public enum PathScope { User, Machine }

/// <summary>What, if anything, is wrong with one PATH entry.</summary>
public enum PathIssue
{
    None,
    Empty,        // a stray ";;" — an empty segment
    Duplicate,    // the same folder listed earlier already
    Missing,      // the folder does not exist, and it is on a connected FIXED drive (safe to call dead)
    Unverifiable, // on a drive that is not connected / removable / a network share — NEVER auto-removed
}

/// <summary>One entry in a PATH, with a verdict on whether it is worth removing.</summary>
public sealed record PathEntry(string Value, PathIssue Issue);

/// <summary>
/// Reads and rewrites the Windows PATH (per-user or machine). Trimming PATH is a real, reversible change: the
/// whole current PATH value is captured into the backup session BEFORE anything is written (the same
/// before-the-change invariant as the tweak engine), so Restore puts back exactly the PATH you had.
///
/// The verdict on each entry is deliberately cautious: a folder is only called <see cref="PathIssue.Missing"/>
/// (dead) when it sits on a connected, FIXED drive and truly is not there. A path on a drive that is not
/// connected, removable, or a network share reads as <see cref="PathIssue.Unverifiable"/> and is never
/// auto-selected for removal — "looks dead" on a disconnected disk is not dead (Oscar's constraint).
/// </summary>
public static class PathService
{
    public const string EntryType = "path";

    // The registry locations of the two PATHs. Settable so a test can point them at a throwaway key and never
    // touch the real machine PATH.
    internal static string MachineKey { get; set; } = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    internal static string UserKey { get; set; } = @"HKCU\Environment";

    public static string KeyFor(PathScope scope) => scope == PathScope.Machine ? MachineKey : UserKey;

    /// <summary>The raw PATH value (env-var tokens NOT expanded), or "" when the value is absent.</summary>
    public static string ReadRaw(PathScope scope)
    {
        try
        {
            var (root, sub) = ParseKey(KeyFor(scope));
            using var key = root.OpenSubKey(sub);
            return key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Splits the PATH into entries and flags each one. Order is preserved.</summary>
    public static List<PathEntry> Analyze(PathScope scope)
    {
        var raw = ReadRaw(scope);
        var result = new List<PathEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';'))
        {
            var value = part.Trim();
            if (value.Length == 0) { result.Add(new PathEntry(part, PathIssue.Empty)); continue; }

            var key = Normalize(value);
            if (!seen.Add(key)) { result.Add(new PathEntry(value, PathIssue.Duplicate)); continue; }

            result.Add(new PathEntry(value, VerifyExists(value)));
        }
        return result;
    }

    private static string Normalize(string p)
    {
        try { p = Environment.ExpandEnvironmentVariables(p); } catch { }
        return p.TrimEnd('\\', '/').Trim();
    }

    // Only a connected FIXED drive lets us call a folder "dead". Anything else is Unverifiable.
    private static PathIssue VerifyExists(string value)
    {
        string expanded;
        try { expanded = Environment.ExpandEnvironmentVariables(value); } catch { return PathIssue.Unverifiable; }
        if (expanded.StartsWith(@"\\")) return PathIssue.Unverifiable; // UNC network share
        try
        {
            var rootPath = Path.GetPathRoot(expanded);
            if (string.IsNullOrEmpty(rootPath)) return PathIssue.Unverifiable;
            var drive = new DriveInfo(rootPath);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return PathIssue.Unverifiable;
            // Directory.Exists would return false for "access denied" or "path too long" just as for "not there",
            // and a Missing entry is pre-ticked for removal — that would flag a folder that DOES exist. Tell the
            // two apart: only a real not-found is Missing; "couldn't tell" is Unverifiable (never auto-removed),
            // the same third state as a disconnected drive. (An unreadable fact must never trigger a removal.)
            try { _ = File.GetAttributes(expanded); return PathIssue.None; }
            catch (DirectoryNotFoundException) { return PathIssue.Missing; }
            catch (FileNotFoundException) { return PathIssue.Missing; }
            catch { return PathIssue.Unverifiable; }
        }
        catch { return PathIssue.Unverifiable; }
    }

    /// <summary>
    /// Writes a new PATH made of <paramref name="keptEntries"/>, after capturing the current PATH into the backup
    /// session and flushing it to disk. Broadcasts the environment change so newly started programs see it.
    /// Returns how many entries were removed.
    /// </summary>
    public static int Apply(PathScope scope, IReadOnlyList<string> keptEntries, BackupSession session, Action flushBeforeChanging)
    {
        var currentRaw = ReadRaw(scope);
        int before = currentRaw.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

        session.Entries.Add(new BackupEntry
        {
            Type = EntryType,
            TweakId = EntryType,
            TweakName = scope == PathScope.Machine ? "PATH (machine)" : "PATH (user)",
            ValueName = scope.ToString().ToLowerInvariant(),
            Existed = true,
            Value = currentRaw,
        });

        flushBeforeChanging();

        var newRaw = string.Join(";", keptEntries.Select(e => e.Trim()).Where(e => e.Length > 0));
        WritePath(scope, newRaw);
        NativeMethods.BroadcastEnvironmentChange();

        int after = newRaw.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
        LogService.Log($"PATH ({scope}): {before} entries -> {after}.");
        return Math.Max(0, before - after);
    }

    /// <summary>Puts a PATH back to exactly what a backup recorded, after validating it is a plausible PATH string.</summary>
    public static void RestoreEntry(BackupEntry entry)
    {
        if (entry.ValueName is null || entry.Value is null) return;
        if (!Enum.TryParse<PathScope>(entry.ValueName, ignoreCase: true, out var scope)) return;
        if (!IsValidPathValue(entry.Value))
            throw new InvalidOperationException("The backup holds an implausible PATH value; refused.");
        WritePath(scope, entry.Value);
        NativeMethods.BroadcastEnvironmentChange();
    }

    // A backup is untrusted input and this runs elevated. PATH is written to the registry (not a command), so
    // there is no shell injection; the guard is against a forged value smuggling control characters. Each
    // segment must be a bounded, printable path-ish string.
    public static bool IsValidPathValue(string? value)
    {
        if (value is null) return false;
        if (value.Length > 32_767) return false; // the registry hard cap for this value
        foreach (var seg in value.Split(';'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            if (s.Length > 4_096) return false;
            if (s.Any(c => c < ' ')) return false; // no control characters
        }
        return true;
    }

    private static void WritePath(PathScope scope, string raw)
    {
        var (root, sub) = ParseKey(KeyFor(scope));
        using var key = root.CreateSubKey(sub, writable: true)
            ?? throw new InvalidOperationException($"Cannot open {KeyFor(scope)}");
        key.SetValue("Path", raw, RegistryValueKind.ExpandString);
    }

    private static (RegistryKey root, string sub) ParseKey(string keyPath)
    {
        int idx = keyPath.IndexOf('\\');
        string hive = idx < 0 ? keyPath : keyPath[..idx];
        string sub = idx < 0 ? "" : keyPath[(idx + 1)..];
        RegistryKey root = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Unknown hive in '{keyPath}'"),
        };
        return (root, sub);
    }
}
