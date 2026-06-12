using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// Saves a JSON snapshot of every value WinPure touches before touching it,
/// lists historical snapshots and restores them.
/// </summary>
public sealed class BackupManager
{
    public static string BackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPure", "Backups");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupSession CreateSession() => new()
    {
        Id = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}",
        CreatedUtc = DateTime.UtcNow,
    };

    public void SaveSession(BackupSession session)
    {
        if (session.Entries.Count == 0 && session.TweakNames.Count == 0) return;
        Directory.CreateDirectory(BackupDirectory);
        string path = Path.Combine(BackupDirectory, session.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(session, JsonOptions));
        session.FilePath = path;
        LogService.Log($"Backup saved: {path} ({session.Entries.Count} entries)");
    }

    public List<BackupSession> ListSessions()
    {
        var sessions = new List<BackupSession>();
        if (!Directory.Exists(BackupDirectory)) return sessions;
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "backup_*.json"))
        {
            try
            {
                var session = JsonSerializer.Deserialize<BackupSession>(File.ReadAllText(file));
                if (session is null) continue;
                session.FilePath = file;
                sessions.Add(session);
            }
            catch { /* skip corrupt file */ }
        }
        return sessions.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public void DeleteSession(BackupSession session)
    {
        if (session.FilePath is { } path && File.Exists(path)) File.Delete(path);
        LogService.Log($"Backup deleted: {session.Id}");
    }

    /// <summary>Restores every entry of a snapshot. Returns the number of failures.</summary>
    public int RestoreSession(BackupSession session)
    {
        int failures = 0;
        // restore in reverse order so later writes are undone first
        for (int i = session.Entries.Count - 1; i >= 0; i--)
        {
            try
            {
                RestoreEntry(session.Entries[i]);
            }
            catch (Exception ex)
            {
                failures++;
                LogService.Log($"Restore failed for {Describe(session.Entries[i])}: {ex.Message}");
            }
        }
        LogService.Log($"Backup restored: {session.Id} ({session.Entries.Count - failures}/{session.Entries.Count} entries)");
        return failures;
    }

    private static void RestoreEntry(BackupEntry entry)
    {
        switch (entry.Type)
        {
            case "registry-value":
            {
                if (entry.KeyPath is null || entry.ValueName is null) return;
                if (entry.Existed && entry.Value is not null && entry.Kind is not null)
                {
                    var kind = Enum.Parse<RegistryValueKind>(entry.Kind);
                    RegistryHelper.WriteValue(entry.KeyPath, entry.ValueName,
                        TweakAction.DeserializeValue(entry.Value, kind), kind);
                }
                else
                {
                    RegistryHelper.DeleteValue(entry.KeyPath, entry.ValueName);
                }
                break;
            }
            case "registry-key":
            {
                if (entry.KeyPath is null) return;
                if (entry.Existed)
                    RegistryHelper.CreateKeyWithDefault(entry.KeyPath, entry.Value);
                else
                    RegistryHelper.DeleteKeyTree(entry.KeyPath);
                break;
            }
            case "service":
            {
                if (entry.ServiceName is null || entry.StartMode is null) return;
                ServiceAction.SetStartMode(entry.ServiceName, entry.StartMode.Value);
                break;
            }
            case "scheduled-task":
            {
                if (entry.TaskPath is null) return;
                ScheduledTaskAction.SetEnabled(entry.TaskPath, entry.TaskWasEnabled ?? true);
                break;
            }
        }
    }

    private static string Describe(BackupEntry e) =>
        e.Type switch
        {
            "registry-value" => $"{e.KeyPath}!{e.ValueName}",
            "registry-key" => e.KeyPath ?? "?",
            "service" => $"service {e.ServiceName}",
            "scheduled-task" => $"task {e.TaskPath}",
            _ => e.Type
        };
}

/// <summary>Registry helpers shared by restore (mirrors the protected helpers on TweakAction).</summary>
internal static class RegistryHelper
{
    public static void WriteValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        var (root, sub) = Parse(keyPath);
        using var key = root.CreateSubKey(sub, writable: true)
            ?? throw new InvalidOperationException($"Cannot open {keyPath}");
        key.SetValue(valueName, value, kind);
    }

    public static void DeleteValue(string keyPath, string valueName)
    {
        var (root, sub) = Parse(keyPath);
        using var key = root.OpenSubKey(sub, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public static void DeleteKeyTree(string keyPath)
    {
        var (root, sub) = Parse(keyPath);
        root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }

    public static void CreateKeyWithDefault(string keyPath, string? defaultValue)
    {
        var (root, sub) = Parse(keyPath);
        using var key = root.CreateSubKey(sub, writable: true)!;
        if (defaultValue is not null)
            key.SetValue("", defaultValue, RegistryValueKind.String);
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
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => throw new ArgumentException($"Unknown hive in '{keyPath}'")
        };
        return (root, sub);
    }
}
