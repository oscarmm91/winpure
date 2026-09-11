using System.Diagnostics;
using System.Text;

namespace WinPure.Services;

public static class PowerShellRunner
{
    public sealed record PsResult(int ExitCode, string Output, string Error)
    {
        public bool Success => ExitCode == 0;
        /// <summary>True when the child process had to be killed after the timeout.</summary>
        public bool TimedOut { get; init; }
    }

    /// <summary>
    /// Runs a PowerShell script hidden and returns its output. Never throws.
    /// Output is drained asynchronously so a chatty or hung child can neither fill the
    /// pipe buffers nor outlive the timeout.
    /// </summary>
    public static PsResult Run(string script, int timeoutMs = 120_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Encode(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Windows PowerShell 5.1 writes in the console codepage (cp1252 here), which
                // mangles accents in service and package names. Force UTF-8 on both ends.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var p = Process.Start(psi);
            if (p is null) return new PsResult(-1, "", "Could not start powershell.exe");

            // Read both streams on background threads: ReadToEnd() on a hung child blocks
            // forever and WaitForExit(timeout) below would never even be reached.
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var outDone = new ManualResetEventSlim(false);
            using var errDone = new ManualResetEventSlim(false);

            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) outDone.Set(); else stdout.AppendLine(e.Data);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) errDone.Set(); else stderr.AppendLine(e.Data);
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                try { p.WaitForExit(5_000); } catch { }
                LogService.Log($"PowerShell timed out after {timeoutMs} ms: {FirstLine(script)}");
                return new PsResult(-1, stdout.ToString().Trim(), "Timed out") { TimedOut = true };
            }

            // The process exited; give the readers a moment to flush what is still buffered.
            outDone.Wait(2_000);
            errDone.Wait(2_000);

            return new PsResult(p.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (Exception ex)
        {
            return new PsResult(-1, "", ex.Message);
        }
    }

    /// <summary>Runs a script and throws with the real error text when it fails.</summary>
    public static void RunOrThrow(string script, string what, int timeoutMs = 120_000)
    {
        var result = Run(script, timeoutMs);
        if (result.Success) return;
        string detail = !string.IsNullOrWhiteSpace(result.Error) ? result.Error
            : !string.IsNullOrWhiteSpace(result.Output) ? result.Output
            : $"exit code {result.ExitCode}";
        throw new InvalidOperationException($"{what} failed: {detail}");
    }

    private static string FirstLine(string script)
    {
        string s = script.TrimStart();
        int nl = s.IndexOf('\n');
        return (nl < 0 ? s : s[..nl]).Trim();
    }

    private static string Encode(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
