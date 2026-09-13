using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Runs code as this same user without the Administrators group and without privileges: what an unelevated program of
/// this user gets. A test that measures what such a program can do must not run as an administrator, and CI does.
/// </summary>
static class ReducedToken
{
    private const int TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const int TOKEN_DUPLICATE = 0x0002;
    private const int TOKEN_IMPERSONATE = 0x0004;
    private const int TOKEN_QUERY = 0x0008;
    private const int DISABLE_MAX_PRIVILEGE = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public int Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existing, int flags,
        int disableSidCount, SidAndAttributes[] sidsToDisable,
        int deletePrivilegeCount, IntPtr privilegesToDelete,
        int restrictedSidCount, SidAndAttributes[]? sidsToRestrict,
        out SafeAccessTokenHandle newToken);

    public static T Run<T>(Func<T> action)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE | TOKEN_QUERY, out var own))
            throw new Win32Exception();
        using (own)
        {
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var sid = new byte[administrators.BinaryLength];
            administrators.GetBinaryForm(sid, 0);
            var pinned = GCHandle.Alloc(sid, GCHandleType.Pinned);
            try
            {
                // Disabled here means deny-only: the group can still deny this token access, never grant it.
                var disable = new[] { new SidAndAttributes { Sid = pinned.AddrOfPinnedObject() } };
                if (!CreateRestrictedToken(own, DISABLE_MAX_PRIVILEGE, disable.Length, disable, 0, IntPtr.Zero, 0, null, out var reduced))
                    throw new Win32Exception();
                using (reduced)
                    return WindowsIdentity.RunImpersonated(reduced, action);
            }
            finally
            {
                pinned.Free();
            }
        }
    }
}
