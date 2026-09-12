using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// What a restored backup entry may touch. Backups are plain JSON in the user's profile, which any
/// program the user runs can edit without elevation, while WinPure restores them elevated. So an entry
/// may only reach what this version of WinPure changes itself: the catalog's own registry values, keys,
/// services, scheduled tasks and Windows features, plus the Startup page's switches. A forged entry that
/// points anywhere else — Winlogon, a service nobody tweaks, a quote smuggled into a task name — is
/// refused before anything runs.
/// </summary>
internal static class BackupEntryPolicy
{
    /// <summary>Tests only: the registry key their toy tweaks restore under. The app never sets it.</summary>
    internal static string? TestKeyPrefix { get; set; }

    private sealed record Allowed(
        HashSet<string> Values, HashSet<string> Keys, HashSet<string> Services, HashSet<string> Tasks, HashSet<string> Features);

    private static readonly Lazy<Allowed> FromCatalog = new(() =>
    {
        var ignoreCase = StringComparer.OrdinalIgnoreCase;
        var allowed = new Allowed(new(ignoreCase), new(ignoreCase), new(ignoreCase), new(ignoreCase), new(ignoreCase));
        foreach (var action in TweakCatalog.Build().SelectMany(t => t.Actions))
        {
            switch (action)
            {
                case RegistryValueAction value: allowed.Values.Add(Normalize(value.KeyPath) + "!" + value.ValueName); break;
                case RegistryKeyAction key: allowed.Keys.Add(Normalize(key.KeyPath)); break;
                case ServiceAction service: allowed.Services.Add(service.ServiceName); break;
                case ScheduledTaskAction task: allowed.Tasks.Add(task.TaskPath); break;
                case FeatureAction feature: allowed.Features.Add(feature.FeatureName); break;
            }
        }
        return allowed;
    });

    /// <summary>False, with the reason, for an entry that reaches beyond what WinPure changes.</summary>
    public static bool IsAllowed(BackupEntry entry, out string reason)
    {
        var allowed = FromCatalog.Value;
        reason = "";
        switch (entry.Type)
        {
            case "registry-value":
                if (entry.KeyPath is null || entry.ValueName is null) return true; // restoring it does nothing
                if (UnderTestKey(entry.KeyPath) || allowed.Values.Contains(Normalize(entry.KeyPath) + "!" + entry.ValueName)) return true;
                reason = "WinPure never changes this registry value";
                return false;

            case "registry-key":
                if (entry.KeyPath is null) return true;
                if (UnderTestKey(entry.KeyPath) || allowed.Keys.Contains(Normalize(entry.KeyPath))) return true;
                reason = "WinPure never changes this registry key";
                return false;

            case "startup-entry":
                if (entry.KeyPath is null || entry.ValueName is null) return true;
                if (IsStartupApprovedKey(entry.KeyPath) && (entry.Value is null || IsTwelveByteHex(entry.Value))) return true;
                reason = "this is not a Startup switch";
                return false;

            case "service":
                if (entry.ServiceName is null) return true;
                if (allowed.Services.Contains(entry.ServiceName)) return true;
                reason = "WinPure never changes this service";
                return false;

            case "scheduled-task":
                if (entry.TaskPath is null) return true;
                if (allowed.Tasks.Contains(entry.TaskPath) || IsStartupTaskPath(entry.TaskPath)) return true;
                reason = "WinPure never changes this scheduled task";
                return false;

            case "optional-feature":
                if (entry.ValueName is null || allowed.Features.Contains(entry.ValueName)) return true;
                reason = "WinPure never changes this Windows feature";
                return false;

            default:
                // system-state validates its own values (SystemState.Restore); unknown types restore nothing.
                return true;
        }
    }

    private static bool UnderTestKey(string keyPath) =>
        TestKeyPrefix is { } prefix &&
        (keyPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
         keyPath.StartsWith(prefix + @"\", StringComparison.OrdinalIgnoreCase));

    /// <summary>Where Task Manager and the Startup page keep their switches, for this user or all users.</summary>
    private static bool IsStartupApprovedKey(string keyPath)
    {
        string path = Normalize(keyPath);
        bool hive = path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase);
        return hive && new[] { @"\StartupApproved\Run", @"\StartupApproved\Run32", @"\StartupApproved\StartupFolder" }
            .Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTwelveByteHex(string value) =>
        value.Length == 24 && value.All(Uri.IsHexDigit);

    /// <summary>
    /// A third-party logon task from the Startup page. Its path is not in the catalog, so it is held to
    /// characters a task name plausibly has — no quotes, no $, `, ;, |, &amp;, brackets or wildcards.
    /// </summary>
    private static bool IsStartupTaskPath(string taskPath) =>
        taskPath.Length is > 1 and <= 260 &&
        taskPath.StartsWith(@"\", StringComparison.Ordinal) &&
        !taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) &&
        taskPath.All(c => char.IsLetterOrDigit(c) || " _-.(){}#+,@".Contains(c) || c == (char)92);

    private static string Normalize(string keyPath)
    {
        int idx = keyPath.IndexOf((char)92);
        string hive = idx < 0 ? keyPath : keyPath[..idx];
        string rest = idx < 0 ? "" : keyPath[idx..];
        string shortHive = hive.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKEY_CURRENT_USER" => "HKCU",
            "HKEY_CLASSES_ROOT" => "HKCR",
            "HKEY_USERS" => "HKU",
            var other => other,
        };
        return shortHive + rest;
    }
}
