using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Talvora.Shared;

public static partial class WindowsSessionLauncher
{
public static IReadOnlyList<WindowsSessionInfo> ListSessions()
    {
        EnsureWindows();
        return EnumerateSessions()
            .OrderByDescending(session => session.IsActive)
            .ThenBy(session => session.SessionId)
            .ToArray();
    }

    public static InteractiveUserContext GetDefaultInteractiveUser()
    {
        EnsureWindows();
        var target = SelectDefaultSession(EnumerateSessions());
        return ReadUserContext(target);
    }

    public static InteractiveUserContext GetActiveInteractiveUser()
    {
        EnsureWindows();
        var active = EnumerateSessions()
            .Where(item => item.IsActive &&
                !string.IsNullOrWhiteSpace(item.UserName))
            .ToArray();
        if (active.Length == 0)
        {
            throw new InvalidOperationException(
                "No active interactive Windows user session is available.");
        }
        return ReadUserContext(SelectDefaultSession(active));
    }

    public static InteractiveUserContext GetActiveUserForSession(int sessionId)
    {
        EnsureWindows();
        var session = EnumerateSessions().FirstOrDefault(item =>
            item.SessionId == sessionId && item.IsActive &&
            !string.IsNullOrWhiteSpace(item.UserName));
        return session is null
            ? throw new InvalidOperationException(
                $"Windows user session {sessionId} is not active.")
            : ReadUserContext(session);
    }

    public static UserProcessLaunchResult StartProcess(
        string executable,
        IReadOnlyList<string>? arguments = null,
        int? sessionId = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool visible = true,
        bool newConsole = false,
        string? expectedUserSid = null)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var sessions = EnumerateSessions();
        var target = sessionId is int requested
            ? sessions.FirstOrDefault(item => item.SessionId == requested)
                ?? throw new InvalidOperationException(
                    $"Windows session was not found: {requested}")
            : SelectDefaultSession(sessions);

        IntPtr userToken = IntPtr.Zero;
        IntPtr baseEnvironment = IntPtr.Zero;
        IntPtr customEnvironment = IntPtr.Zero;

        try
        {
            userToken = QueryUserToken(target.SessionId);
            if (!string.IsNullOrWhiteSpace(expectedUserSid))
            {
                using var identity = new WindowsIdentity(userToken);
                if (!string.Equals(identity.User?.Value, expectedUserSid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "Interactive Windows user identity changed during launch.");
                }
            }
            baseEnvironment = CreateUserEnvironment(userToken, target.SessionId);
            var environmentValues = ReadEnvironmentBlock(baseEnvironment);
            ApplyEnvironmentOverrides(environmentValues, environment);

            var environmentPointer = baseEnvironment;
            if (environment is { Count: > 0 })
            {
                customEnvironment = CreateEnvironmentBlockPointer(environmentValues);
                environmentPointer = customEnvironment;
            }

            var cwd = ResolveWorkingDirectory(workingDirectory, environmentValues);
            var resolvedExecutable = ResolveExecutable(
                executable,
                cwd,
                environmentValues);

            var argumentList = arguments?.ToArray() ?? [];
            var commandLine = BuildCommandLine(
                resolvedExecutable.DisplayName,
                argumentList);

            var startupInfo = new NativeMethods.STARTUPINFO
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };

            uint creationFlags = CreateUnicodeEnvironment;
            if (newConsole)
            {
                creationFlags |= CreateNewConsole;
            }
            else if (!visible)
            {
                creationFlags |= CreateNoWindow;
                startupInfo.dwFlags |= StartfUseShowWindow;
                startupInfo.wShowWindow = SwHide;
            }

            var mutableCommandLine = new StringBuilder(commandLine);
            NativeMethods.PROCESS_INFORMATION processInformation;
            if (!NativeMethods.CreateProcessAsUserW(
                    userToken,
                    resolvedExecutable.ApplicationName,
                    mutableCommandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    creationFlags,
                    environmentPointer,
                    cwd,
                    ref startupInfo,
                    out processInformation))
            {
                var createAsUserError = Marshal.GetLastWin32Error();
                if (createAsUserError is 5 or 1314)
                {
                    mutableCommandLine = new StringBuilder(commandLine);
                    if (!NativeMethods.CreateProcessWithTokenW(
                            userToken,
                            logonFlags: 0,
                            resolvedExecutable.ApplicationName,
                            mutableCommandLine,
                            creationFlags,
                            environmentPointer,
                            cwd,
                            ref startupInfo,
                            out processInformation))
                    {
                        var createWithTokenError = Marshal.GetLastWin32Error();
                        throw new Win32Exception(
                            createWithTokenError,
                            $"Interactive process launch failed for session {target.SessionId}. " +
                            $"CreateProcessAsUserW={createAsUserError}; " +
                            $"CreateProcessWithTokenW={createWithTokenError}");
                    }
                }
                else
                {
                    throw new Win32Exception(
                        createAsUserError,
                        $"CreateProcessAsUserW failed for session {target.SessionId}. " +
                        $"Win32Error={createAsUserError}");
                }
            }

            try
            {
                return new UserProcessLaunchResult(
                    target.SessionId,
                    target.User,
                    checked((int)processInformation.dwProcessId),
                    checked((int)processInformation.dwThreadId),
                    resolvedExecutable.DisplayName,
                    argumentList,
                    cwd,
                    commandLine);
            }
            finally
            {
                if (processInformation.hThread != IntPtr.Zero)
                {
                    _ = NativeMethods.CloseHandle(processInformation.hThread);
                }

                if (processInformation.hProcess != IntPtr.Zero)
                {
                    _ = NativeMethods.CloseHandle(processInformation.hProcess);
                }
            }
        }
        finally
        {
            if (customEnvironment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(customEnvironment);
            }

            if (baseEnvironment != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyEnvironmentBlock(baseEnvironment);
            }

            if (userToken != IntPtr.Zero)
            {
                _ = NativeMethods.CloseHandle(userToken);
            }
        }
    }
}
