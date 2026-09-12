namespace WinPure.Models;

/// <summary>Where an autostart entry actually lives.</summary>
public enum StartupSource
{
    /// <summary>A value under a CurrentVersion\Run key.</summary>
    RegistryRun,
    /// <summary>A shortcut or script sitting in a Startup folder.</summary>
    StartupFolder,
    /// <summary>A scheduled task with a logon trigger.</summary>
    ScheduledTask,
}

/// <summary>
/// One thing that starts with Windows, as shown on the Startup Apps page.
///
/// Turning an entry off never deletes it: registry and folder entries are switched with the
/// same StartupApproved bit Task Manager uses, and tasks with Disable-ScheduledTask. That is
/// what makes it reversible — and what makes WinPure agree with what Task Manager shows.
/// </summary>
public sealed class StartupEntry
{
    /// <summary>Stable id, e.g. "run:hkcu:Docker Desktop". Used as the tweak id for backups.</summary>
    public required string Id { get; init; }
    /// <summary>Friendly name: the file's description when we can read it, else the raw entry name.</summary>
    public required string Name { get; init; }
    /// <summary>The raw registry value name or file name — what Task Manager keys off.</summary>
    public required string EntryName { get; init; }
    public string Publisher { get; init; } = "";
    /// <summary>Full command line or shortcut path, shown as the detail line.</summary>
    public string Command { get; init; } = "";
    /// <summary>Resolved executable, when one could be parsed out of the command.</summary>
    public string? ExecutablePath { get; init; }
    public required StartupSource Source { get; init; }
    /// <summary>"This user" or "All users" — an all-users entry needs admin to change.</summary>
    public required string Scope { get; init; }
    public bool Enabled { get; init; }
    /// <summary>True when the executable or shortcut target no longer exists on disk.</summary>
    public bool IsOrphan { get; init; }

    // --- how to toggle it ---

    /// <summary>StartupApproved mirror key (Run / Run32 / StartupFolder). Null for tasks.</summary>
    public string? ApprovedKeyPath { get; init; }
    /// <summary>Full task path, for ScheduledTask entries.</summary>
    public string? TaskPath { get; init; }

    public string SourceLabel => Source switch
    {
        StartupSource.RegistryRun => "Registry",
        StartupSource.StartupFolder => "Startup folder",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => "",
    };
}
