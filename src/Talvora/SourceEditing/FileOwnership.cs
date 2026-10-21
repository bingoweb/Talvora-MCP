using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Talvora.SourceEditing;

internal static partial class FileOwnership
{
    internal static SecurityIdentifier? TryGetOwner(string path)
    {
        try
        {
            FileSystemSecurity security = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Owner);
            return security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            PrivilegeNotHeldException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool TrySetOwner(string path, SecurityIdentifier owner)
    {
        try
        {
            if (TryGetOwner(path) is { } current && current == owner)
            {
                return true;
            }

            using var restore = RestorePrivilege.Enable();
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var security = directory.GetAccessControl(AccessControlSections.Owner);
                security.SetOwner(owner);
                directory.SetAccessControl(security);
            }
            else
            {
                var file = new FileInfo(path);
                var security = file.GetAccessControl(AccessControlSections.Owner);
                security.SetOwner(owner);
                file.SetAccessControl(security);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            PrivilegeNotHeldException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Enables SeRestorePrivilege on the process token for the scope, then restores it.</summary>
    private sealed partial class RestorePrivilege : IDisposable
    {
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x00000002;

        private readonly nint token;
        private readonly TokenPrivileges previous;
        private readonly bool changed;

        private RestorePrivilege(nint token, TokenPrivileges previous, bool changed)
        {
            this.token = token;
            this.previous = previous;
            this.changed = changed;
        }

        static RestorePrivilege()
        {
            if (Marshal.SizeOf<TokenPrivileges>() != 16)
            {
                throw new InvalidOperationException("TOKEN_PRIVILEGES layout mismatch.");
            }
        }

        internal static RestorePrivilege Enable()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            {
                return new RestorePrivilege(0, default, false);
            }

            var privileges = new TokenPrivileges { PrivilegeCount = 1, Attributes = SePrivilegeEnabled };
            if (!LookupPrivilegeValueW(null, "SeRestorePrivilege", out privileges.Luid) ||
                !AdjustTokenPrivileges(token, false, ref privileges,
                    (uint)Marshal.SizeOf<TokenPrivileges>(), out var previous, out _) ||
                Marshal.GetLastPInvokeError() != 0)
            {
                // Not held (normal user): owner changes to another account will fail and
                // are reported as OWNER_NOT_PRESERVED by the caller.
                return new RestorePrivilege(token, default, false);
            }

            return new RestorePrivilege(token, previous, true);
        }

        public void Dispose()
        {
            if (changed)
            {
                var restore = previous;
                AdjustTokenPrivileges(token, false, ref restore, 0, out _, out _);
            }

            if (token != 0)
            {
                CloseHandle(token);
            }
        }

        // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES: LUID is 4-byte aligned → Pack = 4 (16 bytes total).
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenProcessToken(nint process, uint access, out nint token);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LookupPrivilegeValueW(string? system, string name, out long luid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AdjustTokenPrivileges(
            nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
            ref TokenPrivileges newState, uint bufferLength,
            out TokenPrivileges previousState, out uint returnLength);

        [LibraryImport("kernel32.dll")]
        private static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);
    }
}
