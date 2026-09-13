using System.IO;
using System.Runtime.InteropServices;

namespace WinPure.Services;

/// <summary>What a cleanup target removes.</summary>
public enum CleanupKind
{
    /// <summary>Delete the contents of one or more folders (or files matching a glob under them).</summary>
    Paths,
    /// <summary>Empty the Recycle Bin through the shell.</summary>
    RecycleBin,
    /// <summary>Delete a whole folder, taking ownership first if the tree is protected (e.g. Windows.old).</summary>
    Folder,
}

/// <summary>
/// One row on the Cleanup page: a category of junk to delete. Cleaning is NOT reversible — there is no
/// backup for a deleted cache — so these are not TweakActions; the page is a one-shot delete section like
/// Repair, and destructive rows (user data) ask before running.
/// </summary>
public sealed class CleanupTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Icon { get; init; } = "";
    public CleanupKind Kind { get; init; } = CleanupKind.Paths;

    /// <summary>
    /// Folders whose CONTENTS are cleared (the folder itself is kept). Environment variables are expanded,
    /// and a "*" path segment matches every subfolder — so "…\User Data\*\Cache" hits every browser profile.
    /// </summary>
    public string[] Roots { get; init; } = Array.Empty<string>();

    /// <summary>When set, only files matching these globs are deleted (recursively), and folders are left alone.</summary>
    public string[] Globs { get; init; } = Array.Empty<string>();

    /// <summary>Needs an explicit yes (user data, or a big one-way delete). The confirmation defaults to No.</summary>
    public bool NeedsConfirm { get; init; }
}

/// <summary>Measures and deletes what a <see cref="CleanupTarget"/> points at. Skips what it cannot touch.</summary>
public static class CleanupService
{
    public sealed record Result(long Bytes, int Deleted, int Skipped);

    // Recurse into subfolders, silently skipping anything we cannot read — a cache is full of files other
    // programs hold open, and one locked file must never stop the sweep.
    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Whether the row is worth showing at all. A whole-folder target (Windows.old) only applies when the
    /// folder is actually present, so it is hidden on machines that never did a feature update; every other
    /// target always shows (an empty cache is a legitimate "nothing to clean", not an absent feature).
    /// </summary>
    public static bool ShouldShow(CleanupTarget target)
    {
        if (target.Kind != CleanupKind.Folder) return true;
        try { return ExpandRoots(target.Roots).Any(); } catch { return false; }
    }

    /// <summary>Bytes this target would free right now. Never throws; an unreadable target measures as 0.</summary>
    public static long Measure(CleanupTarget target)
    {
        try
        {
            if (target.Kind == CleanupKind.RecycleBin) return RecycleBinBytes();
            // A whole-folder target measures the folder itself (which is deleted, not just cleared).
            long total = 0;
            foreach (var dir in ExpandRoots(target.Roots)) total += MeasureDir(dir, target.Globs);
            return total;
        }
        catch { return 0; }
    }

    /// <summary>Deletes what the target points at. Locked or protected items are counted as skipped, never fatal.</summary>
    public static Result Clean(CleanupTarget target)
    {
        if (target.Kind == CleanupKind.RecycleBin) return EmptyRecycleBin();
        if (target.Kind == CleanupKind.Folder) return CleanFolders(target);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        foreach (var dir in ExpandRoots(target.Roots))
        {
            if (target.Globs.Length == 0)
            {
                // Clear the folder's contents but keep the folder — Windows expects %TEMP% etc. to exist.
                foreach (var entry in SafeEntries(dir))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            long size = MeasureDir(entry, Array.Empty<string>());
                            Directory.Delete(entry, recursive: true);
                            bytes += size; deleted++;
                        }
                        else
                        {
                            long size = SafeLength(entry);
                            File.Delete(entry);
                            bytes += size; deleted++;
                        }
                    }
                    catch { skipped++; }
                }
            }
            else
            {
                foreach (var glob in target.Globs)
                    foreach (var file in SafeFiles(dir, glob))
                    {
                        try { long size = SafeLength(file); File.Delete(file); bytes += size; deleted++; }
                        catch { skipped++; }
                    }
            }
        }
        return new Result(bytes, deleted, skipped);
    }

    private static long MeasureDir(string dir, string[] globs)
    {
        long total = 0;
        try
        {
            var info = new DirectoryInfo(dir);
            if (globs.Length == 0)
            {
                foreach (var f in info.EnumerateFiles("*", Walk)) total += SafeLength(f);
            }
            else
            {
                foreach (var glob in globs)
                    foreach (var f in info.EnumerateFiles(glob, Walk)) total += SafeLength(f);
            }
        }
        catch { }
        return total;
    }

    /// <summary>Every existing folder a pattern names, expanding "*" segments to their subfolders.</summary>
    internal static IEnumerable<string> ExpandRoots(IEnumerable<string> patterns)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
            foreach (var dir in ExpandOne(pattern))
                if (seen.Add(dir)) yield return dir;
    }

    private static IEnumerable<string> ExpandOne(string pattern)
    {
        string full = Environment.ExpandEnvironmentVariables(pattern);
        var parts = full.Split('\\');
        // The drive ("C:") needs its trailing slash to be a real path.
        IEnumerable<string> dirs = new[] { parts[0].EndsWith(":", StringComparison.Ordinal) ? parts[0] + "\\" : parts[0] };
        for (int i = 1; i < parts.Length; i++)
        {
            string seg = parts[i];
            if (seg.Length == 0) continue;
            dirs = dirs.SelectMany(d =>
            {
                try
                {
                    if (seg.Contains('*')) return Directory.EnumerateDirectories(d, seg);
                    string combined = Path.Combine(d, seg);
                    return Directory.Exists(combined) ? new[] { combined } : Enumerable.Empty<string>();
                }
                catch { return Enumerable.Empty<string>(); }
            }).ToList();
        }
        return dirs.Where(d => { try { return Directory.Exists(d); } catch { return false; } });
    }

    private static IEnumerable<string> SafeEntries(string dir)
    {
        try { return Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir, string glob)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles(glob, Walk).Select(f => f.FullName).ToList(); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return 0; }
    }

    /// <summary>Deletes whole folders a <see cref="CleanupKind.Folder"/> target names (e.g. Windows.old).</summary>
    private static Result CleanFolders(CleanupTarget target)
    {
        long bytes = 0;
        int deleted = 0, skipped = 0;
        foreach (var dir in ExpandRoots(target.Roots))
        {
            long size = MeasureDir(dir, Array.Empty<string>());
            if (TryDeleteTree(dir)) { bytes += size; deleted++; continue; }
            // A protected tree — Windows.old is owned by SYSTEM/TrustedInstaller and full of read-only
            // files. Seize ownership by SID (locale-independent, no takeown "/d Y" prompt) and grant
            // Administrators full control, then let PowerShell clear the read-only bit and delete.
            SeizeOwnership(dir);
            PowerShellRunner.Run(
                $"Remove-Item -LiteralPath {PowerShellRunner.Quote(dir)} -Recurse -Force -ErrorAction SilentlyContinue",
                600_000);
            long left = SafeDirExists(dir) ? MeasureDir(dir, Array.Empty<string>()) : 0;
            if (left == 0) { bytes += size; deleted++; }
            else { bytes += Math.Max(0, size - left); skipped++; }
        }
        return new Result(bytes, deleted, skipped);
    }

    private static bool TryDeleteTree(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch { return false; }
    }

    private static bool SafeDirExists(string dir)
    {
        try { return Directory.Exists(dir); } catch { return false; }
    }

    private static void SeizeOwnership(string dir)
    {
        // *S-1-5-32-544 = BUILTIN\Administrators, by SID so this works on any Windows display language.
        PowerShellRunner.Run($"icacls {PowerShellRunner.Quote(dir)} /setowner *S-1-5-32-544 /T /C /Q", 600_000);
        PowerShellRunner.Run($"icacls {PowerShellRunner.Quote(dir)} /grant *S-1-5-32-544:F /T /C /Q", 600_000);
    }

    // ---- Recycle Bin (shell) ----

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);

    private const int SHERB_NOCONFIRMATION = 0x1;
    private const int SHERB_NOPROGRESSUI = 0x2;
    private const int SHERB_NOSOUND = 0x4;

    private static long RecycleBinBytes()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    private static Result EmptyRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        long bytes = SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        long items = info.i64NumItems;
        int result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        // S_OK, or the "already empty" code, both mean nothing is left.
        return result == 0 ? new Result(bytes, (int)items, 0) : new Result(0, 0, 1);
    }
}
