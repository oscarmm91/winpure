using Microsoft.Win32;
using WinPure.Services;

namespace WinPure.Models;

/// <summary>A single reversible operation. A tweak bundles one or more actions.</summary>
public abstract class TweakAction
{
    /// <summary>True if the system already matches the tweaked state. Null = cannot tell.</summary>
    public abstract bool? IsApplied(ScanContext ctx);

    /// <summary>Captures current state for the backup, then applies the change.</summary>
    public abstract void Apply(Tweak tweak, List<BackupEntry> backup);

    /// <summary>Reverts to the stock Windows default (used by the toggle-off path).</summary>
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

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
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
        WriteValue(KeyPath, ValueName, ApplyValue, Kind);
    }

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

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
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

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
    {
        var start = ReadValue(ServiceKey, "Start", out _);
        if (start is null) return; // not installed
        backup.Add(new BackupEntry
        {
            Type = "service",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            ServiceName = ServiceName,
            StartMode = (int)start,
            Existed = true,
        });
        WriteValue(ServiceKey, "Start", 4, Microsoft.Win32.RegistryValueKind.DWord);
        PowerShellRunner.Run($"Stop-Service -Name '{ServiceName}' -Force -ErrorAction SilentlyContinue");
    }

    public override void RevertToDefault() => SetStartMode(ServiceName, DefaultStartMode);

    internal static void SetStartMode(string serviceName, int startMode)
    {
        WriteValue($@"HKLM\SYSTEM\CurrentControlSet\Services\{serviceName}", "Start",
            startMode, Microsoft.Win32.RegistryValueKind.DWord);
        if (startMode == 2)
            PowerShellRunner.Run($"Start-Service -Name '{serviceName}' -ErrorAction SilentlyContinue");
    }
}

/// <summary>Disables a scheduled task. Revert re-enables it.</summary>
public sealed class ScheduledTaskAction : TweakAction
{
    /// <summary>Full path, e.g. \Microsoft\Windows\Feedback\Siuf\DmClient</summary>
    public required string TaskPath { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        if (!ctx.Loaded) return null;
        return !ctx.TaskEnabled.TryGetValue(TaskPath, out bool enabled) || !enabled;
    }

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
    {
        backup.Add(new BackupEntry
        {
            Type = "scheduled-task",
            TweakId = tweak.Id,
            TweakName = tweak.Name,
            TaskPath = TaskPath,
            TaskWasEnabled = true,
            Existed = true,
        });
        SetEnabled(TaskPath, false);
    }

    public override void RevertToDefault() => SetEnabled(TaskPath, true);

    internal static void SetEnabled(string taskPath, bool enabled)
    {
        string dir = taskPath[..taskPath.LastIndexOf('\\')] + "\\";
        string name = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
        string verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
        PowerShellRunner.Run($"{verb} -TaskPath '{dir}' -TaskName '{name}' -ErrorAction SilentlyContinue | Out-Null");
    }
}

/// <summary>Removes UWP/Appx packages (current user + provisioned). Not reversible in place.</summary>
public sealed class AppxRemoveAction : TweakAction
{
    /// <summary>Package name substrings, e.g. "Microsoft.ZuneMusic", "king.com.CandyCrush".</summary>
    public required string[] PackagePatterns { get; init; }

    public override bool? IsApplied(ScanContext ctx)
    {
        if (!ctx.Loaded) return null;
        return !ctx.AnyPackageInstalled(PackagePatterns);
    }

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
    {
        // App removal is restored via the Microsoft Store, not via snapshot — nothing to back up.
        foreach (var pattern in PackagePatterns)
        {
            PowerShellRunner.Run($"""
                $ErrorActionPreference = 'SilentlyContinue'
                Get-AppxPackage -AllUsers -Name '*{pattern}*' | Remove-AppxPackage -AllUsers
                Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*{pattern}*' |
                    Remove-AppxProvisionedPackage -Online | Out-Null
                """, 180_000);
        }
    }

    public override void RevertToDefault() =>
        throw new NotSupportedException("Removed Store apps must be reinstalled from the Microsoft Store.");
}

/// <summary>Arbitrary PowerShell apply/revert with detection delegated to the scan context.</summary>
public sealed class CommandAction : TweakAction
{
    public required string ApplyScript { get; init; }
    public required string RevertScript { get; init; }
    public required Func<ScanContext, bool?> Detect { get; init; }
    public int TimeoutMs { get; init; } = 120_000;

    public override bool? IsApplied(ScanContext ctx) => ctx.Loaded ? Detect(ctx) : null;

    public override void Apply(Tweak tweak, List<BackupEntry> backup)
    {
        var result = PowerShellRunner.Run(ApplyScript, TimeoutMs);
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Command failed" : result.Error);
    }

    public override void RevertToDefault()
    {
        var result = PowerShellRunner.Run(RevertScript, TimeoutMs);
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Command failed" : result.Error);
    }
}
