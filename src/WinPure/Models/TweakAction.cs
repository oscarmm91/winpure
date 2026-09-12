using Microsoft.Win32;
using WinPure.Services;

namespace WinPure.Models;

/// <summary>A single reversible operation. A tweak bundles one or more actions.</summary>
public abstract class TweakAction
{
    /// <summary>True if the system already matches the tweaked state. Null = cannot tell.</summary>
    public abstract bool? IsApplied(ScanContext ctx);

    /// <summary>
    /// Records the current state into the backup list. MUST NOT change anything: the engine
    /// calls this first and flushes the snapshot to disk before calling <see cref="Apply"/>,
    /// so that a crash mid-batch still leaves a usable backup on disk.
    /// </summary>
    public abstract void Capture(Tweak tweak, List<BackupEntry> backup);

    /// <summary>Applies the change. Always preceded by <see cref="Capture"/>.</summary>
    public abstract void Apply();

    /// <summary>
    /// Stock Windows default. Only a FALLBACK for the toggle-off path when no backup of
    /// this tweak exists — the engine prefers the real captured value whenever it has one,
    /// because these defaults are hand-written in the catalog and may not match this machine.
    /// </summary>
    public abstract void RevertToDefault();

    // ---------- shared registry helpers ----------

    protected static (RegistryKey root, string subKey) ParseKey(string keyPath)
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

    protected static object? ReadValue(string keyPath, string valueName, out RegistryValueKind kind)
    {
        kind = RegistryValueKind.None;
        var (root, sub) = ParseKey(keyPath);
        using var key = root.OpenSubKey(sub);
        if (key is null) return null;
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null) kind = key.GetValueKind(valueName);
        return value;
    }

    protected static void WriteValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        var (root, sub) = ParseKey(keyPath);
        using var key = root.CreateSubKey(sub, writable: true)
            ?? throw new InvalidOperationException($"Cannot open or create {keyPath}");
        key.SetValue(valueName, value, kind);
    }

    protected static void DeleteValue(string keyPath, string valueName)
    {
        var (root, sub) = ParseKey(keyPath);
        using var key = root.OpenSubKey(sub, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    protected static void DeleteKeyTree(string keyPath)
    {
        var (root, sub) = ParseKey(keyPath);
        root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }

    protected static bool KeyExists(string keyPath)
    {
        var (root, sub) = ParseKey(keyPath);
        using var key = root.OpenSubKey(sub);
        return key is not null;
    }

    internal static string SerializeValue(object value) => value switch
    {
        int i => ((uint)i).ToString(),
        long l => l.ToString(),
        string s => s,
        _ => value.ToString() ?? ""
    };

    internal static object DeserializeValue(string raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)uint.Parse(raw)),
        RegistryValueKind.QWord => long.Parse(raw),
        _ => raw
    };
}

/// <summary>Sets a registry value; reverting either restores a default value or deletes it.</summary>
public sealed class RegistryValueAction : TweakAction
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
    public required object ApplyValue { get; init; }
    /// <summary>Stock default. Null = the value does not exist on a stock system → delete on revert.</summary>
    public object? DefaultValue { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        try
        {
            var current = ReadValue(KeyPath, ValueName, out _);
            if (current is null) return false;
            return ValuesEqual(current, ApplyValue);
        }
        catch { return null; }
    }

    public override void Capture(Tweak tweak, List<BackupEntry> backup)
    {
        var current = ReadValue(KeyPath, ValueName, out var currentKind);
        backup.Add(new BackupEntry
        {
            Type = "registry-value",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            KeyPath = KeyPath,
            ValueName = ValueName,
            Existed = current is not null,
            Kind = current is null ? Kind.ToString() : currentKind.ToString(),
            Value = current is null ? null : SerializeValue(current),
        });
    }

    public override void Apply() => WriteValue(KeyPath, ValueName, ApplyValue, Kind);

    public override void RevertToDefault()
    {
        if (DefaultValue is null) DeleteValue(KeyPath, ValueName);
        else WriteValue(KeyPath, ValueName, DefaultValue, Kind);
    }

    private static bool ValuesEqual(object a, object b)
    {
        if (a is int ia && b is int ib) return ia == ib;
        return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Deletes a registry key (e.g. a shell context-menu handler) or creates one
/// (e.g. the classic-context-menu CLSID). Revert recreates/deletes accordingly.
/// </summary>
public sealed class RegistryKeyAction : TweakAction
{
    public required string KeyPath { get; init; }
    /// <summary>True = applying deletes the key. False = applying creates it.</summary>
    public bool DeleteOnApply { get; init; } = true;
    /// <summary>Default value (unnamed) data used when (re)creating the key.</summary>
    public string? KeyDefaultValue { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        try { return KeyExists(KeyPath) != DeleteOnApply; }
        catch { return null; }
    }

    public override void Capture(Tweak tweak, List<BackupEntry> backup)
    {
        bool existed = KeyExists(KeyPath);
        string? originalDefault = null;
        if (existed)
        {
            var (root, sub) = ParseKey(KeyPath);
            using var key = root.OpenSubKey(sub);
            originalDefault = key?.GetValue("") as string;
        }
        backup.Add(new BackupEntry
        {
            Type = "registry-key",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            KeyPath = KeyPath,
            Existed = existed,
            Value = originalDefault,
        });
    }

    public override void Apply()
    {
        if (DeleteOnApply)
        {
            DeleteKeyTree(KeyPath);
        }
        else
        {
            var (root, sub) = ParseKey(KeyPath);
            using var key = root.CreateSubKey(sub, writable: true)!;
            key.SetValue("", KeyDefaultValue ?? "", RegistryValueKind.String);
        }
    }

    public override void RevertToDefault()
    {
        if (DeleteOnApply)
        {
            // restore the stock key with its known default value
            var (root, sub) = ParseKey(KeyPath);
            using var key = root.CreateSubKey(sub, writable: true)!;
            if (KeyDefaultValue is not null)
                key.SetValue("", KeyDefaultValue, RegistryValueKind.String);
        }
        else
        {
            DeleteKeyTree(KeyPath);
        }
    }
}

/// <summary>Disables a Windows service (registry Start = 4 + stop). Revert restores the stock start mode.</summary>
public sealed class ServiceAction : TweakAction
{
    public required string ServiceName { get; init; }
    /// <summary>2 = Automatic, 3 = Manual, 4 = Disabled.</summary>
    public required int DefaultStartMode { get; init; }

    private string ServiceKey => $@"HKLM\SYSTEM\CurrentControlSet\Services\{ServiceName}";

    public override bool? IsApplied(ScanContext ctx)
    {
        try
        {
            var start = ReadValue(ServiceKey, "Start", out _);
            if (start is null) return true; // service not present → nothing to disable
            return (int)start == 4;
        }
        catch { return null; }
    }

    public override void Capture(Tweak tweak, List<BackupEntry> backup)
    {
        var start = ReadValue(ServiceKey, "Start", out _);
        if (start is null) return; // not installed on this edition → nothing to back up
        backup.Add(new BackupEntry
        {
            Type = "service",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            ServiceName = ServiceName,
            StartMode = (int)start,
            Existed = true,
        });
    }

    public override void Apply()
    {
        if (ReadValue(ServiceKey, "Start", out _) is null) return; // not installed
        WriteValue(ServiceKey, "Start", 4, Microsoft.Win32.RegistryValueKind.DWord);
        // The start mode above is what actually sticks across reboots. Stopping it now is
        // best-effort: a busy service with dependents may refuse, and that is not a failure
        // of the tweak — but it must not be swallowed either.
        var stop = PowerShellRunner.Run($"Stop-Service -Name '{ServiceName}' -Force -ErrorAction Stop", 60_000);
        if (!stop.Success)
            LogService.Log($"Service {ServiceName} set to Disabled but could not be stopped now (takes effect on reboot): {stop.Error}");
    }

    public override void RevertToDefault() => SetStartMode(ServiceName, DefaultStartMode);

    internal static void SetStartMode(string serviceName, int startMode)
    {
        WriteValue($@"HKLM\SYSTEM\CurrentControlSet\Services\{serviceName}", "Start",
            startMode, Microsoft.Win32.RegistryValueKind.DWord);
        // Only Automatic (2) gets started back up; Manual (3) is on-demand by definition.
        if (startMode == 2)
        {
            var start = PowerShellRunner.Run($"Start-Service -Name '{serviceName}' -ErrorAction Stop", 60_000);
            if (!start.Success)
                LogService.Log($"Service {serviceName} restored to start mode {startMode} but could not be started now: {start.Error}");
        }
    }
}

/// <summary>Disables a scheduled task. Revert re-enables it.</summary>
public sealed class ScheduledTaskAction : TweakAction
{
    /// <summary>Full path, e.g. \Microsoft\Windows\Feedback\Siuf\DmClient</summary>
    public required string TaskPath { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        // Unknown unless the task query actually worked — otherwise a failed lookup would
        // read as "already disabled" and the tweak would claim to be applied.
        if (!ctx.Loaded || !ctx.TasksQueryOk) return null;
        return !ctx.TaskEnabled.TryGetValue(TaskPath, out bool enabled) || !enabled;
    }

    public override void Capture(Tweak tweak, List<BackupEntry> backup)
    {
        // Measure the real state instead of assuming it was enabled: several of these tasks
        // ship disabled (or missing) on 24H2/25H2, and "restoring" them to enabled would
        // turn ON telemetry the user never had running.
        var (exists, enabled) = ReadState(TaskPath);
        backup.Add(new BackupEntry
        {
            Type = "scheduled-task",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            TaskPath = TaskPath,
            TaskWasEnabled = enabled,
            Existed = exists,
        });
    }

    public override void Apply() => SetEnabled(TaskPath, false);

    public override void RevertToDefault() => SetEnabled(TaskPath, true);

    /// <summary>(exists, enabled) for a task path. A missing task reports (false, false).</summary>
    internal static (bool exists, bool enabled) ReadState(string taskPath)
    {
        var (dir, name) = Split(taskPath);
        // $$ raw string: {{x}} interpolates, single braces stay literal for PowerShell.
        var result = PowerShellRunner.Run($$"""
            $t = Get-ScheduledTask -TaskPath '{{dir}}' -TaskName '{{name}}' -ErrorAction SilentlyContinue
            if (-not $t) { 'missing' } else { $t.State.ToString() }
            """, 30_000);
        string state = result.Output.Trim();
        if (!result.Success || state.Length == 0 || state == "missing") return (false, false);
        return (true, !state.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
    }

    internal static void SetEnabled(string taskPath, bool enabled)
    {
        var (dir, name) = Split(taskPath);
        string verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
        // A task that does not exist is not an error (Windows drops these between builds);
        // a task that exists and refuses to change IS one, and must not be reported as success.
        PowerShellRunner.RunOrThrow($$"""
            $ErrorActionPreference = 'Stop'
            $t = Get-ScheduledTask -TaskPath '{{dir}}' -TaskName '{{name}}' -ErrorAction SilentlyContinue
            if (-not $t) { exit 0 }
            {{verb}} -TaskPath '{{dir}}' -TaskName '{{name}}' | Out-Null
            """, $"{(enabled ? "Enabling" : "Disabling")} task {taskPath}", 60_000);
    }

    private static (string dir, string name) Split(string taskPath)
    {
        int idx = taskPath.LastIndexOf('\\');
        return (taskPath[..idx] + "\\", taskPath[(idx + 1)..]);
    }
}

/// <summary>Removes UWP/Appx packages (current user + provisioned). Not reversible in place.</summary>
public sealed class AppxRemoveAction : TweakAction
{
    /// <summary>Package name substrings, e.g. "Microsoft.ZuneMusic", "king.com.CandyCrush".</summary>
    public required string[] PackagePatterns { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        // An app listing that failed produces an empty set, which would otherwise mean
        // "no bloatware here" — the single likeliest source of a false "Already optimized".
        if (!ctx.Loaded || !ctx.AppsQueryOk) return null;
        return !ctx.AnyPackageInstalled(PackagePatterns);
    }

    /// <summary>
    /// Nothing to back up: a removed Store app is reinstalled from the Store, not from a
    /// snapshot. The catalog marks these tweaks as not fully reversible.
    /// </summary>
    public override void Capture(Tweak tweak, List<BackupEntry> backup) { }

    public override void Apply()
    {
        var problems = new List<string>();
        foreach (var pattern in PackagePatterns)
        {
            // Report what actually happened instead of assuming success: a package that is
            // not installed is fine, one that refuses to uninstall is not.
            var result = PowerShellRunner.Run($$"""
                $ErrorActionPreference = 'Stop'
                $found = $false
                Get-AppxPackage -AllUsers -Name '*{{pattern}}*' | ForEach-Object {
                    $found = $true
                    try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop }
                    catch { Write-Error "$($_.Exception.Message)" }
                }
                Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*{{pattern}}*' | ForEach-Object {
                    $found = $true
                    try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null }
                    catch { Write-Error "$($_.Exception.Message)" }
                }
                if (-not $found) { Write-Output 'not-installed' }
                """, 180_000);

            if (!result.Success)
                problems.Add($"{pattern}: {(result.TimedOut ? "timed out" : FirstLineOf(result.Error))}");
        }
        if (problems.Count > 0)
            throw new InvalidOperationException("Could not remove: " + string.Join("; ", problems));
    }

    private static string FirstLineOf(string text)
    {
        string s = text.Trim();
        int nl = s.IndexOf('\n');
        return (nl < 0 ? s : s[..nl]).Trim();
    }

    public override void RevertToDefault() =>
        throw new NotSupportedException("Removed Store apps must be reinstalled from the Microsoft Store.");
}

/// <summary>
/// Turns one startup entry off (or back on) without deleting it, by flipping the same
/// StartupApproved bit Task Manager uses. "Applied" means the entry is disabled.
///
/// The backup keeps the ORIGINAL 12 bytes verbatim, not just the bit: Windows also stores a
/// timestamp in there, and restoring a reconstruction instead of what was really present
/// would quietly rewrite state this app never owned. If no value existed at all, the backup
/// records that, and restoring deletes it again — an absent value is what Windows reads as
/// "enabled", so inventing one would not be the same thing.
/// </summary>
public sealed class StartupEntryAction : TweakAction
{
    /// <summary>The StartupApproved mirror key: ...\StartupApproved\Run | Run32 | StartupFolder.</summary>
    public required string ApprovedKeyPath { get; init; }
    /// <summary>Registry value name under Run, or the file name for a Startup-folder entry.</summary>
    public required string EntryName { get; init; }

    private const byte EnabledBit = 0x02;

    public override bool? IsApplied(ScanContext ctx)
    {
        try { return !Services.StartupScanner.IsEnabled(ApprovedKeyPath, EntryName); }
        catch { return null; }
    }

    public override void Capture(Tweak tweak, List<BackupEntry> backup)
    {
        var original = ReadBytes();
        backup.Add(new BackupEntry
        {
            Type = "startup-entry",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            KeyPath = ApprovedKeyPath,
            ValueName = EntryName,
            Existed = original is not null,
            Kind = RegistryValueKind.Binary.ToString(),
            Value = original is null ? null : Convert.ToHexString(original),
        });
    }

    public override void Apply() => SetEnabled(ApprovedKeyPath, EntryName, false);

    /// <summary>"Default" for a startup entry is on — an entry exists because something installed it.</summary>
    public override void RevertToDefault() => SetEnabled(ApprovedKeyPath, EntryName, true);

    private byte[]? ReadBytes() => Services.StartupScanner.ReadApproved(ApprovedKeyPath, EntryName);

    internal static void SetEnabled(string approvedKeyPath, string entryName, bool enabled)
    {
        // Keep whatever Windows had in the remaining 11 bytes (its own timestamp); only the
        // state byte is ours to change.
        byte[] bytes = Services.StartupScanner.ReadApproved(approvedKeyPath, entryName) is { Length: 12 } existing
            ? (byte[])existing.Clone()
            : new byte[12];
        bytes[0] = enabled ? EnabledBit : (byte)0x01;

        var (root, sub) = ParseKey(approvedKeyPath);
        using var key = root.CreateSubKey(sub, writable: true)
            ?? throw new InvalidOperationException($"Cannot open or create {approvedKeyPath}");
        key.SetValue(entryName, bytes, RegistryValueKind.Binary);
    }
}

/// <summary>Arbitrary PowerShell apply/revert with detection delegated to the scan context.</summary>
public sealed class CommandAction : TweakAction
{
    public required string ApplyScript { get; init; }
    public required string RevertScript { get; init; }
    public required Func<ScanContext, bool?> Detect { get; init; }
    public int TimeoutMs { get; init; } = 120_000;

    public override bool? IsApplied(ScanContext ctx) => ctx.Loaded ? Detect(ctx) : null;

    /// <summary>Script-driven tweaks carry their own revert script; nothing to snapshot.</summary>
    public override void Capture(Tweak tweak, List<BackupEntry> backup) { }

    public override void Apply() => PowerShellRunner.RunOrThrow(ApplyScript, "Command", TimeoutMs);

    public override void RevertToDefault() => PowerShellRunner.RunOrThrow(RevertScript, "Command", TimeoutMs);
}
