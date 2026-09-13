using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ProcessTools(
    IProcessService processService,
    IOperationExecutor executor,
    IBrokerClient brokerClient)
{
    [McpServerTool, Description("Lists processes visible to the Windows account running Talvora.")]
    public async Task<ToolEnvelope<IReadOnlyList<ProcessSnapshot>>> ListProcesses(
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "process.list",
            token => processService.ListAsync(token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool, Description("Starts an executable with explicit argument boundaries. Can route through the elevated broker when requested.")]
    public async Task<ToolEnvelope<ProcessStartResult>> StartProcess(
        [Description("Executable path or executable name resolvable by Windows.")] string fileName,
        [Description("Arguments passed to the executable as separate values.")] string[]? arguments = null,
        [Description("Working directory. Optional.")] string? workingDirectory = null,
        [Description("Run through the elevated broker when true; otherwise run in the normal Talvora host context.")] bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        if (elevated)
        {
            var brokerResult = await brokerClient.ExecuteProcessAsync(
                new BrokerProcessExecutionRequest(
                    fileName,
                    Arguments: arguments,
                    WorkingDirectory: workingDirectory,
                    Mode: BrokerProcessExecutionMode.StartOnly),
                cancellationToken).ConfigureAwait(false);

            return ToolEnvelope.From(MapElevatedStartResult(brokerResult));
        }

        var request = new StartProcessRequest(fileName, arguments, workingDirectory);
        var result = await executor.ExecuteAsync(
            "process.start",
            token => processService.StartAsync(request, token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool, Description("Stops a process. By default the full child process tree is terminated as well.")]
    public async Task<ToolEnvelope<bool>> StopProcess(
        [Description("Windows process ID.")] int processId,
        [Description("Terminate child processes as well.")] bool entireProcessTree = true,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "process.stop",
            async token =>
            {
                await processService.StopAsync(processId, entireProcessTree, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    private static TalvoraResult<ProcessStartResult> MapElevatedStartResult(
        TalvoraResult<BrokerExecutionResult> result)
    {
        if (!result.IsSuccess)
        {
            return TalvoraResult.Failure<ProcessStartResult>(
                result.Error ?? new TalvoraError(
                    "operation_failed",
                    "Elevated Broker returned a failed process start without an error payload.",
                    "elevated.process.execute"));
        }

        if (result.Value is null)
        {
            return TalvoraResult.Failure<ProcessStartResult>(new TalvoraError(
                "operation_failed",
                "Elevated Broker returned a successful process start without an execution result.",
                "elevated.process.execute"));
        }

        return TalvoraResult.Success(new ProcessStartResult(result.Value.ProcessId));
    }
}
