using System.Runtime.InteropServices;

namespace WinPure.Services;

/// <summary>A power action the Power page can run.</summary>
public enum PowerAction { Shutdown, Restart, SignOut, Sleep, Hibernate, Lock }

/// <summary>Performs power actions. Swappable so tests never actually shut the machine down.</summary>
public interface IPowerBackend
{
    /// <summary>Schedules a shutdown or restart in <paramref name="delaySeconds"/> using Windows' own
    /// shutdown.exe, so it persists even if WinPure is closed. force closes apps without saving.</summary>
    (bool Ok, string? Error) ShutdownOrRestart(bool restart, int delaySeconds, bool force);
    /// <summary>Cancels a shutdown/restart scheduled through shutdown.exe (shutdown /a).</summary>
    (bool Ok, string? Error) Abort();
    /// <summary>Returns whether Windows actually slept/hibernated (false e.g. when hibernation is disabled).</summary>
    bool Sleep(bool hibernate);
    /// <summary>Returns whether the workstation was locked.</summary>
    bool Lock();
    (bool Ok, string? Error) SignOut();
}

/// <summary>
/// The Power page: schedule a shutdown or restart (via Windows' shutdown.exe /t, which persists even if
/// WinPure is closed and is cancelled with shutdown /a), or sleep / hibernate / lock / sign out now. These
/// are one-shot system actions — not tweaks, no backup — so the page confirms the destructive ones first.
/// </summary>
public static class PowerService
{
    public static IPowerBackend Backend { get; private set; } = new WindowsPowerBackend();

    /// <summary>Swaps the backend for a fake, so tests never power the machine off. Returns the previous one.</summary>
    internal static IPowerBackend Swap(IPowerBackend backend)
    {
        var old = Backend;
        Backend = backend;
        return old;
    }

    /// <summary>Only Shutdown and Restart take a timed delay — Windows' shutdown.exe provides the timer and
    /// the abort. The others fire immediately (Windows has no built-in timer for them).</summary>
    public static bool SupportsDelay(PowerAction action) => action is PowerAction.Shutdown or PowerAction.Restart;

    /// <summary>One week, in minutes. The cap keeps minutes*60 well inside int range: without it, a typo'd
    /// 8-9 digit delay would overflow, wrap negative and — after Math.Max(0, …) — schedule an IMMEDIATE
    /// shutdown behind a dialog that claimed it was centuries away.</summary>
    public const int MaxDelayMinutes = 10080;

    /// <summary>Parses the delay field: a whole number of minutes from 0 to <see cref="MaxDelayMinutes"/>.</summary>
    public static bool TryParseDelayMinutes(string? text, out int minutes)
    {
        minutes = 0;
        return int.TryParse(text?.Trim(), out minutes) && minutes >= 0 && minutes <= MaxDelayMinutes;
    }

    /// <summary>Runs the action. delaySeconds applies only to Shutdown/Restart (see SupportsDelay).</summary>
    public static (bool Ok, string? Error) Run(PowerAction action, int delaySeconds, bool force)
    {
        switch (action)
        {
            case PowerAction.Shutdown: return Backend.ShutdownOrRestart(restart: false, delaySeconds, force);
            case PowerAction.Restart: return Backend.ShutdownOrRestart(restart: true, delaySeconds, force);
            case PowerAction.SignOut: return Backend.SignOut();
            // These report the native call's real result, so a no-op (e.g. hibernation disabled) is not
            // reported as success. The error text is left to the caller so it can be a translated message.
            case PowerAction.Sleep: return Backend.Sleep(hibernate: false) ? (true, null) : (false, null);
            case PowerAction.Hibernate: return Backend.Sleep(hibernate: true) ? (true, null) : (false, null);
            case PowerAction.Lock: return Backend.Lock() ? (true, null) : (false, null);
            default: return (false, "Unknown action");
        }
    }

    public static (bool Ok, string? Error) Abort() => Backend.Abort();
}

/// <summary>The real backend: shutdown.exe for shutdown/restart/sign-out, P/Invoke for sleep/hibernate/lock.</summary>
public sealed class WindowsPowerBackend : IPowerBackend
{
    public (bool Ok, string? Error) ShutdownOrRestart(bool restart, int delaySeconds, bool force)
    {
        int seconds = Math.Max(0, delaySeconds);
        string flag = restart ? "/r" : "/s";
        string args = force ? $"{flag} /t {seconds} /f" : $"{flag} /t {seconds}";
        var r = PowerShellRunner.Run($"shutdown.exe {args}", 30_000);
        return (r.Success, r.Success ? null : r.Error);
    }

    public (bool Ok, string? Error) Abort()
    {
        // shutdown /a exits 0 when it aborts a pending shutdown, non-zero (1116) when nothing is scheduled;
        // Ok reflects that so the page can tell "cancelled" from "nothing to cancel".
        var r = PowerShellRunner.Run("shutdown.exe /a", 30_000);
        return (r.Success, null);
    }

    public bool Sleep(bool hibernate) => SetSuspendState(hibernate, bForce: false, bWakeupEventsDisabled: false);

    public bool Lock() => LockWorkStation();

    public (bool Ok, string? Error) SignOut()
    {
        var r = PowerShellRunner.Run("shutdown.exe /l", 30_000);
        return (r.Success, r.Success ? null : r.Error);
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool bForce, bool bWakeupEventsDisabled);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
