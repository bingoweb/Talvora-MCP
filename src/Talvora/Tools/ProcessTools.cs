using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, int ProcessId);

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "talvora_run_process", Destructive = true, OpenWorld = true), Description("Run any executable available to the Talvora LocalSystem service with explicit arguments. No command deny-list is applied.")]
    public static async Task<TalvoraProcessResult> RunProcess(
        string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var result = await ProcessRunner.RunAsync(
            executable,
            workingDirectory,
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);

        return new TalvoraProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId);
    }
}
