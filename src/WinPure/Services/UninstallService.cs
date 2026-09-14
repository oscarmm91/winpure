using System.Diagnostics;
using Microsoft.Win32;

namespace WinPure.Services;

/// <summary>A program Windows knows how to uninstall, read from the Uninstall registry keys.</summary>
public sealed record InstalledProgram(string Key, string Name, string Version, string Publisher, string Command, bool PerUser);

/// <summary>Lists installed programs and runs their uninstaller. Swappable so tests never uninstall anything.</summary>
public interface IUninstallBackend
{
    IReadOnlyList<InstalledProgram> Read();
    /// <summary>Launches the program's own uninstaller and waits for it to finish. Judge the result by reading the list again.</summary>
    void Run(InstalledProgram program);
}

/// <summary>
/// The generic uninstaller — one of the "no undo" pages, like Remove Apps and Install: Restore never reinstalls
/// a program, so it lives apart, is in no preset and asks before acting. It reads the standard Uninstall keys
/// (per-machine, 32-bit, and per-user), runs the program's OWN uninstaller, and judges success by whether the
/// entry is gone afterwards — never by an exit code.
/// </summary>
public static class UninstallService
{
    internal static IUninstallBackend Backend { get; set; } = new UninstallRegistry();

    internal static IUninstallBackend Swap(IUninstallBackend backend)
    {
        var previous = Backend;
        Backend = backend;
        return previous;
    }

    public static IReadOnlyList<InstalledProgram> Read() => Backend.Read();
    public static void Run(InstalledProgram program) => Backend.Run(program);

    /// <summary>
    /// Whether a program is gone after its uninstaller ran — judged by reading the list again, NEVER by an exit
    /// code. This is the whole "state is not the effect" rule for this page: an uninstaller can return success
    /// and leave the program behind, or the user can cancel its dialog.
    /// </summary>
    public static bool IsGone(InstalledProgram program, IReadOnlyList<InstalledProgram> afterList) =>
        !afterList.Any(p => p.Key == program.Key && p.Name == program.Name);
}

/// <summary>Reads the Uninstall registry keys and launches uninstallers. Never exercised by tests — they swap a fake.</summary>
internal sealed class UninstallRegistry : IUninstallBackend
{
    private static readonly (RegistryKey Root, string Path, bool PerUser)[] Roots =
    {
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", false),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true),
    };

    public IReadOnlyList<InstalledProgram> Read()
    {
        var byName = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, path, perUser) in Roots)
        {
            try
            {
                using var uninstall = root.OpenSubKey(path);
                if (uninstall is null) continue;
                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var k = uninstall.OpenSubKey(subName);
                        if (k is null) continue;

                        var name = (k.GetValue("DisplayName") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Skip Windows updates, hotfixes, and hidden system components — the same things Add/Remove
                        // Programs hides. These are not user-facing apps and uninstalling them is a foot-gun.
                        if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (k.GetValue("ParentKeyName") is not null) continue;
                        var releaseType = k.GetValue("ReleaseType") as string;
                        if (releaseType is "Security Update" or "Update" or "Hotfix") continue;
                        if (name.StartsWith("KB") && name.Length > 2 && char.IsDigit(name[2])) continue;

                        var quiet = (k.GetValue("QuietUninstallString") as string)?.Trim();
                        var normal = (k.GetValue("UninstallString") as string)?.Trim();
                        var command = !string.IsNullOrWhiteSpace(quiet) ? quiet : normal;
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        var version = (k.GetValue("DisplayVersion") as string)?.Trim() ?? "";
                        var publisher = (k.GetValue("Publisher") as string)?.Trim() ?? "";

                        var program = new InstalledProgram(subName, name!, version, publisher, command!, perUser);
                        // A 32-bit and 64-bit view can list the same app; keep the first (machine before user).
                        byName.TryAdd($"{name}|{version}", program);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return byName.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Run(InstalledProgram program)
    {
        // Split the command into an executable and its arguments and launch it WITHOUT a shell — no cmd/powershell
        // re-parsing, so nothing in the registry string can be re-interpreted. UseShellExecute lets the uninstaller
        // elevate and show its own UI. Wait for it to finish so the caller can re-read the list.
        var (exe, args) = SplitCommand(program.Command);
        if (exe.Length == 0) throw new InvalidOperationException("The uninstall command is empty.");
        var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true };
        using var process = Process.Start(psi);
        process?.WaitForExit();
    }

    internal static (string exe, string args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return ("", "");
        if (command[0] == '"')
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }
}
