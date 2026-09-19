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

    public static UserProcessLaunchResult StartProcess(
        string executable,
        IReadOnlyList<string>? arguments = null,
        int? sessionId = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool visible = true,
        bool newConsole = false)
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
                    out var processInformation))
            {
                throw NewWin32Exception(
                    $"CreateProcessAsUserW failed for session {target.SessionId}");
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
