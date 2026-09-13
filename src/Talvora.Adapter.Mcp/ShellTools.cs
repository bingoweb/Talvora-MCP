using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Application;
using Talvora.Modules.Shell;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ShellTools(IExecutionRouter executionRouter)
{
    [McpServerTool, Description("Runs a PowerShell 7 or cmd.exe command without an artificial command deny-list. Can run with administrator privileges when requested.")]
    public async Task<ToolEnvelope<ShellExecutionResult>> RunShell(
        [Description("Command text to execute.")] string command,
        [Description("Shell to use: PowerShell or Cmd.")] ShellKind shell = ShellKind.PowerShell,
        [Description("Working directory. Uses the Talvora process directory when omitted.")] string? workingDirectory = null,
        [Description("Load the user's PowerShell profile when true.")] bool loadProfile = false,
        [Description("Run with administrator privileges when true; otherwise use the normal Talvora host context.")] bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ShellExecutionRequest(
            command,
            shell,
            LoadProfile: loadProfile,
            WorkingDirectory: workingDirectory);

        var result = await executionRouter.ExecuteShellAsync(
            request,
            elevated ? ExecutionPrivilege.Elevated : ExecutionPrivilege.Normal,
            cancellationToken).ConfigureAwait(false);

        return ToolEnvelope.From(result);
    }
}
