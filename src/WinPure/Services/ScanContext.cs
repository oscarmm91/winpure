using System.Text.Json;

namespace WinPure.Services;

/// <summary>
/// Snapshot of slow-to-query system state (Appx packages, scheduled tasks, power config),
/// gathered in a single PowerShell pass so the initial scan stays fast.
/// Registry-based detection never touches this.
/// </summary>
public sealed class ScanContext
{
    public HashSet<string> InstalledPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Task path → enabled. Tasks not present are simply missing from the map.</summary>
    public Dictionary<string, bool> TaskEnabled { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Extras { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Loaded { get; private set; }

    public static readonly string[] WatchedTasks =
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    };

    public static ScanContext Gather()
    {
        var ctx = new ScanContext();
        string taskList = string.Join(",", WatchedTasks.Select(t => $"'{t}'"));
        string script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $r = [ordered]@{}
            $r.apps = @(Get-AppxPackage -AllUsers | Select-Object -ExpandProperty Name -Unique)
            $tasks = @{}
            foreach ($t in @({{taskList}})) {
                $dir = Split-Path $t; $name = Split-Path $t -Leaf
                $task = Get-ScheduledTask -TaskPath ($dir + '\') -TaskName $name -ErrorAction SilentlyContinue
                if ($task) { $tasks[$t] = ($task.State -ne 'Disabled') }
            }
            $r.tasks = $tasks
            $power = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -ErrorAction SilentlyContinue
            $r.hibernate = if ($null -ne $power.HibernateEnabled) { [string]$power.HibernateEnabled } else { '1' }
            $scheme = powercfg /getactivescheme
            $r.powerplan = [string]$scheme
            $r.fwTelemetryBlock = [bool](Get-NetFirewallRule -DisplayName 'WinPure - Block Telemetry Client' -ErrorAction SilentlyContinue)
            $r.onedrive = [bool]((Test-Path "$env:ProgramFiles\Microsoft OneDrive\OneDrive.exe") -or (Test-Path "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"))
            $r | ConvertTo-Json -Depth 4 -Compress
            """;

        var result = PowerShellRunner.Run(script, 60_000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return ctx;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;
            if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
                foreach (var a in apps.EnumerateArray())
                    if (a.GetString() is { } name) ctx.InstalledPackages.Add(name);

            if (root.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Object)
                foreach (var prop in tasks.EnumerateObject())
                    ctx.TaskEnabled[prop.Name] = prop.Value.GetBoolean();

            foreach (var key in new[] { "hibernate", "powerplan" })
                if (root.TryGetProperty(key, out var v) && v.GetString() is { } s) ctx.Extras[key] = s;
            if (root.TryGetProperty("fwTelemetryBlock", out var fw)) ctx.Extras["fwTelemetryBlock"] = fw.GetBoolean() ? "1" : "0";
            if (root.TryGetProperty("onedrive", out var od)) ctx.Extras["onedrive"] = od.GetBoolean() ? "1" : "0";

            ctx.Loaded = true;
        }
        catch
        {
            // leave ctx partially filled; detection falls back to Unknown
        }
        return ctx;
    }

    public bool AnyPackageInstalled(IEnumerable<string> namePatterns)
    {
        foreach (var pattern in namePatterns)
            foreach (var installed in InstalledPackages)
                if (installed.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
        return false;
    }
}
