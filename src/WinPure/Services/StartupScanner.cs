using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// Finds everything that starts with Windows: Run keys (64- and 32-bit, per-user and
/// machine-wide), both Startup folders, and scheduled tasks that fire at logon.
///
/// Enabled/disabled state comes from the Explorer\StartupApproved\* mirror keys — the same
/// place Task Manager reads and writes, so the two always agree. A source with no mirror
/// value is enabled: that is the pre-Windows-8 default Windows still honours.
/// </summary>
public static class StartupScanner
{
    private const string RunSuffix = @"Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>One Run key and the mirror that holds its on/off bits.</summary>
    private readonly record struct RunLocation(string RunKey, string ApprovedKey, string Scope);

    private static readonly RunLocation[] RunLocations =
    {
        new($@"HKCU\Software\{RunSuffix}", $@"HKCU\Software\{ApprovedRoot}\Run", "This user"),
        new($@"HKLM\SOFTWARE\{RunSuffix}", $@"HKLM\SOFTWARE\{ApprovedRoot}\Run", "All users"),
        // 32-bit installers write under WOW6432Node, and their mirror is Run32 — NOT Run.
        // Verified on 2026-09-12: "Adobe Creative Cloud" lives in WOW6432Node\Run and its
        // bit lives in StartupApproved\Run32. Writing to Run would silently do nothing.
        new($@"HKLM\SOFTWARE\WOW6432Node\{RunSuffix}", $@"HKLM\SOFTWARE\{ApprovedRoot}\Run32", "All users (32-bit)"),
    };

    /// <summary>Every StartupApproved key the scanner reads and writes — the only ones a backup may restore a switch into.</summary>
    internal static IEnumerable<string> ApprovedKeys =>
        RunLocations.Select(l => l.ApprovedKey)
            .Append($@"HKCU\Software\{ApprovedRoot}\StartupFolder")
            .Append($@"HKLM\SOFTWARE\{ApprovedRoot}\StartupFolder");

    public static List<StartupEntry> Scan(ScanContext? ctx = null)
    {
        var entries = new List<StartupEntry>();
        foreach (var location in RunLocations)
            entries.AddRange(FromRunKey(location));

        entries.AddRange(FromStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            $@"HKCU\Software\{ApprovedRoot}\StartupFolder", "This user"));
        entries.AddRange(FromStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            $@"HKLM\SOFTWARE\{ApprovedRoot}\StartupFolder", "All users"));

        if (ctx is not null)
            entries.AddRange(FromLogonTasks(ctx));

        return entries
            .OrderBy(e => e.Source)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------- sources

    private static IEnumerable<StartupEntry> FromRunKey(RunLocation location)
    {
        var (root, sub) = Parse(location.RunKey);
        using var key = root.OpenSubKey(sub);
        if (key is null) yield break;

        foreach (string name in key.GetValueNames())
        {
            if (name.Length == 0) continue;
            string command = key.GetValue(name)?.ToString() ?? "";
            string? exe = ResolveExecutable(command);

            yield return new StartupEntry
            {
                Id = $"startup:run:{location.RunKey}:{name}",
                Name = FriendlyName(exe) ?? name,
                EntryName = name,
                Publisher = Publisher(exe),
                Command = command,
                ExecutablePath = exe,
                Source = StartupSource.RegistryRun,
                Scope = location.Scope,
                Enabled = IsEnabled(location.ApprovedKey, name),
                IsOrphan = exe is not null && !File.Exists(exe),
                ApprovedKeyPath = location.ApprovedKey,
            };
        }
    }

    private static IEnumerable<StartupEntry> FromStartupFolder(string folder, string approvedKey, string scope)
    {
        if (folder.Length == 0 || !Directory.Exists(folder)) yield break;

        foreach (string path in Directory.EnumerateFiles(folder))
        {
            string fileName = Path.GetFileName(path);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            // Shortcuts are the normal case; a .bat or .exe dropped straight in works too and
            // Windows keys its bit off the same file name.
            string? target = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? null : path;

            yield return new StartupEntry
            {
                Id = $"startup:folder:{scope}:{fileName}",
                Name = FriendlyName(target) ?? Path.GetFileNameWithoutExtension(fileName),
                EntryName = fileName,
                Publisher = Publisher(target),
                Command = path,
                ExecutablePath = target,
                Source = StartupSource.StartupFolder,
                Scope = scope,
                Enabled = IsEnabled(approvedKey, fileName),
                ApprovedKeyPath = approvedKey,
            };
        }
    }

    private static IEnumerable<StartupEntry> FromLogonTasks(ScanContext ctx)
    {
        foreach (var task in ctx.LogonTasks)
        {
            yield return new StartupEntry
            {
                Id = $"startup:task:{task.Path}",
                Name = task.Path[(task.Path.LastIndexOf('\\') + 1)..],
                EntryName = task.Path,
                Publisher = task.Author,
                Command = task.Action.Length > 0 ? task.Action : task.Path,
                Source = StartupSource.ScheduledTask,
                Scope = "Scheduled task",
                Enabled = task.Enabled,
                TaskPath = task.Path,
            };
        }
    }

    // ---------------------------------------------------------------- state

    /// <summary>
    /// Reads the StartupApproved bit. No value at all means enabled — Windows keeps that
    /// default for anything an installer wrote and nobody has toggled since.
    /// </summary>
    public static bool IsEnabled(string approvedKeyPath, string entryName)
    {
        byte[]? raw = ReadApproved(approvedKeyPath, entryName);
        if (raw is null || raw.Length == 0) return true;
        return (raw[0] & 0x02) != 0;
    }

    public static byte[]? ReadApproved(string approvedKeyPath, string entryName)
    {
        try
        {
            var (root, sub) = Parse(approvedKeyPath);
            using var key = root.OpenSubKey(sub);
            return key?.GetValue(entryName) as byte[];
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- presentation

    /// <summary>Pulls the executable out of a Run command line: "C:\a b\x.exe" --flag → C:\a b\x.exe</summary>
    internal static string? ResolveExecutable(string command)
    {
        string s = command.Trim();
        if (s.Length == 0) return null;

        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }

        // Unquoted: the path may still contain spaces, so take the longest prefix that exists.
        int at = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (at > 0) return s[..(at + 4)];

        int space = s.IndexOf(' ');
        return space < 0 ? s : s[..space];
    }

    private static string? FriendlyName(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            string name = info.FileDescription ?? "";
            if (name.Trim().Length == 0) name = info.ProductName ?? "";
            return name.Trim().Length == 0 ? null : name.Trim();
        }
        catch { return null; }
    }

    private static string Publisher(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return "";
        try { return FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static (RegistryKey root, string subKey) Parse(string keyPath)
    {
        int idx = keyPath.IndexOf('\\');
        string hive = idx < 0 ? keyPath : keyPath[..idx];
        string sub = idx < 0 ? "" : keyPath[(idx + 1)..];
        RegistryKey root = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Unexpected hive in '{keyPath}'"),
        };
        return (root, sub);
    }
}
