using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace WinPure.Services;

/// <summary>
/// Where backups live, and how WinPure knows a backup is its own. WinPure restores backups as
/// administrator, so a backup any program of the user could edit was a way to make WinPure do something
/// as administrator — a review showed a forged file re-enabling SMB 1.0 or a disabled service through a
/// plain Undo. A list of allowed entries cannot tell those apart from a genuine undo (BackupEntryPolicy);
/// only the file's ORIGIN can, and only origin is trusted here.
///
/// The origin record is the NTFS owner. The one property an unelevated program cannot forge is an owner
/// of BUILTIN\Administrators or SYSTEM: measured on this machine, an unelevated token is refused when it
/// tries to create or re-own any object with either owner (ERROR_INVALID_OWNER). So WinPure creates its
/// store folders — and every backup file — owned by Administrators with a DACL only SYSTEM and
/// Administrators can write, and trusts a folder or file ONLY when its owner is one of those two and no
/// other account has a write-like right. A folder owned by the user (however its DACL is shaped, an
/// OWNER RIGHTS "cap" included) is therefore untrusted: it is set aside under a new name, never read.
///
/// This replaces an earlier rule that trusted the DACL's shape and let an OWNER RIGHTS ACE stand in for
/// the owner. That was forgeable: under %ProgramData% any user may create a subfolder, own it, and give
/// it WinPure's exact permissions; an inherit-only OWNER RIGHTS ACE also fooled the old cap check.
/// </summary>
internal static class BackupStore
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinPure", "Backups");

    /// <summary>Where versions before 2026-09-12 kept backups, per user. Read once, to copy from.</summary>
    public static string LegacyDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPure", "Backups");

    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    // The raw generic bits .NET surfaces on some ACEs instead of mapping them: GENERIC_WRITE and
    // GENERIC_ALL. A DACL that grants Users GENERIC_WRITE would slip past a mask built from the specific
    // rights alone (measured: CREATOR OWNER GA reads as 0x10000000, Users GW as 0x40000000).
    private const FileSystemRights GenericWrite = (FileSystemRights)0x40000000;
    private const FileSystemRights GenericAll = (FileSystemRights)0x10000000;

    private const FileSystemRights WriteLike =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | GenericWrite | GenericAll;

    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    /// <summary>
    /// Owner = Administrators (only an elevated token can set this), a protected DACL of SYSTEM and
    /// Administrators in full, nothing inherited from %ProgramData% where every user may create files.
    /// </summary>
    internal static DirectorySecurity ProtectedSecurity()
    {
        var security = new DirectorySecurity();
        security.SetOwner(AdministratorsSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>The same, for a backup file: owner Administrators, protected, SYSTEM and Administrators in full.</summary>
    internal static FileSecurity ProtectedFileSecurity()
    {
        var security = new FileSecurity();
        security.SetOwner(AdministratorsSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// The reason a security descriptor is not one WinPure wrote, or null when it is trusted: the owner is
    /// SYSTEM or Administrators, and no other account has a write-like right (checking explicit and
    /// inherited ACEs and every inheritance/propagation flag). Folders must also have a protected DACL;
    /// files may inherit the folder's, so <paramref name="requireProtected"/> is false for them.
    /// </summary>
    internal static string? Distrust(FileSystemSecurity security, bool requireProtected)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner)
            return "the owner could not be read";
        if (owner != SystemSid && owner != AdministratorsSid)
            return $"owned by {owner}, not SYSTEM or Administrators";
        if (requireProtected && !security.AreAccessRulesProtected)
            return "its permissions are inherited, not the ones WinPure sets";
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid == SystemSid || sid == AdministratorsSid) continue;
            if ((rule.FileSystemRights & WriteLike) != 0) return $"{sid} can write to it";
        }
        return null;
    }

    /// <summary>
    /// Makes sure the parent and the Backups folder exist, owned by Administrators and writable only by
    /// SYSTEM and Administrators. A folder that exists with a different owner (or a link, or a file where a
    /// folder should be) was not made by WinPure: it is set aside under a new name and a fresh one is
    /// created, so nothing planted in it is ever read. A descriptor that cannot be READ is a third state:
    /// it throws (nothing set aside, nothing changed), never counted as untrusted. After creating, it
    /// judges again and throws if still untrusted — the case where the process is not really elevated, or
    /// something raced the creation. Then no backup is written and nothing is changed.
    /// </summary>
    public static void EnsureProtected(string directory)
    {
        foreach (string path in new[] { Path.GetDirectoryName(directory)!, directory })
        {
            if (Path.Exists(path))
            {
                // JudgeExisting throws when it cannot read the descriptor: unreadable is not untrusted.
                string? why = JudgeExisting(path);
                if (why is not null)
                {
                    // A random suffix: a predictable aside name a user pre-creates would block the move.
                    string aside = $"{path}.untrusted-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..(path.Length + 25)];
                    if (File.Exists(path)) File.Move(path, aside);
                    else Directory.Move(path, aside);
                    LogService.Log($"Backup folder {path} was not written by WinPure ({why}); moved aside to {aside} and not read.");
                }
            }

            if (!Directory.Exists(path)) new DirectoryInfo(path).Create(ProtectedSecurity());

            string? stillWrong = JudgeExisting(path);
            if (stillWrong is not null)
                throw new InvalidOperationException(
                    $"The backup folder {path} could not be made administrator-owned ({stillWrong}); WinPure needs to run elevated. Nothing was changed.");
        }
    }

    /// <summary>The reason an existing path is not a folder WinPure wrote, or null. Throws if the descriptor is unreadable.</summary>
    private static string? JudgeExisting(string path)
    {
        if (File.Exists(path)) return "it is a file, not a folder";
        var info = new DirectoryInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return "it is a link";
        // No catch: a descriptor we cannot read is "unknown", the caller must stop, not set aside.
        return Distrust(info.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), requireProtected: true);
    }

    /// <summary>
    /// Writes a backup file into the store, owned by Administrators, atomically (temp file + replace). Only
    /// an elevated process can create it with that owner; an unelevated one is refused, so nothing is written.
    /// </summary>
    internal static void WriteProtected(string path, string text)
    {
        string tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var stream = new FileInfo(tmp).Create(
                   FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadData, FileShare.None, 4096, FileOptions.None, ProtectedFileSecurity()))
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Reads a backup file only if it too is owned by Administrators/SYSTEM and no one else can write it,
    /// judged from the SAME handle its content is read from, so a swap between the check and the read gets
    /// nothing. Returns the reason it was refused, or null with the text.
    /// </summary>
    internal static string? TryReadTrusted(string file, out string text)
    {
        text = "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        string? why = Distrust(stream.GetAccessControl(), requireProtected: false);
        if (why is not null) return why;
        using var reader = new StreamReader(stream);
        text = reader.ReadToEnd();
        return null;
    }

    /// <summary>
    /// Copies backups from the per-user folder older versions used into the protected store, once; the
    /// originals stay where they were. Each file is rewritten (owned by Administrators when
    /// <paramref name="protectedWrites"/>) so it carries the store's origin. A file that cannot be READ is
    /// logged and skipped, never fatal: a locked or unreadable legacy file must not brick the store on
    /// every run. A file that cannot be WRITTEN into the store still throws. Returns how many were copied.
    /// </summary>
    public static int MigrateLegacy(string legacyDirectory, string targetDirectory, bool protectedWrites)
    {
        string marker = Path.Combine(targetDirectory, "migrated-from-appdata.txt");
        if (File.Exists(marker) || !Directory.Exists(legacyDirectory)) return 0;

        int copied = 0, skipped = 0;
        foreach (string file in Directory.EnumerateFiles(legacyDirectory, "backup_*.json"))
        {
            string destination = Path.Combine(targetDirectory, Path.GetFileName(file));
            if (File.Exists(destination)) continue;
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                skipped++;
                LogService.Log($"Legacy backup {Path.GetFileName(file)} could not be read, skipped: {ex.Message}");
                continue;
            }
            if (protectedWrites) WriteProtected(destination, text);
            else File.WriteAllText(destination, text);
            copied++;
        }
        string markerText = $"Copied {copied} backup(s) from {legacyDirectory} on {DateTime.Now:yyyy-MM-dd HH:mm}" +
                            (skipped > 0 ? $"; {skipped} could not be read and were skipped." : ".");
        if (protectedWrites) WriteProtected(marker, markerText); else File.WriteAllText(marker, markerText);
        LogService.Log($"Copied {copied} backup(s) from {legacyDirectory} into {targetDirectory}" +
                       (skipped > 0 ? $"; {skipped} skipped" : "") + "; the originals were left in place.");
        return copied;
    }
}
