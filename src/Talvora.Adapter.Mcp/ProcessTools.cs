using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Modules.Processes;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ProcessTools(
    IProcessService processService,
    IOperationExecutor executor)
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

    [McpServerTool, Description("Starts an executable with explicit argument boundaries.")]
    public async Task<ToolEnvelope<ProcessStartResult>> StartProcess(
        [Description("Executable path or executable name resolvable by Windows.")] string fileName,
        [Description("Arguments passed to the executable as separate values.")] string[]? arguments = null,
        [Description("Working directory. Optional.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
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
}
