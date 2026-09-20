using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public sealed record TalvoraProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    long StandardOutputTotalCharacters,
    long StandardErrorTotalCharacters,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    bool OutputDrainTimedOut);

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "talvora_run_process", Destructive = true, OpenWorld = true), Description("Run any executable available to the Talvora LocalSystem service with explicit arguments. " + SourceEditRoutingContract.EscapeHatchRouting + " No command deny-list is applied.")]
    public static async Task<TalvoraProcessResult> RunProcess(
        string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        int timeoutSeconds = 300,
        int maxCapturedCharactersPerStream =
            ProcessRunner.DefaultMaximumCapturedCharacters,
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
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream:
                maxCapturedCharactersPerStream);

        return new TalvoraProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.StandardOutputTotalCharacters,
            result.StandardErrorTotalCharacters,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            result.OutputDrainTimedOut);
    }
}
