using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>
/// "Apply to future users": writes a tweak's per-user (HKCU) registry values into the Default profile
/// template, C:\Users\Default\NTUSER.DAT, so accounts created later inherit them. The template is the
/// real hive every new profile is copied from, so this is a genuine change — captured into the same
/// backup session first, exactly like any other, and undone from Restore.
///
/// The hive is opened privately with RegLoadAppKey (REG_PROCESS_APPKEY): no other process can load the
/// same file while WinPure holds it, and closing the handle unloads it and flushes the change into the
/// primary file. Measured on this machine (tools/TemplateProbe, elevated, on copies, 2026-09-13): the
/// real template loads with full access despite its many security descriptors, and a written value
/// survives in NTUSER.DAT alone. Writing the real template needs elevation, which WinPure has; the tests
/// point <see cref="TemplatePath"/> at a throwaway hive and run unelevated.
///
/// Only reversible tweaks whose values live under HKCU are eligible — machine-wide changes (services,
/// tasks, features, HKLM policies) already affect every account, and app removals are per-machine.
/// </summary>
internal static class FutureUsers
{
    private const int KEY_ALL_ACCESS = 0xF003F;
    private const int REG_PROCESS_APPKEY = 0x1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int RegLoadAppKey(string file, out IntPtr hKey, int samDesired, int options, int reserved);

    /// <summary>The Default profile hive. Settable so tests use a throwaway hive instead of the real one.</summary>
    internal static string TemplatePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "..", "..", "Users", "Default", "NTUSER.DAT");

    /// <summary>Backup entry type for a value WinPure wrote into the Default template.</summary>
    internal const string EntryType = "future-user-value";

    /// <summary>A value to mirror into the template: the key path relative to the hive root, e.g. "Software\Foo".</summary>
    internal readonly record struct TemplateWrite(string TweakId, string TweakName, string RelativeKey, string ValueName, RegistryValueKind Kind, object Value);

    /// <summary>A tweak can reach future users when it can be undone and it writes at least one HKCU value.</summary>
    internal static bool IsEligible(Tweak tweak) =>
        tweak.FullyReversible && tweak.Actions.OfType<RegistryValueAction>().Any(a => IsHkcu(a.KeyPath));

    /// <summary>Every HKCU value the applied, eligible tweaks would set — what to mirror into the template.</summary>
    internal static IReadOnlyList<TemplateWrite> WritesFor(IEnumerable<(Tweak tweak, bool apply)> changes)
    {
        var writes = new List<TemplateWrite>();
        foreach (var (tweak, apply) in changes)
        {
            if (!apply || !IsEligible(tweak)) continue;
            foreach (var action in tweak.Actions.OfType<RegistryValueAction>())
            {
                if (!IsHkcu(action.KeyPath)) continue;
                writes.Add(new TemplateWrite(tweak.Id, tweak.Name, Relative(action.KeyPath), action.ValueName, action.Kind, action.ApplyValue));
            }
        }
        return writes;
    }

    /// <summary>The relative keys+values the catalog can legitimately mirror, for the backup policy to check against.</summary>
    internal static HashSet<string> AllowedTargets()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in TweakCatalog.Build().SelectMany(t => t.Actions).OfType<RegistryValueAction>())
            if (IsHkcu(action.KeyPath))
                set.Add(Relative(action.KeyPath) + "!" + action.ValueName);
        return set;
    }

    /// <summary>
    /// Mirrors the writes into the template. Every prior value is captured into the session first, and
    /// <paramref name="flushBeforeWriting"/> puts that snapshot on disk before a single value is changed —
    /// the same invariant the engine keeps for the current user. Failures are logged, not thrown, so a
    /// template that cannot be reached never undoes what was already applied to the current user.
    /// </summary>
    internal static void Apply(IReadOnlyList<TemplateWrite> writes, BackupSession session, Action flushBeforeWriting)
    {
        if (writes.Count == 0) return;
        if (!File.Exists(TemplatePath))
        {
            LogService.Log($"Apply to future users skipped: the profile template {TemplatePath} was not found.");
            return;
        }

        RegistryKey? hive = null;
        try
        {
            hive = Load();
        }
        catch (Exception ex)
        {
            LogService.Log($"Apply to future users skipped: the profile template could not be opened ({ex.Message}).");
            return;
        }

        try
        {
            foreach (var w in writes)
            {
                object? prior = ReadValue(hive, w.RelativeKey, w.ValueName, out var priorKind);
                session.Entries.Add(new BackupEntry
                {
                    Type = EntryType,
                    TweakId = w.TweakId,
                    TweakName = w.TweakName,
                    KeyPath = w.RelativeKey,
                    ValueName = w.ValueName,
                    Existed = prior is not null,
                    Kind = (prior is null ? w.Kind : priorKind).ToString(),
                    Value = prior is null ? null : TweakAction.SerializeValue(prior),
                });
            }

            // The snapshot of what the template held reaches disk before we change the template.
            flushBeforeWriting();

            int written = 0;
            foreach (var w in writes)
            {
                try
                {
                    using var key = hive.CreateSubKey(w.RelativeKey, writable: true)
                        ?? throw new InvalidOperationException($"could not open {w.RelativeKey}");
                    key.SetValue(w.ValueName, w.Value, w.Kind);
                    written++;
                }
                catch (Exception ex)
                {
                    LogService.Log($"Apply to future users: could not write {w.RelativeKey}!{w.ValueName}: {ex.Message}");
                }
            }
            LogService.Log($"Applied {written} of {writes.Count} value(s) to the profile template for future users.");
        }
        finally
        {
            hive.Dispose();   // closing the last handle unloads the hive and flushes it to the primary file
        }
    }

    /// <summary>Undoes one template value: writes back what it held, or deletes it if it had none.</summary>
    internal static void RestoreEntry(BackupEntry entry)
    {
        if (entry.KeyPath is null || entry.ValueName is null) return;
        if (!File.Exists(TemplatePath))
            throw new InvalidOperationException($"The profile template {TemplatePath} was not found.");

        using var hive = Load();
        if (entry.Existed && entry.Value is not null && entry.Kind is not null)
        {
            var kind = Enum.Parse<RegistryValueKind>(entry.Kind);
            using var key = hive.CreateSubKey(entry.KeyPath, writable: true)
                ?? throw new InvalidOperationException($"could not open {entry.KeyPath}");
            key.SetValue(entry.ValueName, TweakAction.DeserializeValue(entry.Value, kind), kind);
        }
        else
        {
            // There was no value before; deleting it returns the template to what it was.
            using var key = hive.OpenSubKey(entry.KeyPath, writable: true);
            key?.DeleteValue(entry.ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Tests only: create the throwaway template hive file so Apply/Restore have one to open (RegLoadAppKey makes a fresh hive when the file is absent).</summary>
    internal static void CreateTemplateFileForTests()
    {
        using var _ = Load();
    }

    /// <summary>Tests only: read a value straight out of the (throwaway) template hive.</summary>
    internal static object? ReadTemplateValueForTests(string relativeKey, string valueName)
    {
        using var hive = Load();
        return ReadValue(hive, relativeKey, valueName, out _);
    }

    private static RegistryKey Load()
    {
        int result = RegLoadAppKey(Path.GetFullPath(TemplatePath), out IntPtr handle, KEY_ALL_ACCESS, REG_PROCESS_APPKEY, 0);
        if (result != 0) throw new Win32Exception(result);
        return RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
    }

    private static object? ReadValue(RegistryKey hive, string relativeKey, string valueName, out RegistryValueKind kind)
    {
        kind = RegistryValueKind.DWord;
        using var key = hive.OpenSubKey(relativeKey, writable: false);
        if (key is null) return null;
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null) kind = key.GetValueKind(valueName);
        return value;
    }

    private static bool IsHkcu(string keyPath)
    {
        int idx = keyPath.IndexOf('\\');
        string hive = idx < 0 ? keyPath : keyPath[..idx];
        return hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ||
               hive.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The key path with the HKCU hive prefix stripped, relative to the loaded template root.</summary>
    private static string Relative(string keyPath)
    {
        int idx = keyPath.IndexOf('\\');
        return idx < 0 ? "" : keyPath[(idx + 1)..];
    }
}
