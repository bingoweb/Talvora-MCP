namespace Talvora.Abstractions;

public sealed record PlatformInfo(
    string OperatingSystem,
    string Framework,
    string ProcessArchitecture,
    string OsArchitecture,
    string MachineName,
    string UserName,
    bool IsElevated,
    TimeSpan Uptime);
