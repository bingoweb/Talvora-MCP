using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Ipc.Client;
using Talvora.Modules.Shell;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ShellTools(
    IShellService shellService,
    IOperationExecutor executor,
    IBrokerClient brokerClient)
{
    [McpServerTool, Description("Runs a PowerShell 7 or cmd.exe command without an artificial command deny-list. Can route through the elevated broker when requested.")]
    public async Task<ToolEnvelope<ShellExecutionResult>> RunShell(
        [Description("Command text to execute.")] string command,
        [Description("Shell to use: PowerShell or Cmd.")] ShellKind shell = ShellKind.PowerShell,
        [Description("Working directory. Uses the Talvora process directory when omitted.")] string? workingDirectory = null,
        [Description("Load the user's PowerShell profile when true.")] bool loadProfile = false,
        [Description("Run through the elevated broker when true; otherwise run in the normal Talvora host context.")] bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        if (elevated)
        {
            var brokerResult = await brokerClient.ExecuteShellAsync(
                new BrokerShellExecutionRequest(
                    command,
                    shell switch
                    {
                        ShellKind.PowerShell => BrokerShellKind.PowerShell,
                        ShellKind.Cmd => BrokerShellKind.Cmd,
                        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell."),
                    },
                    LoadProfile: loadProfile,
                    WorkingDirectory: workingDirectory),
                cancellationToken).ConfigureAwait(false);

            return ToolEnvelope.From(MapElevatedResult(brokerResult));
        }

        var request = new ShellExecutionRequest(
            command,
            shell,
            LoadProfile: loadProfile,
            WorkingDirectory: workingDirectory);

        var result = await executor.ExecuteAsync(
            "shell.execute",
            token => shellService.ExecuteAsync(request, token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    private static TalvoraResult<ShellExecutionResult> MapElevatedResult(
        TalvoraResult<BrokerExecutionResult> result)
    {
        if (!result.IsSuccess)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(
                result.Error ?? new TalvoraError(
                    "operation_failed",
                    "Elevated Broker returned a failed shell operation without an error payload.",
                    "elevated.shell.execute"));
        }

        if (result.Value is null)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(new TalvoraError(
                "operation_failed",
                "Elevated Broker returned a successful shell operation without an execution result.",
                "elevated.shell.execute"));
        }

        var value = result.Value;
        if (value.ExitCode is not { } exitCode)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(new TalvoraError(
                "operation_failed",
                "Elevated Broker returned a completed shell operation without an exit code.",
                "elevated.shell.execute"));
        }

        return TalvoraResult.Success(new ShellExecutionResult(
            value.ProcessId,
            exitCode,
            value.StandardOutput,
            value.StandardError,
            value.Duration));
    }
}
