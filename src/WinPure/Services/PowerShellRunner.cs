using System.Diagnostics;

namespace WinPure.Services;

public static class PowerShellRunner
{
    public sealed record PsResult(int ExitCode, string Output, string Error)
    {
        public bool Success => ExitCode == 0;
    }

    /// <summary>Runs a PowerShell script hidden and returns its output. Never throws.</summary>
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
            };
            using var p = Process.Start(psi);
            if (p is null) return new PsResult(-1, "", "Could not start powershell.exe");
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return new PsResult(-1, output, "Timed out");
            }
            return new PsResult(p.ExitCode, output.Trim(), error.Trim());
        }
        catch (Exception ex)
        {
            return new PsResult(-1, "", ex.Message);
        }
    }

    private static string Encode(string script) =>
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
}
