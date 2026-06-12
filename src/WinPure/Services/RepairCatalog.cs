namespace WinPure.Services;

/// <summary>A one-shot maintenance tool (no toggle state). Inspired by Chris Titus WinUtil fixes.</summary>
public sealed class RepairTool
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Icon { get; init; } = "";
    public required string Script { get; init; }
    public int TimeoutMs { get; init; } = 600_000;
    /// <summary>Shown in a confirmation dialog before running. Null = run without confirmation.</summary>
    public string? ConfirmText { get; init; }
    public bool RequiresRestart { get; init; }
}

public static class RepairCatalog
{
    public static IReadOnlyList<RepairTool> Build() => new[]
    {
        new RepairTool
        {
            Id = "repair-restore-point",
            Name = "Create System Restore Point",
            Description = "Create a Windows restore point before making big changes.",
            Icon = "",
            Script = """
                $ErrorActionPreference = 'Stop'
                New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' `
                    -Name 'SystemRestorePointCreationFrequency' -Value 0 -Type DWord -Force | Out-Null
                Enable-ComputerRestore -Drive "$env:SystemDrive\"
                Checkpoint-Computer -Description 'WinPure Restore Point' -RestorePointType 'MODIFY_SETTINGS'
                Write-Output 'Restore point created.'
                """,
        },
        new RepairTool
        {
            Id = "repair-system-files",
            Name = "Repair System Files (SFC + DISM)",
            Description = "Scan and repair corrupted Windows system files. Can take 15–30 minutes.",
            Icon = "",
            ConfirmText = "This runs 'sfc /scannow' followed by 'DISM /RestoreHealth'. It can take 15–30 minutes and cannot be cancelled midway. Continue?",
            TimeoutMs = 3_600_000,
            Script = """
                sfc /scannow
                Dism /Online /Cleanup-Image /RestoreHealth
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-windows-update",
            Name = "Reset Windows Update",
            Description = "Fix stuck updates: clear the download cache and restart update services.",
            Icon = "",
            ConfirmText = "This stops the update services, clears the Windows Update download cache (SoftwareDistribution\\Download) and restarts the services. Continue?",
            Script = """
                $ErrorActionPreference = 'SilentlyContinue'
                Stop-Service -Name wuauserv, BITS, cryptsvc -Force
                Remove-Item "$env:ALLUSERSPROFILE\Microsoft\Network\Downloader\qmgr*.dat" -Force
                Remove-Item "$env:SystemRoot\SoftwareDistribution\Download.bak" -Recurse -Force
                Rename-Item "$env:SystemRoot\SoftwareDistribution\Download" 'Download.bak' -Force
                Remove-Item "$env:SystemRoot\WindowsUpdate.log" -Force
                Start-Service -Name cryptsvc, BITS, wuauserv
                Write-Output 'Windows Update components were reset.'
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-network",
            Name = "Reset Network",
            Description = "Reset Winsock, the TCP/IP stack and flush the DNS cache.",
            Icon = "",
            ConfirmText = "This resets Winsock and the TCP/IP stack (netsh) and flushes DNS. You should restart afterwards. Continue?",
            RequiresRestart = true,
            Script = """
                netsh winsock reset
                netsh int ip reset
                ipconfig /flushdns
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-temp-files",
            Name = "Clean Temporary Files",
            Description = "Delete user and system temp files to free disk space.",
            Icon = "",
            Script = """
                $ErrorActionPreference = 'SilentlyContinue'
                $before = (Get-PSDrive -Name $env:SystemDrive.TrimEnd(':')).Free
                Get-ChildItem $env:TEMP -Force | Remove-Item -Recurse -Force
                Get-ChildItem "$env:SystemRoot\Temp" -Force | Remove-Item -Recurse -Force
                $after = (Get-PSDrive -Name $env:SystemDrive.TrimEnd(':')).Free
                $freed = [math]::Max(0, ($after - $before) / 1MB)
                Write-Output ("Temp files cleaned. Freed {0:N0} MB." -f $freed)
                exit 0
                """,
        },
    };
}
