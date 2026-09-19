using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Talvora.Shared;

public sealed record WindowsSessionInfo(
    int SessionId,
    string StationName,
    string State,
    string UserName,
    string DomainName,
    string User,
    string ClientName,
    bool IsActive);
public sealed record InteractiveUserContext(
    int SessionId,
    string UserName,
    string DomainName,
    string User,
    string Sid,
    IReadOnlyDictionary<string, string> Environment)
{
    public string? UserProfile =>
        Environment.TryGetValue("USERPROFILE", out var value) ? value : null;

    public string? LocalAppData =>
        Environment.TryGetValue("LOCALAPPDATA", out var value) ? value : null;
}

public sealed record UserProcessLaunchResult(
    int SessionId,
    string User,
    int ProcessId,
    int ThreadId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string CommandLine);

public static partial class WindowsSessionLauncher
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateNoWindow = 0x08000000;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;
}
