using System.IO;
using System.Text.Json;

namespace WinPure.Services;

/// <summary>An app the Install page offers, identified by its winget package id.</summary>
public sealed record InstallableApp(string Id, string Name, string Description, string Group, string Icon);

/// <summary>
/// The apps the Install page offers. Every id was checked with `winget show --id ... --exact --source winget`
/// on 2026-09-12. Ids are matched case-sensitively by winget's --exact: `voidtools.Everything`, not `Voidtools`.
/// </summary>
public static class AppInstallerCatalog
{
    private static string Glyph(int code) => ((char)code).ToString();

    public static IReadOnlyList<InstallableApp> Build() => new InstallableApp[]
    {
        new("Mozilla.Firefox", "Firefox", "Mozilla's open-source web browser.", "Browsers", Glyph(0xE774)),
        new("Google.Chrome", "Google Chrome", "Google's web browser.", "Browsers", Glyph(0xE774)),
        new("Brave.Brave", "Brave", "A Chromium-based browser that blocks ads and trackers by default.", "Browsers", Glyph(0xE774)),

        new("7zip.7zip", "7-Zip", "Opens and creates ZIP, 7z, RAR and other archives.", "Utilities", Glyph(0xE8B7)),
        new("voidtools.Everything", "Everything", "Finds any file on your PC by name, instantly.", "Utilities", Glyph(0xE721)),
        new("Microsoft.PowerToys", "PowerToys", "Microsoft's extra tools: FancyZones, PowerRename, Color Picker and more.", "Utilities", Glyph(0xE90F)),
        new("ShareX.ShareX", "ShareX", "Screenshots, screen recordings and quick annotations.", "Utilities", Glyph(0xE722)),
        new("Notepad++.Notepad++", "Notepad++", "A lightweight text and code editor.", "Utilities", Glyph(0xE70F)),
        new("WinDirStat.WinDirStat", "WinDirStat", "Shows what is filling up your disk.", "Utilities", Glyph(0xEDA2)),

        new("Bitwarden.Bitwarden", "Bitwarden", "An open-source password manager that syncs across devices.", "Passwords", Glyph(0xE72E)),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", "An offline password manager that keeps your vault in a file you control.", "Passwords", Glyph(0xE72E)),

        new("VideoLAN.VLC", "VLC media player", "Plays almost any video or audio file.", "Media", Glyph(0xE768)),
        new("OBSProject.OBSStudio", "OBS Studio", "Screen recording and live streaming.", "Media", Glyph(0xE714)),

        new("Git.Git", "Git", "Version control for code.", "Development", Glyph(0xE943)),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Microsoft's code editor.", "Development", Glyph(0xE943)),
        new("Python.Python.3.13", "Python 3.13", "The Python programming language.", "Development", Glyph(0xE943)),
        new("OpenJS.NodeJS.LTS", "Node.js LTS", "The JavaScript runtime, long-term support release.", "Development", Glyph(0xE943)),

        new("Discord.Discord", "Discord", "Voice, video and text chat.", "Communication", Glyph(0xE8BD)),
        new("Zoom.Zoom", "Zoom", "Video meetings.", "Communication", Glyph(0xE8AA)),

        new("TheDocumentFoundation.LibreOffice", "LibreOffice", "A free office suite: documents, spreadsheets and presentations.", "Office", Glyph(0xE8A5)),
        new("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Opens, fills in and signs PDF files.", "Office", Glyph(0xE8A5)),

        new("Valve.Steam", "Steam", "Valve's game store and launcher.", "Gaming", Glyph(0xE7FC)),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic's game store and launcher.", "Gaming", Glyph(0xE7FC)),
    };
}

/// <summary>Talks to winget. Swappable so tests never install anything.</summary>
public interface IWingetBackend
{
    /// <summary>Package ids winget sees installed, or null when winget is missing or did not answer.</summary>
    IReadOnlySet<string>? ReadInstalledIds();
    /// <summary>Installs one package and returns winget's exit code. Judge the result by reading the installed ids afterwards.</summary>
    int Install(string id);
}

public static class Winget
{
    internal static IWingetBackend Backend { get; set; } = new WingetCli();

    /// <summary>winget package ids: letters, digits, dot, dash, underscore and plus, starting with a letter or digit.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 128 &&
        char.IsAsciiLetterOrDigit(id[0]) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+');

    /// <summary>
    /// The package ids in a `winget export` file. Its JSON is the same in every Windows language, unlike the
    /// table `winget list` prints. Compared ignoring case, because --exact taught us ids are easy to mistype.
    /// </summary>
    public static HashSet<string> ParseExport(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("Sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (var source in sources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object ||
                    !source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var package in packages.EnumerateArray())
                {
                    if (package.ValueKind == JsonValueKind.Object &&
                        package.TryGetProperty("PackageIdentifier", out var id) &&
                        id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } value)
                        ids.Add(value);
                }
            }
        }
        return ids;
    }
}

internal sealed class WingetCli : IWingetBackend
{
    public IReadOnlySet<string>? ReadInstalledIds()
    {
        string file = Path.Combine(Path.GetTempPath(), $"winpure-winget-{Guid.NewGuid():N}.json");
        try
        {
            // Read-only, so it may die with the app. About 15 s for 96 packages, measured on 25H2.
            var result = PowerShellRunner.Run(
                $"winget export -o {PowerShellRunner.Quote(file)} --source winget --disable-interactivity | Out-Null; exit $LASTEXITCODE",
                120_000, dieWithApp: true);
            if (!result.Success || !File.Exists(file))
            {
                LogService.Log($"winget export failed (exit {result.ExitCode}): {result.Error}");
                return null;
            }
            return Winget.ParseExport(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            LogService.Log($"winget export could not be read: {ex.Message}");
            return null;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public int Install(string id)
    {
        if (!Winget.IsValidId(id)) throw new ArgumentException($"Invalid winget package id '{id}'.");
        // Not dieWithApp: an installer cut off halfway leaves a broken app behind.
        var result = PowerShellRunner.Run(
            $"winget install --id {PowerShellRunner.Quote(id)} --exact --source winget --silent " +
            "--accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Null; exit $LASTEXITCODE",
            1_800_000);
        return result.TimedOut ? -1 : result.ExitCode;
    }
}
