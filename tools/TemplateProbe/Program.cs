using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

// Answers the questions that block "apply to future users", without touching the real template.
//
// The design chosen on 2026-09-12 opens the Default profile hive privately with RegLoadAppKey. Microsoft documents that
// RegLoadAppKey fails on a hive whose keys use more than one security descriptor, and the template uses many. So, run
// elevated, this measures on COPIES of C:\Users\Default\NTUSER.DAT (and its .LOG1/.LOG2) in a new folder under %TEMP%:
//   A0. Does loading alone, read-only, change any file of the copy?
//   A.  RegLoadAppKey: does it load? What security do existing and new keys carry? Once the last handle closes, how long
//       until the file is released, is its header clean, and does the change live in the primary file on its own?
//   B.  RegLoadKey under HKEY_USERS (the fallback): the same questions, checked with RegLoadKey too, plus the unload.
// The original is only read; its files are hashed before and after. Nothing else is written, except temporary mounts of
// copies under HKEY_USERS\WinPureTemplateProbe*, unloaded before the tool ends.
//
// What this cannot answer: the security a NEW account's keys end up with. The template cannot hold that account's SID, so
// profile creation rewrites it; only a throwaway local account's first sign-in shows it. The report prints this account's
// security next to the template's for comparison.
//
// Usage, from a terminal opened with "Run as administrator":  dotnet run --project tools\TemplateProbe
internal static class Program
{
    private const int KEY_READ = 0x20019;
    private const int KEY_ALL_ACCESS = 0xF003F;
    private const int REG_PROCESS_APPKEY = 0x1;
    private const string MountName = "WinPureTemplateProbe";
    private static readonly IntPtr HKEY_USERS = new(unchecked((int)0x80000003));
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(30);

    private const AccessControlSections Sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;

    private static readonly StringBuilder Report = new();

    private static void Say(string line)
    {
        Console.WriteLine(line);
        Report.AppendLine(line);
    }

    private static int Main()
    {
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Say($"WinPure template probe, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Windows {Environment.OSVersion.Version}, elevated={elevated}");
        if (!elevated)
        {
            Say("Run this from a terminal opened with 'Run as administrator'. Nothing was done.");
            return 2;
        }

        string template = DefaultProfileHive();
        if (!File.Exists(template))
        {
            Say($"Template not found at {template}. Nothing was done.");
            return 3;
        }

        string templateFolder = Path.GetDirectoryName(template)!;
        var originals = HiveFiles(template).ToDictionary(f => f, Sha256);
        Say($"template: {template}, header {Header(template)}");
        Listing(templateFolder, "template folder", onlyHiveFiles: true);
        Say($"HKEY_USERS now: {string.Join(", ", Registry.Users.GetSubKeyNames())}");

        string work = Path.Combine(Path.GetTempPath(), $"winpure-template-probe-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(work);
        Say($"working copies: {work}");
        try
        {
            Run("[A0]", () => ProbeReadOnlyLoad(CopyHive(template, Path.Combine(work, "A0"))));
            Run("[A]", () => ProbeAppKey(CopyHive(template, Path.Combine(work, "A")), work));
            Run("[B]", () => ProbeMount(CopyHive(template, Path.Combine(work, "B")), work));

            foreach (var (file, hash) in originals)
                Say($"original {Path.GetFileName(file)} untouched: {Sha256(file) == hash}");
            Say($"original last written {File.GetLastWriteTime(template):yyyy-MM-dd HH:mm:ss}");
            Say("Not answered here: the security a new account's keys end up with. Only a throwaway local account's first sign-in shows it.");
        }
        finally
        {
            string reportFile = Path.Combine(work, "report.txt");
            File.WriteAllText(reportFile, Report.ToString());
            Console.WriteLine();
            Console.WriteLine($"Report saved to {reportFile}");
        }
        return 0;
    }

    private static void Run(string tag, Action probe)
    {
        try { probe(); }
        catch (Exception ex) { Say($"{tag} stopped by {ex.GetType().Name}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ A0: does a read-only load change the files?

    private static void ProbeReadOnlyLoad(string hive)
    {
        string folder = Path.GetDirectoryName(hive)!;
        var before = Snapshot(folder);
        int rc = RegLoadAppKeyW(hive, out IntPtr handle, KEY_READ, REG_PROCESS_APPKEY, 0);
        Say($"[A0] RegLoadAppKey(KEY_READ, REG_PROCESS_APPKEY) = {rc} {Message(rc)}");
        if (rc == 0)
        {
            RegCloseKey(handle);
            WaitReleased(hive, "[A0]");
        }
        CompareSnapshots("[A0]", before, Snapshot(folder));
    }

    // ------------------------------------------------------------------ A: RegLoadAppKey

    private static void ProbeAppKey(string hive, string work)
    {
        string folder = Path.GetDirectoryName(hive)!;
        var before = Snapshot(folder);
        int rc = RegLoadAppKeyW(hive, out IntPtr handle, KEY_ALL_ACCESS, REG_PROCESS_APPKEY, 0);
        Say($"[A] RegLoadAppKey(KEY_ALL_ACCESS, REG_PROCESS_APPKEY) = {rc} {Message(rc)}");
        if (rc != 0)
        {
            // Which half is refused: the exclusive flag, or the load itself?
            foreach (var (access, flags, label) in new[] { (KEY_ALL_ACCESS, 0, "KEY_ALL_ACCESS, shared"), (KEY_READ, 0, "KEY_READ, shared") })
            {
                int rc2 = RegLoadAppKeyW(hive, out IntPtr h2, access, flags, 0);
                Say($"[A] RegLoadAppKey({label}) = {rc2} {Message(rc2)}");
                if (rc2 == 0)
                {
                    using (var readOnly = RegistryKey.FromHandle(new SafeRegistryHandle(h2, true)))
                        DescribeTemplate(readOnly, "[A]");
                    WaitReleased(hive, "[A]");
                    break;
                }
            }
            return;
        }

        using (var root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, true)))
        {
            DescribeTemplate(root, "[A]");
            WriteProbe(root, "[A]");
            root.Flush();
        }
        if (!WaitReleased(hive, "[A]")) return;
        CompareSnapshots("[A]", before, Snapshot(folder));
        CheckPrimaryAlone(hive, work, "A", "[A]", mount: false);
    }

    // ------------------------------------------------------------------ B: RegLoadKey under HKEY_USERS

    private static void ProbeMount(string hive, string work)
    {
        Say($"[B] privileges: SeRestorePrivilege={EnablePrivilege("SeRestorePrivilege")}, SeBackupPrivilege={EnablePrivilege("SeBackupPrivilege")}");
        foreach (string name in new[] { MountName, MountName + "Check" })
            if (!ClearLeftoverMount(name)) return;

        string folder = Path.GetDirectoryName(hive)!;
        var before = Snapshot(folder);
        int rc = RegLoadKeyW(HKEY_USERS, MountName, hive);
        Say($"[B] RegLoadKey(HKEY_USERS\\{MountName}) = {rc} {Message(rc)}");
        if (rc != 0) return;

        try
        {
            using var root = Registry.Users.OpenSubKey(MountName, writable: true)
                ?? throw new InvalidOperationException("the mount loaded but cannot be opened");
            DescribeTemplate(root, "[B]");
            WriteProbe(root, "[B]");
            root.Flush();
        }
        finally
        {
            Unload(MountName, "[B]");
        }
        if (!WaitReleased(hive, "[B]")) return;
        CompareSnapshots("[B]", before, Snapshot(folder));
        CheckPrimaryAlone(hive, work, "B", "[B]", mount: true);
    }

    /// <summary>A mount with this tool's name can only be this tool's own, left by an interrupted run: unload it.</summary>
    private static bool ClearLeftoverMount(string name)
    {
        using (var existing = Registry.Users.OpenSubKey(name))
            if (existing is null) return true;
        int rc = RegUnLoadKeyW(HKEY_USERS, name);
        Say($"[B] HKEY_USERS\\{name} was left by an earlier run; unloading it = {rc} {Message(rc)}");
        return rc == 0;
    }

    private static void Unload(string name, string tag)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        int rc = RegUnLoadKeyW(HKEY_USERS, name);
        Say($"{tag} RegUnLoadKey(HKEY_USERS\\{name}) = {rc} {Message(rc)}");
        if (rc != 0)
            Say($"{tag} A COPY stays mounted under HKEY_USERS\\{name} until a restart or the next run. The real template was never mounted.");
    }

    // ------------------------------------------------------------------ what each probe measures

    private static readonly string[] InterestingKeys =
    {
        "",
        "Software",
        @"Software\Classes",
        @"Software\Policies",
        @"Software\Policies\Microsoft\Windows",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        @"Software\Microsoft\Windows\CurrentVersion\Search",
        @"Control Panel\Desktop",
        @"System\GameConfigStore",
    };

    private static void DescribeTemplate(RegistryKey root, string tag)
    {
        Say($"{tag} root subkeys: {string.Join(", ", root.GetSubKeyNames())}");
        foreach (string path in InterestingKeys)
        {
            using var key = path.Length == 0 ? null : root.OpenSubKey(path);
            var target = path.Length == 0 ? root : key;
            if (target is null)
            {
                Say($"{tag}   {Show(path)}: absent in the template");
                continue;
            }
            Say($"{tag}   {Show(path)}: {target.ValueCount} values, {target.SubKeyCount} subkeys");
            Say($"{tag}     template security: {Security(target)}");
            using var mine = path.Length == 0 ? null : Registry.CurrentUser.OpenSubKey(path);
            Say($"{tag}     this account's:    {(path.Length == 0 ? Security(Registry.CurrentUser) : mine is null ? "(absent)" : Security(mine))}");
        }

        // How many different security descriptors the hive uses: what the RegLoadAppKey documentation cares about.
        var descriptors = new Dictionary<string, int>(StringComparer.Ordinal);
        int unreadable = 0;
        bool truncated = false;
        int walked = Walk(root, descriptors, ref unreadable, ref truncated, depth: 0, budget: 20000);
        Say($"{tag} {descriptors.Count} distinct security descriptors (owner, group and DACL; SACLs not compared, so a lower bound) over {walked} keys"
            + $"{(unreadable > 0 ? $", {unreadable} keys unreadable" : "")}{(truncated ? ", walk cut short by its depth or key budget" : "")}:");
        foreach (var (sddl, count) in descriptors.OrderByDescending(d => d.Value))
            Say($"{tag}   {count} keys: {sddl}");
    }

    private static void WriteProbe(RegistryKey root, string tag)
    {
        foreach (string path in new[] { @"Software\WinPureTemplateProbe", @"Software\Policies\WinPureTemplateProbe" })
        {
            try
            {
                using var key = root.CreateSubKey(path, writable: true);
                key.SetValue("Probe", 1, RegistryValueKind.DWord);
                Say($"{tag} wrote {path}!Probe, read back {key.GetValue("Probe")}; new key security in the template: {Security(key)}");
            }
            catch (Exception ex)
            {
                Say($"{tag} writing {path} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Does the change live in the primary file on its own, or only in the .LOG files next to it?</summary>
    private static void CheckPrimaryAlone(string hive, string work, string name, string tag, bool mount)
    {
        string alone = Path.Combine(work, name + "-primary-only");
        Directory.CreateDirectory(alone);
        string copy = Path.Combine(alone, "NTUSER.DAT");
        CopyFile(hive, copy);
        Say($"{tag} primary file copied alone, header {Header(copy)}");

        if (!mount)
        {
            int rc = RegLoadAppKeyW(copy, out IntPtr handle, KEY_READ, REG_PROCESS_APPKEY, 0);
            if (rc != 0)
            {
                Say($"{tag} the primary file alone does not load with RegLoadAppKey: {rc} {Message(rc)}");
                return;
            }
            using (var root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, true)))
            using (var probe = root.OpenSubKey(@"Software\WinPureTemplateProbe"))
                Say($"{tag} primary file alone, read with RegLoadAppKey, holds the probe value: {probe?.GetValue("Probe") ?? "(absent)"}");
            WaitReleased(copy, tag);
            return;
        }

        string check = MountName + "Check";
        int load = RegLoadKeyW(HKEY_USERS, check, copy);
        if (load != 0)
        {
            Say($"{tag} the primary file alone does not load with RegLoadKey: {load} {Message(load)}");
            return;
        }
        try
        {
            using var probe = Registry.Users.OpenSubKey(check + @"\Software\WinPureTemplateProbe");
            Say($"{tag} primary file alone, read with RegLoadKey, holds the probe value: {probe?.GetValue("Probe") ?? "(absent)"}");
        }
        finally
        {
            Unload(check, tag);
        }
    }

    private static int Walk(RegistryKey key, Dictionary<string, int> descriptors, ref int unreadable, ref bool truncated, int depth, int budget)
    {
        int count = 1;
        string? sddl = SddlOrNull(key);
        if (sddl is null) unreadable++;
        else descriptors[sddl] = descriptors.GetValueOrDefault(sddl) + 1;
        if (depth > 12)
        {
            if (key.SubKeyCount > 0) truncated = true;
            return count;
        }
        foreach (string name in SafeSubKeyNames(key))
        {
            if (count >= budget)
            {
                truncated = true;
                break;
            }
            try
            {
                using var child = key.OpenSubKey(name);
                if (child is not null) count += Walk(child, descriptors, ref unreadable, ref truncated, depth + 1, budget - count);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                unreadable++;
            }
        }
        return count;
    }

    // ------------------------------------------------------------------ files

    private static string DefaultProfileHive()
    {
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        string folder = list?.GetValue("Default") as string ?? @"C:\Users\Default";
        return Path.Combine(Environment.ExpandEnvironmentVariables(folder), "NTUSER.DAT");
    }

    private static IEnumerable<string> HiveFiles(string hive) =>
        new[] { hive, hive + ".LOG1", hive + ".LOG2" }.Where(File.Exists);

    private static string CopyHive(string template, string folder)
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "NTUSER.DAT");
        foreach (string file in HiveFiles(template))
            CopyFile(file, Path.Combine(folder, Path.GetFileName(file)));
        return target;
    }

    private static void CopyFile(string source, string target)
    {
        using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            from.CopyTo(to);
        File.SetAttributes(target, FileAttributes.Normal);
    }

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f) ?? f, f =>
        {
            var info = new FileInfo(f);
            string hash;
            try { hash = Sha256(f)[..16]; }
            catch (IOException) { hash = "(locked)"; }
            return $"{info.Length} bytes, {info.LastWriteTime:HH:mm:ss.fff}, {hash}";
        });

    private static void CompareSnapshots(string tag, Dictionary<string, string> before, Dictionary<string, string> after)
    {
        foreach (var (name, state) in after.OrderBy(a => a.Key))
        {
            string change = !before.TryGetValue(name, out var old) ? "NEW" : old == state ? "unchanged" : $"CHANGED (was {old})";
            Say($"{tag}   file {name}: {state} — {change}");
        }
        foreach (string gone in before.Keys.Except(after.Keys))
            Say($"{tag}   file {gone}: gone");
    }

    private static void Listing(string folder, string label, bool onlyHiveFiles)
    {
        foreach (string file in Directory.GetFiles(folder).OrderBy(f => f))
        {
            string name = Path.GetFileName(file);
            if (onlyHiveFiles && !name.StartsWith("NTUSER.DAT", StringComparison.OrdinalIgnoreCase)) continue;
            var info = new FileInfo(file);
            Say($"  {label}: {name}, {info.Length} bytes, last written {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
    }

    /// <summary>Polls until nothing holds the file: an app hive may be unloaded a moment after its last handle closes.</summary>
    private static bool WaitReleased(string path, string tag)
    {
        var clock = Stopwatch.StartNew();
        while (!IsReleased(path))
        {
            if (clock.Elapsed > ReleaseWait)
            {
                Say($"{tag} {Path.GetFileName(path)} still held after {ReleaseWait.TotalSeconds:0} s; its later checks are skipped.");
                return false;
            }
            Thread.Sleep(100);
        }
        Say($"{tag} {Path.GetFileName(path)} released {clock.ElapsedMilliseconds} ms after the last handle; header {Header(path)}");
        return true;
    }

    private static bool IsReleased(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>The regf header's two sequence numbers (offsets 4 and 8): equal means the file is consistent on its own.</summary>
    private static string Header(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[12];
            if (stream.Read(buffer, 0, 12) < 12) return "(too short)";
            string magic = Encoding.ASCII.GetString(buffer, 0, 4);
            uint primary = BitConverter.ToUInt32(buffer, 4), secondary = BitConverter.ToUInt32(buffer, 8);
            return $"{magic} sequence {primary}/{secondary}{(primary == secondary ? " (clean)" : " (DIRTY: changes still in the logs)")}";
        }
        catch (IOException ex) { return $"(unreadable: {ex.Message})"; }
    }

    // ------------------------------------------------------------------ security

    private static string Security(RegistryKey key)
    {
        try
        {
            var security = key.GetAccessControl(Sections);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AuthorizationRule>().ToList();
            int explicitRules = rules.Count(r => !r.IsInherited);
            return $"{security.GetSecurityDescriptorSddlForm(Sections)} (DACL protected={security.AreAccessRulesProtected}, {explicitRules} explicit of {rules.Count} rules)";
        }
        catch (Exception ex) { return $"(unreadable: {ex.GetType().Name})"; }
    }

    private static string? SddlOrNull(RegistryKey key)
    {
        try { return key.GetAccessControl(Sections).GetSecurityDescriptorSddlForm(Sections); }
        catch (Exception) { return null; }
    }

    private static IEnumerable<string> SafeSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
    }

    private static string Show(string path) => path.Length == 0 ? "(root)" : path;

    private static string Message(int code) => code == 0 ? "(success)" : $"({new Win32Exception(code).Message})";

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out IntPtr token)) return false;   // ADJUST_PRIVILEGES | QUERY
        try
        {
            if (!LookupPrivilegeValueW(null, name, out long luid)) return false;
            var state = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 0x2 };                 // SE_PRIVILEGE_ENABLED
            // AdjustTokenPrivileges returns true even when the privilege was not assigned; that case sets error 1300.
            return AdjustTokenPrivileges(token, false, ref state, Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero)
                && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegLoadAppKeyW(string file, out IntPtr hkResult, int samDesired, int options, int reserved);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegLoadKeyW(IntPtr hKey, string subKey, string file);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegUnLoadKeyW(IntPtr hKey, string subKey);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
