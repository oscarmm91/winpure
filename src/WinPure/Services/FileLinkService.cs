using System.Diagnostics;
using System.IO;

namespace WinPure.Services;

/// <summary>The outcome of a move-and-link.</summary>
public sealed record FileLinkResult(bool Ok, string Message);

/// <summary>Filesystem operations behind the move/link page. Swappable so tests never move real data.</summary>
public interface IFileLinkBackend
{
    bool DirectoryExists(string path);
    bool Exists(string path);
    bool IsReparsePoint(string path);
    long FolderSize(string path);
    long FreeSpace(string path);
    /// <summary>robocopy source into dest; returns the exit code (0-7 = success, 8+ = failure).</summary>
    int Robocopy(string source, string dest);
    void DeleteDirectory(string path);
    /// <summary>Creates a junction at linkPath pointing to target.</summary>
    void CreateJunction(string linkPath, string target);
    /// <summary>The path a reparse point (junction/symlink) resolves to, or null if it is not one.</summary>
    string? ReparseTarget(string path);
}

/// <summary>
/// Moves a folder to another drive and leaves a junction behind (so apps still find it) — a safe way to free
/// space on C:. The move is copy-then-verify-then-delete: the source is only removed AFTER robocopy reports
/// success, so an interrupted move never loses data. It refuses to touch system-critical folders and only moves
/// ACROSS volumes (a same-drive "move" would gain nothing and a junction to the same drive is pointless).
/// Judged by effect: the junction must resolve to the new location at the end.
/// </summary>
public static class FileLinkService
{
    internal static IFileLinkBackend Backend { get; set; } = new FileLinkCli();

    internal static IFileLinkBackend Swap(IFileLinkBackend backend)
    {
        var previous = Backend;
        Backend = backend;
        return previous;
    }

    /// <summary>Why a folder must not be moved/linked over — a drive root or a system-critical location.</summary>
    public static string? RejectSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Loc.T("no folder chosen");
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return Loc.T("the path is not valid"); }
        var root = Path.GetPathRoot(full)?.TrimEnd('\\');
        if (root is null) return Loc.T("the path is not valid");
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return Loc.T("a whole drive cannot be moved");

        // Roots WinPure will never move or replace: the drive root, the user-profile root, and WinPure's own folder.
        string[] blockedExactly =
        {
            Path.Combine(root + "\\", "Users"),
            AppContext.BaseDirectory,
        };
        foreach (var p in blockedExactly)
        {
            var b = p?.TrimEnd('\\');
            if (!string.IsNullOrEmpty(b) && string.Equals(full, b, StringComparison.OrdinalIgnoreCase))
                return Loc.T("this is a system folder");
        }

        // Whole system trees are off-limits both as the folder itself and anything under them — moving a piece of
        // Windows, System32 or Program Files (or junctioning over it) can break Windows or installed software.
        string[] blockedTrees =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        foreach (var p in blockedTrees)
        {
            var b = p?.TrimEnd('\\');
            if (string.IsNullOrEmpty(b)) continue;
            if (string.Equals(full, b, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase))
                return Loc.T("this is a system folder");
        }
        return null;
    }

    /// <summary>
    /// Moves <paramref name="source"/> under <paramref name="destinationParent"/> (on another drive) and puts a
    /// junction at the old path. Copy → verify → delete → link, so the source survives any failure before the copy
    /// is confirmed.
    /// </summary>
    public static FileLinkResult MoveToAnotherDrive(string source, string destinationParent)
    {
        var reject = RejectSource(source);
        if (reject is not null) return new(false, reject);
        if (!Backend.DirectoryExists(source)) return new(false, Loc.T("the folder does not exist"));
        if (Backend.IsReparsePoint(source)) return new(false, Loc.T("that folder is already a link"));
        if (!Backend.DirectoryExists(destinationParent)) return new(false, Loc.T("the destination does not exist"));

        string sourceFull = Path.GetFullPath(source).TrimEnd('\\');
        string name = Path.GetFileName(sourceFull);
        string dest = Path.Combine(Path.GetFullPath(destinationParent).TrimEnd('\\'), name);

        var srcRoot = Path.GetPathRoot(sourceFull);
        var dstRoot = Path.GetPathRoot(dest);
        if (string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
            return new(false, Loc.T("choose a destination on a DIFFERENT drive"));
        if (Backend.Exists(dest)) return new(false, Loc.T("the destination already has a folder with that name"));

        long size = Backend.FolderSize(sourceFull);
        if (Backend.FreeSpace(dest) < size) return new(false, Loc.T("not enough free space on the destination drive"));

        int code = Backend.Robocopy(sourceFull, dest);
        if (code >= 8) return new(false, Loc.F("the copy failed (robocopy {0}); nothing was deleted", code));
        if (!Backend.DirectoryExists(dest)) return new(false, Loc.T("the copy did not produce the destination; nothing was deleted"));

        // Only now, with a verified copy in place, is the original removed and replaced by a junction.
        Backend.DeleteDirectory(sourceFull);
        Backend.CreateJunction(sourceFull, dest);

        var resolved = Backend.ReparseTarget(sourceFull);
        if (resolved is null || !string.Equals(resolved.TrimEnd('\\'), dest.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return new(false, Loc.T("the folder was moved but the junction could not be verified — see the log"));
        return new(true, Loc.F("Moved to {0} and linked.", dest));
    }
}

/// <summary>Real filesystem operations. Never exercised by tests — they swap a fake.</summary>
internal sealed class FileLinkCli : IFileLinkBackend
{
    public bool DirectoryExists(string path) { try { return Directory.Exists(path); } catch { return false; } }
    public bool Exists(string path) { try { return Directory.Exists(path) || File.Exists(path); } catch { return false; } }

    public bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
    }

    public long FolderSize(string path)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(f).Length; } catch { }
            return total;
        }
        catch { return 0; }
    }

    public long FreeSpace(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace; } catch { return long.MaxValue; }
    }

    public int Robocopy(string source, string dest)
    {
        // /E all subfolders, /COPYALL data+attrs+ACLs, /R:1 /W:1 minimal retries, /NP no per-file percent.
        var (code, _) = Run("robocopy.exe", source, dest, "/E", "/COPYALL", "/R:1", "/W:1", "/NP", "/NFL", "/NDL");
        return code;
    }

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public void CreateJunction(string linkPath, string target)
    {
        // A junction is made with mklink /J (a cmd builtin). The paths come from folder pickers and are quoted;
        // Windows forbids the " character in paths, so the quotes cannot be broken — no command injection.
        var (code, output) = Run("cmd.exe", "/c", "mklink", "/J", linkPath, target);
        if (code != 0) throw new InvalidOperationException($"mklink failed: {output.Trim()}");
    }

    public string? ReparseTarget(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0) return null;
            return info.LinkTarget;
        }
        catch { return null; }
    }

    private static (int code, string output) Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi);
        if (process is null) return (-1, "process did not start");
        // Drain both streams while waiting, so robocopy's output cannot fill a buffer and deadlock a blocking read.
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string output = outTask.GetAwaiter().GetResult() + errTask.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }
}
