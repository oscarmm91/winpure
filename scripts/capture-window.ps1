param([Parameter(Mandatory)][string]$OutFile, [string]$Title)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
if ($Title) {
    $hwnd = [Win32Cap]::FindWindow($null, $Title)
    if ($hwnd -eq [IntPtr]::Zero) { throw "window '$Title' not found" }
} else {
    $proc = Get-Process WinPure -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
    $hwnd = $proc.MainWindowHandle
}
$rect = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win32Cap]::PrintWindow($hwnd, $hdc, 3) | Out-Null   # 3 = PW_RENDERFULLCONTENT (captures DX/WPF)
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved ${w}x${h} -> $OutFile"
