using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Application;
using Talvora.Modules.Processes;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ProcessTools(
    IProcessService processService,
    IOperationExecutor executor,
    IExecutionRouter executionRouter)
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

    [McpServerTool, Description("Starts an executable with explicit argument boundaries. Uses automatic Windows privilege routing by default and can run as administrator when explicitly requested.")]
    public async Task<ToolEnvelope<ProcessStartResult>> StartProcess(
        [Description("Executable path or executable name resolvable by Windows.")] string fileName,
        [Description("Arguments passed to the executable as separate values.")] string[]? arguments = null,
        [Description("Working directory. Optional.")] string? workingDirectory = null,
        [Description("Run directly with administrator privileges when true. When false, Talvora starts normally and automatically uses administrator privileges only when Windows requires elevation.")] bool runAsAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        var request = new StartProcessRequest(fileName, arguments, workingDirectory);
        var result = await executionRouter.StartProcessAsync(
            request,
            runAsAdministrator ? ExecutionPrivilege.Elevated : ExecutionPrivilege.Auto,
            cancellationToken).ConfigureAwait(false);

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
