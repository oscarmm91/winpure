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
    /// <summary>
    /// What the card says on success, in place of the script's last line of output; {0} is that line. Null shows
    /// the line itself — right for tools whose output is Windows' own text, already in the user's language.
    /// </summary>
    public string? DoneText { get; init; }
    public bool RequiresRestart { get; init; }
    /// <summary>
    /// Whether killing this mid-run is safe. False for anything that repairs Windows itself:
    /// a half-finished DISM leaves the component store inconsistent, so the answer there is
    /// to show the user it is still alive, not to offer a button that can make it worse.
    /// </summary>
    public bool Cancellable { get; init; } = true;
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
            DoneText = "Restore point created.",
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
            ConfirmText = "This runs 'sfc /scannow' followed by 'DISM /RestoreHealth'. It can take 15–30 minutes and cannot be cancelled midway — WinPure will show you the elapsed time so you can tell it is still working. Continue?",
            TimeoutMs = 3_600_000,
            Cancellable = false,
            Script = """
                sfc /scannow
                Dism /Online /Cleanup-Image /RestoreHealth
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-component-cleanup",
            Name = "Clean Up the Component Store (DISM)",
            Description = "Reclaim disk space by removing superseded Windows Update files from the component store (WinSxS). Can take several minutes.",
            Icon = "",   // Segoe Fluent Repair glyph, written as an escape so the diff is readable
            // Plain StartComponentCleanup only — never /ResetBase, which makes the updates currently
            // installed non-uninstallable. Like SFC/DISM this touches the component store, so killing it
            // midway can leave it inconsistent: not cancellable, the elapsed-time readout reassures instead.
            ConfirmText = "This runs 'DISM /Online /Cleanup-Image /StartComponentCleanup' to remove superseded update components from WinSxS. It can take several minutes and cannot be cancelled midway. Continue?",
            TimeoutMs = 1_800_000,
            Cancellable = false,
            Script = """
                Dism /Online /Cleanup-Image /StartComponentCleanup
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-windows-update",
            Name = "Reset Windows Update",
            Description = "Fix stuck updates: clear the download cache and restart update services.",
            DoneText = "Windows Update components were reset.",
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
            ConfirmText = "This deletes everything in your temporary folder and in the Windows Temp folder. Files that are in use are skipped. Continue?",
            Name = "Clean Temporary Files",
            Description = "Delete user and system temp files to free disk space.",
            DoneText = "Temp files cleaned. Freed {0} MB.",
            Icon = "",
            Script = """
                $ErrorActionPreference = 'SilentlyContinue'
                $before = (Get-PSDrive -Name $env:SystemDrive.TrimEnd(':')).Free
                Get-ChildItem $env:TEMP -Force | Remove-Item -Recurse -Force
                Get-ChildItem "$env:SystemRoot\Temp" -Force | Remove-Item -Recurse -Force
                $after = (Get-PSDrive -Name $env:SystemDrive.TrimEnd(':')).Free
                $freed = [math]::Max(0, ($after - $before) / 1MB)
                Write-Output ('{0:N0}' -f $freed)
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-reregister-apps",
            Name = "Re-register Store Apps (fix Start menu)",
            Description = "Re-register your installed Store apps — a common fix for a broken Start menu, Store or Settings. Nothing is uninstalled.",
            DoneText = "Store apps were re-registered.",
            Icon = "",
            // Cancellable: killing this between iterations is safe — already-registered packages stay done and
            // the rest are simply skipped, exactly as if it had not run. (Individual Add-AppxPackage failures
            // are expected — framework packages, etc. — so the tool reports success once the pass completes.)
            ConfirmText = "This re-registers every Store app installed for your account (Add-AppxPackage -Register). It is a common fix for a broken Start menu, Store or Settings and does not delete anything. It can take a few minutes. Continue?",
            TimeoutMs = 1_200_000,
            Script = """
                $ErrorActionPreference = 'SilentlyContinue'
                Get-AppxPackage | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register "$($_.InstallLocation)\AppXManifest.xml" }
                exit 0
                """,
        },
        new RepairTool
        {
            Id = "repair-winget-upgrade-all",
            Name = "Update All Apps (winget)",
            Description = "Upgrade every app winget can update to its latest version.",
            DoneText = "Apps were updated to their latest versions.",
            Icon = "",
            // Not cancellable, like SFC/DISM: an installer winget launches, cut off halfway, leaves a broken
            // app behind (the same reason the Install page never dies with the app). The exit code is winget's
            // own, so no internet / a failed upgrade reports failure instead of a false "updated".
            ConfirmText = "This runs 'winget upgrade --all' to download and install the latest version of every app winget can update. It can take a while and cannot be cancelled midway. Continue?",
            TimeoutMs = 1_800_000,
            Cancellable = false,
            Script = """
                winget upgrade --all --silent --include-unknown --accept-source-agreements --accept-package-agreements
                exit $LASTEXITCODE
                """,
        },
        new RepairTool
        {
            Id = "repair-install-vcredist",
            Name = "Install Visual C++ Redistributables",
            Description = "Install the Microsoft Visual C++ 2015-2022 runtimes (x64 and x86) — a common fix for 'missing VCRUNTIME/MSVCP DLL' errors.",
            DoneText = "Visual C++ Redistributables installed.",
            Icon = "",
            // Not cancellable, for the same reason as the winget upgrade tool. Success is judged by the EFFECT
            // (both redistributables present via winget list), not by the install exit code — winget returns
            // non-zero when a package is already installed, which is not a failure here.
            ConfirmText = "This installs the Microsoft Visual C++ Redistributables (2015-2022, x64 and x86) via winget — a common fix for 'missing VCRUNTIME140.dll / MSVCP140.dll' errors. It cannot be cancelled midway. Continue?",
            TimeoutMs = 900_000,
            Cancellable = false,
            Script = """
                winget install --id Microsoft.VCRedist.2015+.x64 -e --silent --accept-source-agreements --accept-package-agreements
                winget install --id Microsoft.VCRedist.2015+.x86 -e --silent --accept-source-agreements --accept-package-agreements
                $x64 = winget list --id Microsoft.VCRedist.2015+.x64 -e --accept-source-agreements 2>$null | Select-String 'Microsoft.VCRedist.2015'
                $x86 = winget list --id Microsoft.VCRedist.2015+.x86 -e --accept-source-agreements 2>$null | Select-String 'Microsoft.VCRedist.2015'
                if ($x64 -and $x86) { exit 0 } else { exit 1 }
                """,
        },
    };
}
