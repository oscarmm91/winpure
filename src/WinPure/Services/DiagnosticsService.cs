using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace WinPure.Services;

/// <summary>
/// Read-only system facts for the Diagnostics page, and a one-click support bundle (a zip of WinPure's own
/// logs plus a plain-text system summary). Nothing here changes the system; the export writes only to a file
/// the user picks. The summary is deliberately low-detail — no user name, SID or serial numbers.
/// </summary>
public static class DiagnosticsService
{
    /// <summary>The real logs folder. Settable so a test can point the export at a throwaway folder.</summary>
    private static string? _logsDirOverride;
    internal static string? LogsDirForTests { set => _logsDirOverride = value; }

    public static string LogsDir => _logsDirOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPure", "Logs");

    /// <summary>Labelled, read-only facts about this PC. Never throws — an unreadable field is skipped.</summary>
    public static List<(string Label, string Value)> Info()
    {
        var rows = new List<(string, string)>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, value!)); }

        try
        {
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = cv?.GetValue("ProductName") as string ?? "Windows";
            string display = cv?.GetValue("DisplayVersion") as string ?? "";
            string build = cv?.GetValue("CurrentBuildNumber") as string ?? "";
            string ubr = (cv?.GetValue("UBR") is int u) ? $".{u}" : "";
            if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            Add("Windows", product);   // ProductName already carries the edition (e.g. "Windows 11 Pro")
            Add("Version", $"{display} (build {build}{ubr})".Trim());

            if (cv?.GetValue("InstallDate") is int epoch && epoch > 0)
                Add("Install date", DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime.ToString("dd MMM yyyy"));
        }
        catch { }

        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            Add("Processor", (cpu?.GetValue("ProcessorNameString") as string)?.Trim());
        }
        catch { }

        try
        {
            var mem = MemoryService.Query();
            if (mem.TotalBytes > 0)
                Add("Memory", $"{FormatGB(mem.TotalBytes)} total, {FormatGB(mem.AvailableBytes)} free");
        }
        catch { }

        try
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            Add("Uptime", up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m" : $"{up.Hours}h {up.Minutes}m");
        }
        catch { }

        Add("Device", SafeMachineName());
        Add("WinPure", typeof(DiagnosticsService).Assembly.GetName().Version?.ToString());
        return rows;
    }

    /// <summary>Writes a zip of the WinPure logs and a system summary to <paramref name="zipPath"/>.</summary>
    public static (bool Ok, string? Error) Export(string zipPath)
    {
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            var summary = zip.CreateEntry("winpure-system.txt");
            using (var w = new StreamWriter(summary.Open()))
            {
                w.WriteLine("WinPure diagnostics");
                w.WriteLine(DateTimeOffset.Now.ToString("u"));
                w.WriteLine();
                foreach (var (label, value) in Info()) w.WriteLine($"{label}: {value}");
            }

            if (Directory.Exists(LogsDir))
                foreach (var log in Directory.EnumerateFiles(LogsDir, "*.log"))
                    try { zip.CreateEntryFromFile(log, "logs/" + Path.GetFileName(log)); } catch { }

            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string FormatGB(ulong bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; } catch { return ""; }
    }
}
