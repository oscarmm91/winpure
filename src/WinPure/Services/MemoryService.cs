using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinPure.Services;

/// <summary>A snapshot of physical memory use.</summary>
public readonly record struct MemoryInfo(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public int LoadPercent => TotalBytes == 0 ? 0 : (int)Math.Round(100.0 * UsedBytes / TotalBytes);
}

/// <summary>Reads memory use and (best-effort) trims working sets and the standby cache.</summary>
public interface IMemoryBackend
{
    MemoryInfo Query();
    /// <summary>Trims process working sets and clears the standby list. Elevated; effect is usually temporary.</summary>
    void Purge();
}

/// <summary>
/// The honest "free up memory" tool. On modern Windows the memory manager already reclaims RAM on demand,
/// so trimming working sets and clearing the standby cache mostly moves numbers on a graph and any gain is
/// short-lived — the page says so. It is a manual, on-demand action, never a resident background "booster".
/// The real effect (EmptyWorkingSet / NtSetSystemInformation) is not exercised by tests, only the maths and
/// the read-only query; the backend is swappable so tests never touch real process memory.
/// </summary>
public static class MemoryService
{
    public static IMemoryBackend Backend { get; private set; } = new WindowsMemoryBackend();

    /// <summary>Swaps the backend for a fake, so tests never trim real working sets. Returns the previous one.</summary>
    internal static IMemoryBackend Swap(IMemoryBackend backend)
    {
        var old = Backend;
        Backend = backend;
        return old;
    }

    public static MemoryInfo Query() => Backend.Query();

    /// <summary>Trims memory and reports the before/after snapshots.</summary>
    public static (MemoryInfo Before, MemoryInfo After) Clean()
    {
        var before = Backend.Query();
        Backend.Purge();
        var after = Backend.Query();
        return (before, after);
    }
}

/// <summary>The real backend: GlobalMemoryStatusEx to read, EmptyWorkingSet + standby-list purge to trim.</summary>
public sealed class WindowsMemoryBackend : IMemoryBackend
{
    public MemoryInfo Query()
    {
        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref s)
            ? new MemoryInfo(s.ullTotalPhys, s.ullAvailPhys)
            : default;
    }

    public void Purge()
    {
        // Trim every process we are allowed to touch. Access is denied for many protected processes; that is
        // expected and skipped. EmptyWorkingSet pushes pages out to the standby list / page file. The whole
        // enumeration is guarded too, so a rare OS-level failure of GetProcesses() cannot escape Purge().
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try { EmptyWorkingSet(p.Handle); } catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }

        // Clear the standby list itself so the "available" figure reflects the trim. Needs the profiling
        // privilege, which an elevated process has; if it is not granted the call simply fails and is ignored.
        TryPurgeStandbyList();
    }

    private static void TryPurgeStandbyList()
    {
        try
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int command = (int)MemoryListCommand.MemoryPurgeStandbyList;
            var handle = GCHandle.Alloc(command, GCHandleType.Pinned);
            try { NtSetSystemInformation(SystemMemoryListInformation, handle.AddrOfPinnedObject(), sizeof(int)); }
            finally { handle.Free(); }
        }
        catch { }
    }

    private enum MemoryListCommand { MemoryPurgeStandbyList = 4 }
    private const int SystemMemoryListInformation = 0x50;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

    // ---- privilege enabling (for the standby-list purge) ----

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, name, out LUID luid)) return;
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? host, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES tp, int len, IntPtr prev, IntPtr prevLen);
}
