namespace Talvora.Modules.Shell;

public sealed record ShellExecutionRequest(
    string Command,
    ShellKind Shell = ShellKind.PowerShell,
    bool LoadProfile = false,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    TimeSpan? Timeout = null);
