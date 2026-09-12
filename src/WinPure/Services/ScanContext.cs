using System.Text.Json;

namespace WinPure.Services;

/// <summary>
/// Snapshot of slow-to-query system state (Appx packages, scheduled tasks, power config),
/// gathered in a single PowerShell pass so the initial scan stays fast.
/// Registry-based detection never touches this.
///
/// Every section reports whether its query actually succeeded. A failed query must never
/// look like an empty result: "no bloatware found" and "could not ask" lead to opposite
/// conclusions, and getting that wrong shows the user "Already optimized" over a machine
/// that is still full of the apps they asked to remove.
/// </summary>
public sealed class ScanContext
{
    public HashSet<string> InstalledPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Task path → enabled. When <see cref="TasksQueryOk"/>, missing means "not on this machine".</summary>
    public Dictionary<string, bool> TaskEnabled { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Extras { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The scan ran and its output could be parsed.</summary>
    public bool Loaded { get; internal set; }
    /// <summary>The installed-packages query succeeded. False → app detection is unknown, not empty.</summary>
    public bool AppsQueryOk { get; internal set; }
    /// <summary>
    /// True when the listing covered every user. False when it fell back to the current user's
    /// packages — which, run as another account, are THAT account's apps, not the machine's.
    /// </summary>
    public bool AppsListedForAllUsers { get; internal set; }
    /// <summary>The scheduled-task query succeeded. False → task detection is unknown, not "disabled".</summary>
    public bool TasksQueryOk { get; internal set; }
    /// <summary>Human-readable list of what could not be determined, for the status bar.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>A third-party scheduled task that runs at logon — i.e. a startup app.</summary>
    public sealed record LogonTask(string Path, string Author, string Action, bool Enabled);

    /// <summary>
    /// Non-Microsoft tasks with a logon trigger, for the Startup Apps page. Tasks under
    /// \Microsoft\ are left out on purpose: those are Windows' own, they are not what
    /// someone opening a startup manager is looking for, and the tweak pages already
    /// cover the handful that matter.
    /// </summary>
    public List<LogonTask> LogonTasks { get; } = new();

    public static readonly string[] WatchedTasks =
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
        // privacy-diagnostic-tasks. A ScheduledTaskAction whose task is not listed here always
        // reads as already applied — every task a tweak disables must be added.
        @"\Microsoft\Windows\Application Experience\StartupAppTask",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Maps\MapsToastTask",
        @"\Microsoft\Windows\Maps\MapsUpdateTask",
    };

    public static ScanContext Gather()
    {
        var ctx = new ScanContext();
        string taskList = string.Join(",", WatchedTasks.Select(t => $"'{t}'"));

        // Each section fails on its own and says so, instead of one global
        // SilentlyContinue turning every failure into an innocent-looking empty value.
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $r = [ordered]@{}

            $r.appsOk = $false
            try {
                $r.apps = @(Get-AppxPackage -AllUsers | Select-Object -ExpandProperty Name -Unique)
                $r.appsOk = $true
            } catch {
                # -AllUsers needs elevation and a healthy AppX stack; fall back to this user's
                # packages, which is still far better than reporting "nothing installed".
                try {
                    $r.apps = @(Get-AppxPackage | Select-Object -ExpandProperty Name -Unique)
                    $r.appsOk = $true
                    $r.appsScope = 'user'
                } catch {
                    $r.apps = @()
                    $r.appsError = $_.Exception.Message
                }
            }

            $watched = @({{taskList}})
            $tasks = @{}
            $logon = @()
            $r.tasksOk = $false
            try {
                # One call for all of them: if it succeeds, a task missing from the result
                # really is absent from this machine rather than a query that blew up.
                foreach ($t in Get-ScheduledTask) {
                    $full = [string]$t.TaskPath + [string]$t.TaskName
                    if ($watched -contains $full) { $tasks[$full] = ($t.State -ne 'Disabled') }

                    # Same pass feeds the Startup Apps page: third-party logon-trigger tasks.
                    if ($t.TaskPath -notlike '\Microsoft\*' -and $t.Triggers) {
                        $isLogon = $false
                        foreach ($trg in $t.Triggers) {
                            if ($trg.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger') { $isLogon = $true }
                        }
                        if ($isLogon) {
                            $exe = ''
                            foreach ($a in $t.Actions) { if (-not $exe -and $a.Execute) { $exe = [string]$a.Execute } }
                            $logon += [ordered]@{
                                path    = $full
                                author  = [string]$t.Author
                                action  = $exe
                                enabled = ($t.State -ne 'Disabled')
                            }
                        }
                    }
                }
                $r.tasksOk = $true
            } catch {
                $r.tasksError = $_.Exception.Message
            }
            $r.tasks = $tasks
            $r.logonTasks = @($logon)

            # Hibernation and the power plan are read straight from the registry by their actions.
            # Reserved storage has no registry value that reflects it, only DISM's enum name; it needs
            # elevation, so unelevated this throws and that tweak reads Unknown.
            try { $r.reservedStorage = [string](Get-WindowsReservedStorageState -ErrorAction Stop).ReservedStorageState } catch { }
            try { $r.fwTelemetryBlock = [bool](Get-NetFirewallRule -DisplayName 'WinPure - Block Telemetry Client' -ErrorAction SilentlyContinue) } catch { }
            try {
                $r.onedrive = [bool]((Test-Path "$env:ProgramFiles\Microsoft OneDrive\OneDrive.exe") -or (Test-Path "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"))
            } catch { }

            # For the pre-apply guards: a .NET enum name, so it reads the same on a Spanish Windows.
            # BitLocker is deliberately NOT queried here - see GuardInputs.ReadBitLockerStatus.
            try { $r.eventLog = [string](Get-Service -Name EventLog).Status } catch { }

            $r | ConvertTo-Json -Depth 4 -Compress
            """;

        // Read-only, so it may die with the app: nothing is left half-done.
        var result = PowerShellRunner.Run(script, 60_000, dieWithApp: true);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            string why = result.TimedOut ? "the system scan timed out"
                : string.IsNullOrWhiteSpace(result.Error) ? "the system scan produced no output"
                : $"the system scan failed: {FirstLine(result.Error)}";
            ctx.Warnings.Add(why);
            LogService.Log($"Scan failed: {why}");
            return ctx;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            ctx.AppsQueryOk = root.TryGetProperty("appsOk", out var appsOk) && appsOk.GetBoolean();
            if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
                foreach (var a in apps.EnumerateArray())
                    if (a.GetString() is { } name) ctx.InstalledPackages.Add(name);

            ctx.TasksQueryOk = root.TryGetProperty("tasksOk", out var tasksOk) && tasksOk.GetBoolean();
            if (root.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Object)
                foreach (var prop in tasks.EnumerateObject())
                    ctx.TaskEnabled[prop.Name] = prop.Value.GetBoolean();

            if (root.TryGetProperty("logonTasks", out var logon) && logon.ValueKind == JsonValueKind.Array)
                foreach (var t in logon.EnumerateArray())
                {
                    string path = Text(t, "path");
                    if (path.Length == 0) continue;
                    ctx.LogonTasks.Add(new LogonTask(
                        path, Text(t, "author"), Text(t, "action"),
                        t.TryGetProperty("enabled", out var en) && en.GetBoolean()));
                }

            foreach (var key in new[] { "reservedStorage", "eventLog" })
                if (root.TryGetProperty(key, out var v) && v.GetString() is { } s) ctx.Extras[key] = s;
            if (root.TryGetProperty("fwTelemetryBlock", out var fw)) ctx.Extras["fwTelemetryBlock"] = fw.GetBoolean() ? "1" : "0";
            if (root.TryGetProperty("onedrive", out var od)) ctx.Extras["onedrive"] = od.GetBoolean() ? "1" : "0";

            ctx.Loaded = true;
            ctx.AppsListedForAllUsers = ctx.AppsQueryOk && Text(root, "appsScope") != "user";

            if (!ctx.AppsQueryOk)
            {
                ctx.Warnings.Add("installed apps could not be listed");
                LogService.Log($"Scan: app listing failed: {Text(root, "appsError")}");
            }
            else if (Text(root, "appsScope") == "user")
            {
                LogService.Log("Scan: listed this user's packages only (-AllUsers was refused).");
            }

            if (!ctx.TasksQueryOk)
            {
                ctx.Warnings.Add("scheduled tasks could not be read");
                LogService.Log($"Scan: scheduled task listing failed: {Text(root, "tasksError")}");
            }
        }
        catch (Exception ex)
        {
            ctx.Warnings.Add("the system scan returned something unreadable");
            LogService.Log($"Scan output could not be parsed: {ex.Message}");
        }
        return ctx;
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FirstLine(string text)
    {
        string s = text.Trim();
        int nl = s.IndexOf('\n');
        return (nl < 0 ? s : s[..nl]).Trim();
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
