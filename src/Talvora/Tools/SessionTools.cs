using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraSessionInfo(
    int SessionId,
    string StationName,
    string State,
    string UserName,
    string DomainName,
    string User,
    string ClientName,
    bool IsActive);

public sealed record TalvoraSessionListResponse(
    int Count,
    IReadOnlyList<TalvoraSessionInfo> Sessions);

public sealed record TalvoraSessionGetResponse(
    bool Found,
    TalvoraSessionInfo? Session);

public sealed record TalvoraUserProcessStartResponse(
    int SessionId,
    string User,
    int ProcessId,
    int ThreadId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string CommandLine,
    bool Visible,
    bool NewConsole);

[McpServerToolType]
public static class SessionTools
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateNoWindow = 0x08000000;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;

    [McpServerTool(
        Name = "talvora_session_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSessionListResponse)),
     Description("Enumerate local Windows interactive/RDP sessions with session ID, station, state, user/domain, and client name. No user/session allowlist is applied.")]
    public static TalvoraSessionListResponse List()
    {
        EnsureWindows();

        var sessions = EnumerateSessions()
            .OrderByDescending(session => session.IsActive)
            .ThenBy(session => session.SessionId)
            .ToArray();

        return new TalvoraSessionListResponse(sessions.Length, sessions);
    }

    [McpServerTool(
        Name = "talvora_session_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSessionGetResponse)),
     Description("Return one local Windows session by session ID. Missing session IDs return found=false.")]
    public static TalvoraSessionGetResponse Get(int sessionId)
    {
        EnsureWindows();

        var session = EnumerateSessions()
            .FirstOrDefault(value => value.SessionId == sessionId);

        return new TalvoraSessionGetResponse(
            session is not null,
            session);
    }

    [McpServerTool(
        Name = "talvora_user_process_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraUserProcessStartResponse)),
     Description("Start any executable inside a logged-on Windows user's interactive session using the session's primary token. If sessionId is omitted, Talvora selects an active session with a logged-on user. Supports arbitrary argument vectors, working directory, and environment overrides. No executable, path, user, or session allowlist is applied.")]
    public static TalvoraUserProcessStartResponse StartUserProcess(
        string executable,
        string[]? arguments = null,
        int? sessionId = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool visible = true,
        bool newConsole = false)
    {
        EnsureWindows();

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException("Executable is required.", nameof(executable));
        }

        var sessions = EnumerateSessions();
        var target = sessionId is int requestedSessionId
            ? sessions.FirstOrDefault(value => value.SessionId == requestedSessionId)
                ?? throw new InvalidOperationException(
                    $"Windows session was not found: {requestedSessionId}")
            : SelectDefaultSession(sessions);

        if (string.IsNullOrWhiteSpace(target.UserName))
        {
            throw new InvalidOperationException(
                $"Windows session {target.SessionId} does not have a logged-on user.");
        }

        IntPtr userToken = IntPtr.Zero;
        IntPtr baseEnvironment = IntPtr.Zero;
        IntPtr customEnvironment = IntPtr.Zero;

        try
        {
            if (!NativeMethods.WTSQueryUserToken(
                    checked((uint)target.SessionId),
                    out userToken))
            {
                throw NewWin32Exception(
                    $"WTSQueryUserToken failed for session {target.SessionId}");
            }

            if (!NativeMethods.CreateEnvironmentBlock(
                    out baseEnvironment,
                    userToken,
                    inherit: false))
            {
                throw NewWin32Exception(
                    $"CreateEnvironmentBlock failed for session {target.SessionId}");
            }

            var environmentValues = ReadEnvironmentBlock(baseEnvironment);
            ApplyEnvironmentOverrides(environmentValues, environment);

            var environmentPointer = baseEnvironment;
            if (environment is { Count: > 0 })
            {
                customEnvironment = CreateEnvironmentBlockPointer(environmentValues);
                environmentPointer = customEnvironment;
            }

            var cwd = ResolveWorkingDirectory(
                workingDirectory,
                environmentValues);

            var resolvedExecutable = ResolveExecutable(
                executable,
                cwd,
                environmentValues);

            var argumentList = arguments ?? [];
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
                return new TalvoraUserProcessStartResponse(
                    target.SessionId,
                    target.User,
                    checked((int)processInformation.dwProcessId),
                    checked((int)processInformation.dwThreadId),
                    resolvedExecutable.DisplayName,
                    argumentList,
                    cwd,
                    commandLine,
                    visible,
                    newConsole);
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

    private static TalvoraSessionInfo SelectDefaultSession(
        IReadOnlyList<TalvoraSessionInfo> sessions)
    {
        var active = sessions
            .Where(session =>
                session.IsActive &&
                !string.IsNullOrWhiteSpace(session.UserName))
            .OrderBy(session =>
                string.Equals(
                    session.StationName,
                    "Console",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(session => session.SessionId)
            .FirstOrDefault();

        if (active is not null)
        {
            return active;
        }

        var loggedOn = sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.UserName))
            .OrderBy(session => session.SessionId)
            .FirstOrDefault();

        return loggedOn
            ?? throw new InvalidOperationException(
                "No logged-on Windows user session is available.");
    }

    private static TalvoraSessionInfo[] EnumerateSessions()
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

            var result = new List<TalvoraSessionInfo>(
                checked((int)count));

            var structureSize =
                Marshal.SizeOf<NativeMethods.WTS_SESSION_INFOW>();

            for (var index = 0; index < count; index++)
            {
                var pointer = IntPtr.Add(
                    buffer,
                    checked((int)index * structureSize));

                var native = Marshal.PtrToStructure<
                    NativeMethods.WTS_SESSION_INFOW>(pointer);

                var stationName =
                    Marshal.PtrToStringUni(native.pWinStationName)
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

                result.Add(new TalvoraSessionInfo(
                    checked((int)native.SessionId),
                    stationName,
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

                // Some session classes legitimately have no value.
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

    private static Dictionary<string, string> ReadEnvironmentBlock(
        IntPtr environment)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var cursor = environment;

        while (cursor != IntPtr.Zero)
        {
            var entry = Marshal.PtrToStringUni(cursor);

            if (string.IsNullOrEmpty(entry))
            {
                break;
            }

            var separator = entry[0] == '='
                ? entry.IndexOf('=', 1)
                : entry.IndexOf('=');

            if (separator > 0)
            {
                result[entry[..separator]] =
                    entry[(separator + 1)..];
            }

            cursor = IntPtr.Add(
                cursor,
                checked((entry.Length + 1) * sizeof(char)));
        }

        return result;
    }

    private static void ApplyEnvironmentOverrides(
        Dictionary<string, string> values,
        Dictionary<string, string?>? overrides)
    {
        foreach (var pair in overrides
                     ?? new Dictionary<string, string?>())
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                throw new ArgumentException(
                    "Environment variable names cannot be empty.",
                    nameof(overrides));
            }

            if (pair.Value is null)
            {
                values.Remove(pair.Key);
            }
            else
            {
                values[pair.Key] = pair.Value;
            }
        }
    }

    private static IntPtr CreateEnvironmentBlockPointer(
        IReadOnlyDictionary<string, string> values)
    {
        var entries = values
            .OrderBy(
                pair => pair.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}");

        var block = string.Join('\0', entries) + "\0";

        // StringToHGlobalUni adds its own terminating null. Because block
        // already ends with one, the resulting environment block is
        // correctly double-null terminated.
        return Marshal.StringToHGlobalUni(block);
    }

    private static string ResolveWorkingDirectory(
        string? requested,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);

            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException(
                    $"Working directory was not found: {full}");
            }

            return full;
        }

        if (environment.TryGetValue(
                "USERPROFILE",
                out var userProfile) &&
            !string.IsNullOrWhiteSpace(userProfile) &&
            Directory.Exists(userProfile))
        {
            return Path.GetFullPath(userProfile);
        }

        return Environment.CurrentDirectory;
    }

    private static (
        string DisplayName,
        string? ApplicationName)
        ResolveExecutable(
            string executable,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment)
    {
        var containsSeparator =
            executable.Contains(Path.DirectorySeparatorChar) ||
            executable.Contains(Path.AltDirectorySeparatorChar);

        if (Path.IsPathFullyQualified(executable) ||
            containsSeparator)
        {
            var candidate = Path.IsPathFullyQualified(executable)
                ? Path.GetFullPath(executable)
                : Path.GetFullPath(
                    Path.Combine(workingDirectory, executable));

            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    "Executable was not found.",
                    candidate);
            }

            return (candidate, candidate);
        }

        var extensions = GetExecutableExtensions(environment);

        foreach (var directory in EnumerateExecutableSearchDirectories(
                     workingDirectory,
                     environment))
        {
            foreach (var candidate in ExpandCommandCandidate(
                         Path.Combine(directory, executable),
                         extensions))
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        var full = Path.GetFullPath(candidate);
                        return (full, full);
                    }
                }
                catch
                {
                }
            }
        }

        // Let CreateProcessAsUser apply native Windows executable search
        // semantics when a command cannot be resolved from the user PATH.
        return (executable, null);
    }

    private static IEnumerable<string>
        EnumerateExecutableSearchDirectories(
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment)
    {
        yield return workingDirectory;

        if (environment.TryGetValue("PATH", out var path))
        {
            foreach (var directory in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                yield return directory;
            }
        }
    }

    private static string[] GetExecutableExtensions(
        IReadOnlyDictionary<string, string> environment)
    {
        if (Path.DirectorySeparatorChar != '\\')
        {
            return [string.Empty];
        }

        var raw = environment.TryGetValue(
            "PATHEXT",
            out var pathExt)
            ? pathExt
            : ".COM;.EXE;.BAT;.CMD";

        return raw.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(extension =>
                extension.StartsWith('.')
                    ? extension
                    : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExpandCommandCandidate(
        string basePath,
        IReadOnlyList<string> extensions)
    {
        if (Path.HasExtension(basePath))
        {
            yield return basePath;
            yield break;
        }

        yield return basePath;

        foreach (var extension in extensions)
        {
            if (extension.Length > 0)
            {
                yield return basePath + extension;
            }
        }
    }

    private static string BuildCommandLine(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteWindowsArgument(executable));

        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteWindowsArgument(argument));
        }

        return builder.ToString();
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 &&
            !argument.Any(character =>
                char.IsWhiteSpace(character) ||
                character == '"'))
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');

        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(character);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static Win32Exception NewWin32Exception(
        string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(
            error,
            $"{message}. Win32Error={error}");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows session tools require Windows.");
        }
    }

    private static class NativeMethods
    {
        internal enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit,
        }

        internal enum WTS_INFO_CLASS
        {
            WTSInitialProgram,
            WTSApplicationName,
            WTSWorkingDirectory,
            WTSOEMId,
            WTSSessionId,
            WTSUserName,
            WTSWinStationName,
            WTSDomainName,
            WTSConnectState,
            WTSClientBuildNumber,
            WTSClientName,
            WTSClientDirectory,
            WTSClientProductId,
            WTSClientHardwareId,
            WTSClientAddress,
            WTSClientDisplay,
            WTSClientProtocolType,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WTS_SESSION_INFOW
        {
            public uint SessionId;
            public IntPtr pWinStationName;
            public WTS_CONNECTSTATE_CLASS State;
        }

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        internal struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport(
            "Wtsapi32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSEnumerateSessionsW(
            IntPtr hServer,
            int reserved,
            int version,
            out IntPtr ppSessionInfo,
            out uint pCount);

        [DllImport(
            "Wtsapi32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformationW(
            IntPtr hServer,
            uint sessionId,
            WTS_INFO_CLASS infoClass,
            out IntPtr ppBuffer,
            out uint pBytesReturned);

        [DllImport(
            "Wtsapi32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQueryUserToken(
            uint sessionId,
            out IntPtr phToken);

        [DllImport("Wtsapi32.dll")]
        internal static extern void WTSFreeMemory(
            IntPtr pMemory);

        [DllImport(
            "userenv.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateEnvironmentBlock(
            out IntPtr lpEnvironment,
            IntPtr hToken,
            [MarshalAs(UnmanagedType.Bool)] bool inherit);

        [DllImport(
            "userenv.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyEnvironmentBlock(
            IntPtr lpEnvironment);

        [DllImport(
            "advapi32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessAsUserW(
            IntPtr hToken,
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(
            IntPtr hObject);
    }
}
