using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Talvora.Shared;

public static partial class WindowsSessionLauncher
{
private static InteractiveUserContext ReadUserContext(WindowsSessionInfo target)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            userToken = QueryUserToken(target.SessionId);
            environment = CreateUserEnvironment(userToken, target.SessionId);
            var values = ReadEnvironmentBlock(environment);

            using var identity = new WindowsIdentity(userToken);
            var sid = identity.User?.Value
                ?? throw new InvalidOperationException(
                    $"Unable to resolve user SID for session {target.SessionId}.");

            return new InteractiveUserContext(
                target.SessionId,
                target.UserName,
                target.DomainName,
                target.User,
                sid,
                values);
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyEnvironmentBlock(environment);
            }

            if (userToken != IntPtr.Zero)
            {
                _ = NativeMethods.CloseHandle(userToken);
            }
        }
    }

    private static IntPtr QueryUserToken(int sessionId)
    {
        if (!NativeMethods.WTSQueryUserToken(
                checked((uint)sessionId),
                out var token))
        {
            throw NewWin32Exception(
                $"WTSQueryUserToken failed for session {sessionId}");
        }

        return token;
    }

    private static IntPtr CreateUserEnvironment(IntPtr userToken, int sessionId)
    {
        if (!NativeMethods.CreateEnvironmentBlock(
                out var environment,
                userToken,
                inherit: false))
        {
            throw NewWin32Exception(
                $"CreateEnvironmentBlock failed for session {sessionId}");
        }

        return environment;
    }

    private static WindowsSessionInfo SelectDefaultSession(
        IReadOnlyList<WindowsSessionInfo> sessions)
    {
        var active = sessions
            .Where(session =>
                session.IsActive &&
                !string.IsNullOrWhiteSpace(session.UserName))
            .OrderBy(session =>
                string.Equals(
                    session.StationName,
                    "Console",
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(session => session.SessionId)
            .FirstOrDefault();

        if (active is not null)
        {
            return active;
        }

        return sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.UserName))
            .OrderBy(session => session.SessionId)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No logged-on Windows user session is available.");
    }

    private static WindowsSessionInfo[] EnumerateSessions()
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            if (!NativeMethods.WTSEnumerateSessionsW(
                    IntPtr.Zero,
                    reserved: 0,
                    version: 1,
                    out buffer,
                    out var count))
            {
                throw NewWin32Exception("WTSEnumerateSessionsW failed");
            }

            var result = new List<WindowsSessionInfo>(checked((int)count));
            var size = Marshal.SizeOf<NativeMethods.WTS_SESSION_INFOW>();

            for (var index = 0; index < count; index++)
            {
                var pointer = IntPtr.Add(
                    buffer,
                    checked((int)index * size));
                var native = Marshal.PtrToStructure<
                    NativeMethods.WTS_SESSION_INFOW>(pointer);

                var station = Marshal.PtrToStringUni(native.pWinStationName)
                    ?? string.Empty;
                var userName = QuerySessionString(
                    native.SessionId,
                    NativeMethods.WTS_INFO_CLASS.WTSUserName);
                var domainName = QuerySessionString(
                    native.SessionId,
                    NativeMethods.WTS_INFO_CLASS.WTSDomainName);

                var clientName = QuerySessionString(
                    native.SessionId,
                    NativeMethods.WTS_INFO_CLASS.WTSClientName);

                var user = string.IsNullOrWhiteSpace(userName)
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(domainName)
                        ? userName
                        : $"{domainName}\\{userName}";

                result.Add(new WindowsSessionInfo(
                    checked((int)native.SessionId),
                    station,
                    native.State.ToString(),
                    userName,
                    domainName,
                    user,
                    clientName,
                    native.State ==
                        NativeMethods.WTS_CONNECTSTATE_CLASS.WTSActive));
            }

            return result.ToArray();
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NativeMethods.WTSFreeMemory(buffer);
            }
        }
    }

    private static string QuerySessionString(
        uint sessionId,
        NativeMethods.WTS_INFO_CLASS infoClass)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!NativeMethods.WTSQuerySessionInformationW(
                    IntPtr.Zero,
                    sessionId,
                    infoClass,
                    out buffer,
                    out var bytesReturned))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 2 or 1168)
                {
                    return string.Empty;
                }

                throw new Win32Exception(
                    error,
                    $"WTSQuerySessionInformationW({infoClass}) failed for session {sessionId}");
            }

            if (buffer == IntPtr.Zero || bytesReturned == 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0')
                ?? string.Empty;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NativeMethods.WTSFreeMemory(buffer);
            }
        }
    }
}
