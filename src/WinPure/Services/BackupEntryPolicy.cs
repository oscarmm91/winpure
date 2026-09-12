using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// What a restored backup entry may touch. Backups are plain JSON in the user's profile, which any
/// program the user runs can edit without elevation, while WinPure restores them elevated. So an entry
/// may only reach what this version of WinPure changes itself: the catalog's own registry values, keys,
/// services, scheduled tasks and Windows features, plus the Startup page's switches. A forged entry that
/// points anywhere else — Winlogon, a service nobody tweaks, a quote smuggled into a task name — is
/// refused before anything runs.
///
/// This is the second layer. A second review showed its limit: the data of a genuine undo — SMB 1.0 back
/// on, a service back to Automatic — is exactly what a forged file would ask for, so no list can tell
/// them apart. The first layer is that backups live where only administrators can write (BackupStore).
/// </summary>
internal static class BackupEntryPolicy
{
    /// <summary>Tests only: the registry key their toy tweaks restore under. The app never sets it.</summary>
    internal static string? TestKeyPrefix { get; set; }

    private sealed record Allowed(
        Dictionary<string, string> Values, Dictionary<string, string?> Keys, HashSet<string> Services,
        HashSet<string> Tasks, HashSet<string> Features, HashSet<string> StartupKeys);

    private static readonly Lazy<Allowed> FromCatalog = new(() =>
    {
        var ignoreCase = StringComparer.OrdinalIgnoreCase;
        var allowed = new Allowed(new(ignoreCase), new(ignoreCase), new(ignoreCase), new(ignoreCase), new(ignoreCase),
            new(StartupScanner.ApprovedKeys.Select(Normalize), ignoreCase));
        foreach (var action in TweakCatalog.Build().SelectMany(t => t.Actions))
        {
            switch (action)
            {
                // value slot -> the kind the catalog writes; key -> the (Default) the catalog knows for it
                case RegistryValueAction value: allowed.Values[Normalize(value.KeyPath) + "!" + value.ValueName] = value.Kind.ToString(); break;
                case RegistryKeyAction key: allowed.Keys[Normalize(key.KeyPath)] = key.KeyDefaultValue; break;
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
                if (UnderTestKey(entry.KeyPath)) return true;
                if (!allowed.Values.TryGetValue(Normalize(entry.KeyPath) + "!" + entry.ValueName, out string? catalogKind))
                {
                    reason = "WinPure never changes this registry value";
                    return false;
                }
                // The kind the value really had may differ from the catalog's; a kind that makes Windows
                // expand or parse the data differently (ExpandString, MultiString, Binary) must not appear.
                if (entry.Kind is not null && entry.Kind != catalogKind && entry.Kind is not ("DWord" or "QWord" or "String"))
                {
                    reason = $"a {entry.Kind} value where WinPure writes a {catalogKind}";
                    return false;
                }
                return true;

            case "registry-key":
                if (entry.KeyPath is null) return true;
                if (UnderTestKey(entry.KeyPath)) return true;
                if (!allowed.Keys.TryGetValue(Normalize(entry.KeyPath), out string? catalogDefault))
                {
                    reason = "WinPure never changes this registry key";
                    return false;
                }
                // The (Default) of a context-menu handler is a CLSID: only the one the catalog restores.
                if (entry.Existed && entry.Value is not null && entry.Value != (catalogDefault ?? ""))
                {
                    reason = "a (Default) value WinPure never writes for this key";
                    return false;
                }
                return true;

            case "startup-entry":
                if (entry.KeyPath is null || entry.ValueName is null) return true;
                if ((UnderTestKey(entry.KeyPath) || allowed.StartupKeys.Contains(Normalize(entry.KeyPath))) &&
                    (entry.Value is null || IsTwelveByteHex(entry.Value))) return true;
                reason = "this is not a Startup switch";
                return false;

            case "service":
                if (entry.ServiceName is null) return true;
                if (!allowed.Services.Contains(entry.ServiceName))
                {
                    reason = "WinPure never changes this service";
                    return false;
                }
                if (entry.StartMode is not null and not (2 or 3 or 4))
                {
                    reason = $"start mode {entry.StartMode} is not Automatic, Manual or Disabled";
                    return false;
                }
                return true;

            case "scheduled-task":
                if (entry.TaskPath is null) return true;
                if (allowed.Tasks.Contains(entry.TaskPath) || IsStartupTaskPath(entry.TaskPath)) return true;
                reason = "WinPure never changes this scheduled task";
                return false;

            case "optional-feature":
                if (entry.ValueName is null || allowed.Features.Contains(entry.ValueName)) return true;
                reason = "WinPure never changes this Windows feature";
                return false;

            case "system-state":
                // Kind and state are validated in SystemState.Restore, the only way this type is restored.
                return true;

            default:
                // Unknown types restore nothing (RestoreEntry has no case for them).
                return true;
        }
    }

    private static bool UnderTestKey(string keyPath) =>
        TestKeyPrefix is { } prefix &&
        (keyPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
         keyPath.StartsWith(prefix + @"\", StringComparison.OrdinalIgnoreCase));

    private static bool IsTwelveByteHex(string value) =>
        value.Length == 24 && value.All(Uri.IsHexDigit);

    /// <summary>
    /// A third-party logon task from the Startup page. Its path is not in the catalog, so it is held to
    /// characters a task name plausibly has — no quotes, no $, `, ;, |, &amp;, brackets or wildcards.
    /// </summary>
    private static bool IsStartupTaskPath(string taskPath) =>
        taskPath.Length is > 1 and <= 260 &&
        taskPath.StartsWith(@"\", StringComparison.Ordinal) &&
        !taskPath.Contains(@"\\", StringComparison.Ordinal) &&   // "\\Microsoft\..." would slip past the next check
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
