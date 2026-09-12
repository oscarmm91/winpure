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
    /// <summary>
    /// Where snapshots live. Settable so a test harness can point it at a scratch folder
    /// instead of the user's real backups; the app never changes it.
    /// </summary>
    public static string BackupDirectory { get; set; } = BackupStore.DefaultDirectory;

    private static bool _storePrepared;

    /// <summary>
    /// The real backup folder is protected and the old per-user backups copied in, once per run, before
    /// anything is read or written. Tests point BackupDirectory at a scratch folder and skip this.
    /// </summary>
    private static void PrepareStore()
    {
        if (_storePrepared || BackupDirectory != BackupStore.DefaultDirectory) return;
        BackupStore.EnsureProtected(BackupDirectory);
        BackupStore.MigrateLegacy(BackupStore.LegacyDirectory, BackupDirectory);
        _storePrepared = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupSession CreateSession() => new()
    {
        Id = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}",
        CreatedUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Writes the snapshot to disk atomically (temp file + replace), so a crash or a power
    /// cut mid-write can never leave a truncated .json where a good backup used to be.
    /// Throws if it cannot write: the caller must not touch the system without a backup.
    /// </summary>
    public void SaveSession(BackupSession session)
    {
        if (session.Entries.Count == 0 && session.TweakNames.Count == 0) return;
        PrepareStore();   // throws if the folder cannot be protected: then nothing is written, and nothing changed
        Directory.CreateDirectory(BackupDirectory);
        string path = Path.Combine(BackupDirectory, session.Id + ".json");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(tmp, path, overwrite: true);
        bool isNew = session.FilePath is null;
        session.FilePath = path;
        if (isNew) LogService.Log($"Backup opened: {path}");
    }

    /// <summary>
    /// The entries of the most recent snapshot that touched <paramref name="tweakId"/> —
    /// what that tweak found on this machine before it was ever applied. Empty when the
    /// tweak was never applied through WinPure.
    /// </summary>
    public static List<BackupEntry> FindLatestEntriesFor(string tweakId, IEnumerable<BackupSession> sessions)
    {
        foreach (var session in sessions) // already newest-first
        {
            var entries = session.Entries.Where(e => e.TweakId == tweakId).ToList();
            if (entries.Count > 0) return entries;
        }
        return new List<BackupEntry>();
    }

    /// <summary>Restores a set of entries (newest write undone first). Returns failures.</summary>
    public int RestoreEntries(IReadOnlyList<BackupEntry> entries)
    {
        int failures = 0;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            try
            {
                RestoreEntry(entries[i]);
            }
            catch (Exception ex) when (entries[i].Optional)
            {
                // Windows refusing to write back a legacy value is expected, not a failure.
                LogService.Log($"Restore skipped optional {Describe(entries[i])}: {ex.Message}");
            }
            catch (Exception ex)
            {
                failures++;
                LogService.Log($"Restore failed for {Describe(entries[i])}: {ex.Message}");
            }
        }
        return failures;
    }

    public List<BackupSession> ListSessions()
    {
        var sessions = new List<BackupSession>();
        try
        {
            PrepareStore();
        }
        catch (Exception ex)
        {
            // Never fall back to reading an unprotected folder: that is the hole this closes.
            LogService.Log($"Backup folder could not be protected, so no backups are listed: {ex.Message}");
            return sessions;
        }
        if (!Directory.Exists(BackupDirectory)) return sessions;
        int unreadable = 0;
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "backup_*.json"))
        {
            try
            {
                var session = JsonSerializer.Deserialize<BackupSession>(File.ReadAllText(file));
                if (session is null) { unreadable++; continue; }
                session.FilePath = file;
                sessions.Add(session);
            }
            catch (Exception ex)
            {
                // Never silently: a backup that vanishes from the Restore page without a
                // trace is worse than one that shows up broken.
                unreadable++;
                LogService.Log($"Unreadable backup {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (unreadable > 0) LogService.Log($"{unreadable} backup file(s) could not be read and are not listed.");
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
        int failures = RestoreEntries(session.Entries);
        LogService.Log($"Backup restored: {session.Id} ({session.Entries.Count - failures}/{session.Entries.Count} entries)");
        return failures;
    }

    private static void RestoreEntry(BackupEntry entry)
    {
        // Checked before anything runs: backups can be edited without elevation, and this runs elevated.
        if (!BackupEntryPolicy.IsAllowed(entry, out string why))
            throw new InvalidOperationException($"Refused: {why}.");

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
            case "startup-entry":
            {
                if (entry.KeyPath is null || entry.ValueName is null) return;
                if (entry.Existed && entry.Value is not null)
                {
                    // Byte-for-byte what was there, timestamp included.
                    var (root, sub) = ParseHive(entry.KeyPath);
                    using var key = root.CreateSubKey(sub, writable: true)
                        ?? throw new InvalidOperationException($"Cannot open {entry.KeyPath}");
                    key.SetValue(entry.ValueName, Convert.FromHexString(entry.Value), RegistryValueKind.Binary);
                }
                else
                {
                    // There was no override before; an absent value is how Windows says "enabled".
                    RegistryHelper.DeleteValue(entry.KeyPath, entry.ValueName);
                }
                break;
            }
            case "scheduled-task":
            {
                if (entry.TaskPath is null) return;
                // A task that did not exist when we looked must not be conjured into
                // existence — and one that was already disabled stays disabled.
                if (!entry.Existed) return;
                ScheduledTaskAction.SetEnabled(entry.TaskPath, entry.TaskWasEnabled ?? true);
                break;
            }
            case "system-state":
            {
                // Checked before anything runs — see SystemState.Restore.
                SystemState.Restore(entry.ValueName, entry.Value);
                break;
            }
            case "optional-feature":
            {
                // A feature that was not part of this build when we looked is left alone.
                if (!entry.Existed) return;
                OptionalFeatures.Restore(entry.ValueName, entry.Value);
                break;
            }
        }
    }

    private static (RegistryKey root, string subKey) ParseHive(string keyPath)
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

    private static string Describe(BackupEntry e) =>
        e.Type switch
        {
            "registry-value" => $"{e.KeyPath}!{e.ValueName}",
            "startup-entry" => $"startup entry {e.ValueName}",
            "registry-key" => e.KeyPath ?? "?",
            "service" => $"service {e.ServiceName}",
            "scheduled-task" => $"task {e.TaskPath}",
            "system-state" => $"{e.ValueName} setting",
            "optional-feature" => $"Windows feature {e.ValueName}",
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
