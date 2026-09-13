namespace Talvora.Ipc.Client;

public enum BrokerShellKind
{
    PowerShell,
    Cmd,
}

public enum BrokerProcessExecutionMode
{
    WaitForExit,
    StartOnly,
}

public sealed record BrokerShellExecutionRequest(
    string Command,
    BrokerShellKind Shell = BrokerShellKind.PowerShell,
    bool LoadProfile = false,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    TimeSpan? Timeout = null,
    string? OperationId = null);

public sealed record BrokerProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool CreateNoWindow = false,
    TimeSpan? Timeout = null,
    string? OperationId = null,
    BrokerProcessExecutionMode Mode = BrokerProcessExecutionMode.WaitForExit);

public sealed record BrokerExecutionResult(
    string OperationId,
    int ProcessId,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
