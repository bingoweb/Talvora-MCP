using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Modules.Shell;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ShellTools(
    IShellService shellService,
    IOperationExecutor executor)
{
    [McpServerTool, Description("Runs a PowerShell 7 or cmd.exe command without an artificial command deny-list.")]
    public async Task<ToolEnvelope<ShellExecutionResult>> RunShell(
        [Description("Command text to execute.")] string command,
        [Description("Shell to use: PowerShell or Cmd.")] ShellKind shell = ShellKind.PowerShell,
        [Description("Working directory. Uses the Talvora process directory when omitted.")] string? workingDirectory = null,
        [Description("Load the user's PowerShell profile when true.")] bool loadProfile = false,
        CancellationToken cancellationToken = default)
    {
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
}
