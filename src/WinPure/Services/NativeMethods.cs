using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinPure.Services;

/// <summary>
/// Win32 broadcasts that tell running apps about setting changes.
/// Without these, theme registry edits only take effect after each app restarts
/// (Explorer repaints partially — white command bar, dark body).
/// </summary>
public static class NativeMethods
{
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;
    private const int ShcneAssocChanged = 0x8000000;
    private const int ShcnfFlush = 0x1000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string? lParam,
        int fuFlags, int uTimeout, IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, string? lParam);

    [DllImport("shell32.dll", SetLastError = false)]
    private static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    private const uint SpiSetMouse = 0x0004;
    private const uint SpifSendChange = 0x0002;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass,
        out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    /// <summary>
    /// DOMAIN\user signed in to a session — the person at the keyboard, as opposed to the
    /// account this process was elevated as. Pure Win32: no PowerShell, no WMI, no NuGet.
    /// Null when it cannot be read.
    /// </summary>
    public static string? GetSessionUser(int sessionId)
    {
        string? user = QuerySession(sessionId, WtsUserName);
        if (string.IsNullOrEmpty(user)) return null;
        // Without the domain the answer is incomplete, not "the same user": a bare name could
        // resolve to a different account of the same name. Incomplete means unknown.
        string? domain = QuerySession(sessionId, WtsDomainName);
        return string.IsNullOrEmpty(domain) ? null : $"{domain}\\{user}";
    }

    private static string? QuerySession(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
            return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { WTSFreeMemory(buffer); }
    }

    /// <summary>Repaints every running app (Explorer, taskbar…) after a light/dark theme change.</summary>
    public static void BroadcastThemeChange()
    {
        try
        {
            // same sequence Windows Settings uses; sent twice because Explorer
            // occasionally misses a single notification
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "ImmersiveColorSet", SmtoAbortIfHung, 100, IntPtr.Zero);
            SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "ImmersiveColorSet");
            SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "TraySettings");
            SHChangeNotify(ShcneAssocChanged, ShcnfFlush, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // cosmetic refresh only — never let it break an apply
        }
    }

    /// <summary>
    /// Tells running apps the environment (PATH) changed, so a newly started program picks up the new PATH.
    /// Already-running processes keep their old PATH until they restart — Windows offers no way around that.
    /// </summary>
    public static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 100, IntPtr.Zero);
            SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment");
        }
        catch
        {
            // best-effort notification; the registry write is what persists
        }
    }

    /// <summary>
    /// Pushes the current Control Panel\Mouse acceleration values to the live session (SPI_SETMOUSE) so a mouse
    /// tweak takes effect without signing out. The tweak already wrote the registry (which persists across logins);
    /// this only syncs the running pointer to it. Best-effort — never breaks an apply.
    /// </summary>
    public static void ApplyMouseSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            int Read(string name, int fallback) => int.TryParse(key?.GetValue(name) as string, out int v) ? v : fallback;
            // Windows defaults with acceleration on: speed 1, thresholds 6 and 10.
            int[] p = { Read("MouseSpeed", 1), Read("MouseThreshold1", 6), Read("MouseThreshold2", 10) };
            SystemParametersInfoW(SpiSetMouse, 0, p, SpifSendChange);
        }
        catch
        {
            // live-apply is best-effort; the registry values are what persist and load at next sign-in
        }
    }
}
