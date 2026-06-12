namespace WinPure.Models;

public enum TweakCategory
{
    Privacy,
    Apps,
    Services,
    Performance,
    UI,
    ContextMenu
}

/// <summary>Lowest preset that includes the tweak. Manual = never auto-selected by a preset.</summary>
public enum PresetLevel
{
    Manual = 0,
    Safe = 1,
    Balanced = 2,
    Aggressive = 3
}

public enum TweakStatus
{
    Unknown,
    Optimized,
    Pending
}

public sealed class Tweak
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Help { get; init; } = "";
    public required TweakCategory Category { get; init; }
    public PresetLevel Preset { get; init; } = PresetLevel.Manual;
    public string Icon { get; init; } = "";
    public bool RequiresRestart { get; init; }
    public bool RequiresExplorerRestart { get; init; }
    /// <summary>False for actions that cannot be undone in place (e.g. app removal → reinstall from Store).</summary>
    public bool FullyReversible { get; init; } = true;
    public required IReadOnlyList<TweakAction> Actions { get; init; }
}
