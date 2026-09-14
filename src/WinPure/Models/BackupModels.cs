using System.Text.Json.Serialization;

namespace WinPure.Models;

/// <summary>One reversible unit of system state captured before a change.</summary>
public sealed class BackupEntry
{
    /// <summary>
    /// registry-value | registry-key | service | scheduled-task | startup-entry | system-state | optional-feature.
    /// For system-state, ValueName is the SystemStateKind and Value the measured state; for
    /// optional-feature, ValueName is the DISM feature name and Value "Enabled" or "Disabled".
    /// </summary>
    public required string Type { get; set; }
    public required string TweakId { get; set; }
    public string TweakName { get; set; } = "";

    // registry-value / registry-key
    public string? KeyPath { get; set; }
    public string? ValueName { get; set; }
    /// <summary>RegistryValueKind name (DWord, String, ExpandString...).</summary>
    public string? Kind { get; set; }
    /// <summary>Original data, serialized as string (DWORD as decimal).</summary>
    public string? Value { get; set; }
    /// <summary>Whether the value (or key) existed before the change.</summary>
    public bool Existed { get; set; }

    /// <summary>
    /// Came from a best-effort action (see TweakAction.Optional). Windows may refuse to write
    /// it back — that is expected and must not be reported as a failed restore.
    /// </summary>
    public bool Optional { get; set; }

    // service
    public string? ServiceName { get; set; }
    public int? StartMode { get; set; }

    // scheduled-task
    public string? TaskPath { get; set; }
    public bool? TaskWasEnabled { get; set; }
}

public sealed class BackupSession
{
    public required string Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<string> TweakNames { get; set; } = new();
    public List<BackupEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public string? FilePath { get; set; }
}

public sealed class TweakResult
{
    public required Tweak Tweak { get; init; }
    public required bool Success { get; init; }
    public required bool WasApply { get; init; }
    public string Message { get; init; } = "";
}
