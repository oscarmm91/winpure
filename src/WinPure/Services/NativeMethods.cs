using System.Runtime.InteropServices;

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
}
