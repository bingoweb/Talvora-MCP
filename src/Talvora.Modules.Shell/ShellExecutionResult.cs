namespace Talvora.Modules.Shell;

public sealed record ShellExecutionResult(
    int ProcessId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
