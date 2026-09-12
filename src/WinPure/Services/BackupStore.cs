using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinPure.Services;

/// <summary>
/// Where backups live, and who may write there. WinPure restores backups as administrator, so a backup
/// any program of the user could edit was a way to make WinPure do something as administrator — a
/// review showed a forged file re-enabling SMB 1.0 or a disabled service through a plain Undo. A list
/// of allowed entries cannot tell those apart from a genuine undo; only the file's origin can. So
/// backups live under %ProgramData%, in a folder only SYSTEM and Administrators can write.
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
    /// <summary>OWNER RIGHTS: replaces the implicit right of an owner to change permissions.</summary>
    private static readonly SecurityIdentifier OwnerRightsSid = new("S-1-3-4");

    private const FileSystemRights WriteLike =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// SYSTEM and Administrators in full; the owner only reads, even when the owner is the user's own
    /// account, so an unelevated program of that user cannot grant itself write access later. Nothing
    /// inherited from %ProgramData%, where every user may create files by default.
    /// </summary>
    internal static DirectorySecurity ProtectedSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(OwnerRightsSid, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>True when only SYSTEM and Administrators can write, nothing is inherited, and the owner cannot rewrite the permissions.</summary>
    internal static bool IsProtected(DirectorySecurity security)
    {
        if (!security.AreAccessRulesProtected) return false;
        bool ownerCapped = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid == OwnerRightsSid) ownerCapped = (rule.FileSystemRights & WriteLike) == 0;
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            bool trusted = sid == SystemSid || sid == AdministratorsSid;
            if (!trusted && (rule.FileSystemRights & WriteLike) != 0) return false;
        }
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        bool ownerTrusted = owner is not null && (owner == SystemSid || owner == AdministratorsSid);
        return ownerTrusted || ownerCapped;
    }

    /// <summary>
    /// Makes sure the folder exists with protected permissions. A folder that is already there with other
    /// permissions, or that is a link, was not made by WinPure: it is set aside under a new name and a
    /// fresh one is created, so nothing planted in it is ever read. Throws when it cannot be protected —
    /// and then no backup is written, so nothing is changed.
    /// </summary>
    public static void EnsureProtected(string directory)
    {
        foreach (string path in new[] { Path.GetDirectoryName(directory)!, directory })
        {
            var info = new DirectoryInfo(path);
            if (info.Exists && (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || !IsProtected(info.GetAccessControl())))
            {
                string aside = $"{path}.untrusted-{DateTime.Now:yyyyMMdd-HHmmss}";
                Directory.Move(path, aside);
                LogService.Log($"Backup folder {path} had permissions WinPure did not set; moved aside to {aside} and not read.");
                info = new DirectoryInfo(path);
            }
            if (!info.Exists) info.Create(ProtectedSecurity());
        }
    }

    /// <summary>
    /// Copies backups from the per-user folder older versions used into the protected one, once; the
    /// originals stay where they were. Each file is rewritten rather than copied, so it takes the new
    /// folder's permissions. Returns how many were copied.
    /// </summary>
    public static int MigrateLegacy(string legacyDirectory, string targetDirectory)
    {
        string marker = Path.Combine(targetDirectory, "migrated-from-appdata.txt");
        if (File.Exists(marker) || !Directory.Exists(legacyDirectory)) return 0;

        int copied = 0;
        foreach (string file in Directory.EnumerateFiles(legacyDirectory, "backup_*.json"))
        {
            string destination = Path.Combine(targetDirectory, Path.GetFileName(file));
            if (File.Exists(destination)) continue;
            File.WriteAllText(destination, File.ReadAllText(file));
            copied++;
        }
        File.WriteAllText(marker, $"Copied {copied} backup(s) from {legacyDirectory} on {DateTime.Now:yyyy-MM-dd HH:mm}.");
        LogService.Log($"Copied {copied} backup(s) from {legacyDirectory} into {targetDirectory}; the originals were left in place.");
        return copied;
    }
}
