using System.IO;

namespace WinPure.Services;

/// <summary>
/// Reads and writes the Windows hosts file. Before every save it pushes a NEW timestamped copy into an
/// admin-only backups folder beside hosts (under System32), so no save can ever lose earlier content —
/// "Undo last save" pops the most recent backup, stepping back one save at a time. A non-admin cannot
/// plant a poisoned backup because that folder lives under System32. "Reset to the Windows default"
/// rewrites the stock comment-only hosts (backing the current one up first) — the guaranteed way back.
/// This stays out of the shared Restore page on purpose: hosts content is free-form and cannot be
/// policy-validated the way DNS server lists are, so it keeps its own local, admin-only backup.
/// </summary>
public static class HostsService
{
    // Settable so tests run against a throwaway file instead of the real, admin-only hosts.
    private static string? _pathOverride;
    internal static string? PathForTests { set => _pathOverride = value; }

    public static string HostsPath => _pathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static string BackupDir => Path.Combine(Path.GetDirectoryName(HostsPath)!, "WinPureHostsBackups");

    private const int KeepBackups = 50;   // tidy the folder; a hosts editor never approaches this in practice

    /// <summary>The stock Windows 11 hosts file: comments only, no active mappings.</summary>
    public const string DefaultContent =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "#\r\n" +
        "# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\r\n" +
        "#\r\n" +
        "# This file contains the mappings of IP addresses to host names. Each\r\n" +
        "# entry should be kept on an individual line. The IP address should\r\n" +
        "# be placed in the first column followed by the corresponding host name.\r\n" +
        "# The IP address and the host name should be separated by at least one\r\n" +
        "# space.\r\n" +
        "#\r\n" +
        "# Additionally, comments (such as these) may be inserted on individual\r\n" +
        "# lines or following the machine name denoted by a '#' symbol.\r\n" +
        "#\r\n" +
        "# For example:\r\n" +
        "#\r\n" +
        "#      102.54.94.97     rhino.acme.com          # source server\r\n" +
        "#       38.25.63.10     x.acme.com              # x client host\r\n" +
        "\r\n" +
        "# localhost name resolution is handled within DNS itself.\r\n" +
        "#\t127.0.0.1       localhost\r\n" +
        "#\t::1             localhost\r\n";

    /// <summary>Timestamped backups, newest first (the file name embeds a sortable tick count).</summary>
    public static IReadOnlyList<string> Backups()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return Array.Empty<string>();
            return new DirectoryInfo(BackupDir).GetFiles("hosts-*.bak")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => f.FullName).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static bool HasBackup => Backups().Count > 0;

    /// <summary>Reads the current hosts file. Null means it exists but could not be read — never treat that
    /// as an empty file, or the editor would offer to overwrite content it never saw.</summary>
    public static string? Read()
    {
        try { return File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : DefaultContent; }
        catch { return null; }
    }

    public sealed record Result(bool Ok, string? Error);

    private static void BackUpCurrent()
    {
        if (!File.Exists(HostsPath)) return;
        Directory.CreateDirectory(BackupDir);
        long ticks = DateTime.UtcNow.Ticks;
        string dest;
        int i = 0;
        do { dest = Path.Combine(BackupDir, $"hosts-{ticks:D19}-{i:D3}.bak"); i++; }
        while (File.Exists(dest) && i < 1000);
        File.Copy(HostsPath, dest, overwrite: false);
        Prune();
    }

    private static void Prune()
    {
        try
        {
            var all = Backups();   // newest first
            for (int i = KeepBackups; i < all.Count; i++)
                try { File.Delete(all[i]); } catch { }
        }
        catch { }
    }

    /// <summary>Pushes a fresh backup of the current file, then writes the new content.</summary>
    public static Result Save(string content)
    {
        try
        {
            BackUpCurrent();
            File.WriteAllText(HostsPath, content);
            return new Result(true, null);
        }
        catch (Exception ex) { return new Result(false, ex.Message); }
    }

    /// <summary>Rewrites the stock comment-only hosts (backing up the current one first).</summary>
    public static Result ResetToDefault() => Save(DefaultContent);

    /// <summary>Restores and removes the most recent backup — a multi-level undo of prior saves.</summary>
    public static Result RestoreBackup()
    {
        try
        {
            var newest = Backups().FirstOrDefault();
            if (newest is null) return new Result(false, "There is no WinPure backup of the hosts file to restore.");
            File.Copy(newest, HostsPath, overwrite: true);
            try { File.Delete(newest); } catch { }   // pop, so the next Undo steps one save further back
            return new Result(true, null);
        }
        catch (Exception ex) { return new Result(false, ex.Message); }
    }
}
