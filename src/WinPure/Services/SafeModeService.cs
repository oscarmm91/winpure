using System.Diagnostics;

namespace WinPure.Services;

/// <summary>The safe-boot setting for the next restart.</summary>
public enum SafeBoot { Off, Minimal, Network }

/// <summary>Reads and sets the next-boot safe-mode flag. Swappable so tests never touch the real BCD.</summary>
public interface ISafeModeBackend
{
    /// <summary>Best-effort read of the current safeboot setting; Off when it cannot be read.</summary>
    SafeBoot Read();
    /// <summary>Sets (or clears) the safeboot flag. Throws if setting it fails.</summary>
    void Set(SafeBoot mode);
}

/// <summary>
/// The Safe Mode page: flips the next restart into Windows Safe Mode, and back. It never leaves the machine
/// stranded — "Restore normal boot" is always one click and works from inside Safe Mode too. bcdedit is run
/// directly (not through PowerShell) with an argument list, so the {current} identifier is passed verbatim and
/// no shell re-parses anything; the only values ever passed are the constants "minimal"/"network".
/// </summary>
public static class SafeModeService
{
    internal static ISafeModeBackend Backend { get; set; } = new BcdCli();

    internal static ISafeModeBackend Swap(ISafeModeBackend backend)
    {
        var previous = Backend;
        Backend = backend;
        return previous;
    }

    public static SafeBoot Read() => Backend.Read();
    public static void Set(SafeBoot mode) => Backend.Set(mode);
}

/// <summary>Talks to bcdedit.exe directly. Never exercised by tests — they swap a fake.</summary>
internal sealed class BcdCli : ISafeModeBackend
{
    public SafeBoot Read()
    {
        var (ok, output) = Run("/enum", "{current}");
        return ok ? ParseState(output) : SafeBoot.Off; // can't read -> show Off; the actions do not depend on this
    }

    public void Set(SafeBoot mode)
    {
        var args = ArgsFor(mode);
        var (ok, output) = Run(args);
        // Clearing an already-absent safeboot returns non-zero ("element not found") — that IS the goal state, so
        // it is not an error. Setting it, though, must actually succeed.
        if (!ok && mode != SafeBoot.Off)
            throw new InvalidOperationException($"bcdedit failed: {output.Trim()}");
    }

    // The bcdedit arguments for each state. Values are constants, never anything from the user.
    internal static string[] ArgsFor(SafeBoot mode) => mode switch
    {
        SafeBoot.Off => new[] { "/deletevalue", "{current}", "safeboot" },
        SafeBoot.Minimal => new[] { "/set", "{current}", "safeboot", "minimal" },
        SafeBoot.Network => new[] { "/set", "{current}", "safeboot", "network" },
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // "safeboot" is bcdedit's invariant element name; only its presence is trusted for on/off. The value token
    // may be localized, so Network is matched loosely and anything else counts as Minimal.
    internal static SafeBoot ParseState(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("safeboot", StringComparison.OrdinalIgnoreCase))
                return line.Contains("network", StringComparison.OrdinalIgnoreCase) ? SafeBoot.Network : SafeBoot.Minimal;
        }
        return SafeBoot.Off;
    }

    private static (bool ok, string output) Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi);
            if (process is null) return (false, "bcdedit did not start");
            // Drain both streams while waiting, so a full stderr buffer cannot deadlock a blocking read.
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit(15_000);
            string output = outTask.GetAwaiter().GetResult() + errTask.GetAwaiter().GetResult();
            return (process.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            LogService.Log($"bcdedit failed: {ex.Message}");
            return (false, ex.Message);
        }
    }
}
