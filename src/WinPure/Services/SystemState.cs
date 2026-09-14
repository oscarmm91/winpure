using Microsoft.Win32;

namespace WinPure.Services;

/// <summary>Settings that live behind a command (powercfg, DISM) rather than a registry value WinPure writes itself.</summary>
public enum SystemStateKind { Hibernation, PowerPlan, ReservedStorage }

/// <summary>Reads and writes one command-driven setting.</summary>
public interface ISystemStateHandler
{
    /// <summary>The current state, or null when it cannot be read.</summary>
    string? Read();
    /// <summary>Whether <paramref name="state"/> is one this handler could ever have recorded.</summary>
    bool IsValid(string state);
    /// <summary>Writes a state that passed <see cref="IsValid"/>. Throws when Windows refuses.</summary>
    void Write(string state);
}

/// <summary>
/// The handlers behind <see cref="Models.SystemStateAction"/>, and the only way a state read back
/// from a backup file reaches the system.
/// </summary>
public static class SystemState
{
    private static readonly Dictionary<SystemStateKind, ISystemStateHandler> Handlers = new()
    {
        [SystemStateKind.Hibernation] = new HibernationState(),
        [SystemStateKind.PowerPlan] = new PowerPlanState(),
        [SystemStateKind.ReservedStorage] = new ReservedStorageState(),
    };

    public static ISystemStateHandler Handler(SystemStateKind kind) => Handlers[kind];

    /// <summary>Tests swap in a fake so they never run powercfg or DISM. Returns the handler it replaced.</summary>
    internal static ISystemStateHandler Swap(SystemStateKind kind, ISystemStateHandler handler)
    {
        var previous = Handlers[kind];
        Handlers[kind] = handler;
        return previous;
    }

    /// <summary>
    /// Writes back a state recorded in a backup. A backup stores only the measured state, never a
    /// command, and that state is checked before anything runs: backups are plain files in the user's
    /// profile while WinPure runs elevated, so nothing read from one may decide what gets executed.
    /// </summary>
    public static void Restore(string? kindName, string? state)
    {
        if (kindName is null || !Enum.TryParse<SystemStateKind>(kindName, out var kind) || !Enum.IsDefined(kind))
            throw new InvalidOperationException($"Unknown setting '{Clip(kindName)}' in the backup; refused.");
        var handler = Handler(kind);
        if (state is null || !handler.IsValid(state))
            throw new InvalidOperationException($"The backup holds an invalid {kind} state '{Clip(state)}'; refused.");
        handler.Write(state);
    }

    private static string Clip(string? s) => s is null ? "(none)" : s.Length <= 60 ? s : s[..60] + "...";
}

/// <summary>"on" or "off".</summary>
internal sealed class HibernationState : ISystemStateHandler
{
    public string? Read()
    {
        try
        {
            // powercfg /hibernate writes HibernateEnabled; a machine where nobody ever ran it has only
            // HibernateEnabledDefault. Measured on 25H2: the first absent, the second 1, hiberfil.sys present.
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            object? value = key?.GetValue("HibernateEnabled") ?? key?.GetValue("HibernateEnabledDefault");
            return value is int i ? (i != 0 ? "on" : "off") : null;
        }
        catch { return null; }
    }

    public bool IsValid(string state) => state is "on" or "off";

    // The command comes from these constants, never from the state string.
    public void Write(string state) => PowerShellRunner.RunOrThrow(
        state == "on" ? "powercfg /hibernate on" : "powercfg /hibernate off", "Setting hibernation");
}

/// <summary>The active power plan's GUID, lowercase, "D" format.</summary>
internal sealed class PowerPlanState : ISystemStateHandler
{
    public string? Read()
    {
        try
        {
            // The same GUID powercfg /getactivescheme prints (compared on 25H2), without parsing text
            // that Windows translates.
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            return key?.GetValue("ActivePowerScheme") is string s && Guid.TryParse(s, out var guid) ? guid.ToString("D") : null;
        }
        catch { return null; }
    }

    public bool IsValid(string state) => Guid.TryParse(state, out _);

    // Re-formatted from the parsed GUID, so only hex digits and dashes ever reach the command.
    public void Write(string state) => PowerShellRunner.RunOrThrow(
        $"powercfg /setactive {Guid.Parse(state):D}", "Switching the power plan");
}

/// <summary>"Enabled" or "Disabled": DISM's own enum names, identical on every Windows language.</summary>
internal sealed class ReservedStorageState : ISystemStateHandler
{
    public string? Read()
    {
        // Needs elevation: unelevated it is refused within a second and reads as unknown.
        var result = PowerShellRunner.Run(
            "[string](Get-WindowsReservedStorageState -ErrorAction Stop).ReservedStorageState", 60_000, dieWithApp: true);
        string state = result.Output.Trim();
        return result.Success && IsValid(state) ? Normalize(state) : null;
    }

    public bool IsValid(string state) =>
        state.Equals("Enabled", StringComparison.OrdinalIgnoreCase) || state.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

    public void Write(string state) => PowerShellRunner.RunOrThrow(
        Normalize(state) == "Enabled"
            ? "Set-WindowsReservedStorageState -State Enabled -ErrorAction Stop"
            : "Set-WindowsReservedStorageState -State Disabled -ErrorAction Stop",
        "Changing reserved storage");

    private static string Normalize(string state) =>
        state.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ? "Enabled" : "Disabled";
}
