using System.IO;
using System.Text.Json;

namespace WinPure.Services;

/// <summary>
/// A WinPure configuration file: which tweaks were switched on, to tick the same boxes on another PC.
/// It carries tweak ids only — never values, registry paths or scripts — so a file from anyone can at
/// worst tick boxes, and nothing changes until the user clicks Apply Changes.
/// Different from a backup, which rolls back THIS machine.
/// </summary>
public static class ConfigFile
{
    public const string AppName = "WinPure";
    public const int FormatVersion = 1;
    /// <summary>Far beyond any real catalog; anything bigger is not a WinPure configuration.</summary>
    public const int MaxChars = 256 * 1024;

    public sealed record Parsed(IReadOnlyList<string> KnownIds, IReadOnlyList<string> UnknownIds);

    private sealed class Dto
    {
        public string? App { get; set; }
        public int Format { get; set; }
        public DateTime ExportedUtc { get; set; }
        public string? Version { get; set; }
        public List<string>? Tweaks { get; set; }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Serialize(IEnumerable<string> selectedIds, string appVersion) =>
        JsonSerializer.Serialize(new Dto
        {
            App = AppName,
            Format = FormatVersion,
            ExportedUtc = DateTime.UtcNow,
            Version = appVersion,
            Tweaks = selectedIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList(),
        }, Indented);

    /// <summary>
    /// Splits the file's tweak ids into ones this version knows and ones it does not (renamed or
    /// newer). Throws <see cref="InvalidDataException"/>, with a message fit to show the user, when
    /// the file is not a WinPure configuration this version can read.
    /// </summary>
    public static Parsed Parse(string json, IEnumerable<string> catalogIds)
    {
        if (json.Length > MaxChars)
            throw new InvalidDataException("This file is too large to be a WinPure configuration.");

        Dto? dto;
        try { dto = JsonSerializer.Deserialize<Dto>(json); }
        catch (JsonException) { throw new InvalidDataException("This file is not a WinPure configuration: it is not valid JSON."); }

        if (dto is null || dto.App != AppName)
            throw new InvalidDataException("This file is not a WinPure configuration.");
        if (dto.Format != FormatVersion)
            throw new InvalidDataException($"This configuration uses format {dto.Format}, which this version of WinPure cannot read.");

        var catalog = new HashSet<string>(catalogIds, StringComparer.Ordinal);
        var ids = (dto.Tweaks ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new Parsed(ids.Where(catalog.Contains).ToList(), ids.Where(id => !catalog.Contains(id)).ToList());
    }
}
