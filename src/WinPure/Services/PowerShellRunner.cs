using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinPure.Services;

public static class PowerShellRunner
{
    public sealed record PsResult(int ExitCode, string Output, string Error)
    {
        public bool Success => ExitCode == 0;
        /// <summary>True when the child process had to be killed after the timeout.</summary>
        public bool TimedOut { get; init; }
        /// <summary>True when the caller cancelled and the child process was killed.</summary>
        public bool Cancelled { get; init; }
    }

    /// <summary>
    /// Runs a PowerShell script hidden and returns its output. Never throws.
    /// Output is drained asynchronously so a chatty or hung child can neither fill the
    /// pipe buffers nor outlive the timeout.
    /// </summary>
    /// <param name="dieWithApp">
    /// True (the default): the child is ended by Windows if WinPure exits or is killed. Pass false
    /// only for work that must never be interrupted halfway, such as DISM repairing Windows.
    /// </param>
    public static PsResult Run(string script, int timeoutMs = 120_000, CancellationToken cancel = default,
        bool dieWithApp = true)
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
            if (dieWithApp) AssignToJob(p);

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

            // Poll instead of one long wait so a cancel from the UI is noticed promptly.
            var clock = Stopwatch.StartNew();
            while (!p.WaitForExit(200))
            {
                if (cancel.IsCancellationRequested)
                {
                    Kill(p);
                    LogService.Log($"PowerShell cancelled by the user: {FirstLine(script)}");
                    return new PsResult(-1, stdout.ToString().Trim(), "Cancelled") { Cancelled = true };
                }
                if (clock.ElapsedMilliseconds > timeoutMs)
                {
                    Kill(p);
                    LogService.Log($"PowerShell timed out after {timeoutMs} ms: {FirstLine(script)}");
                    return new PsResult(-1, stdout.ToString().Trim(), "Timed out") { TimedOut = true };
                }
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

    // ---------------------------------------------------------------- kill-on-close job

    /// <summary>
    /// Whether the most recent child was placed in the job. Tests use it to tell "the job does
    /// not work" apart from "this environment refuses nested jobs".
    /// </summary>
    internal static bool LastJobAssignOk { get; private set; }

    private static readonly Lazy<IntPtr> ChildJob = new(CreateKillOnCloseJob);

    /// <summary>
    /// Children go into a Windows job object marked kill-on-close, whose only handle lives in this
    /// process and is never closed by hand. When WinPure exits — or is killed — Windows closes that
    /// handle and ends every child with it. Without this, closing the app mid-scan left
    /// powershell.exe running, with the timeout that should have stopped it gone along with the app.
    /// </summary>
    private static void AssignToJob(Process p)
    {
        try
        {
            IntPtr job = ChildJob.Value;
            LastJobAssignOk = job != IntPtr.Zero && AssignProcessToJobObject(job, p.Handle);
            if (!LastJobAssignOk)
                LogService.Log("Could not place PowerShell in the kill-on-close job; it may outlive the app.");
        }
        catch { LastJobAssignOk = false; }
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        return SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info,
            Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()) ? job : IntPtr.Zero;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static void Kill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        try { p.WaitForExit(5_000); } catch { }
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
