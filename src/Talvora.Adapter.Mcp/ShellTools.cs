using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Application;
using Talvora.Modules.Shell;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class ShellTools(IExecutionRouter executionRouter)
{
    [McpServerTool, Description("Runs a PowerShell 7 or cmd.exe command without an artificial command deny-list. Uses automatic Windows privilege routing by default and can run as administrator when explicitly requested.")]
    public async Task<ToolEnvelope<ShellExecutionResult>> RunShell(
        [Description("Command text to execute.")] string command,
        [Description("Shell to use: PowerShell or Cmd.")] ShellKind shell = ShellKind.PowerShell,
        [Description("Working directory. Uses the Talvora process directory when omitted.")] string? workingDirectory = null,
        [Description("Load the user's PowerShell profile when true.")] bool loadProfile = false,
        [Description("Run directly with administrator privileges when true. When false, Talvora starts normally and automatically uses administrator privileges only when Windows requires elevation.")] bool runAsAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ShellExecutionRequest(
            command,
            shell,
            LoadProfile: loadProfile,
            WorkingDirectory: workingDirectory);

        var result = await executionRouter.ExecuteShellAsync(
            request,
            runAsAdministrator ? ExecutionPrivilege.Elevated : ExecutionPrivilege.Auto,
            cancellationToken).ConfigureAwait(false);

        return ToolEnvelope.From(result);
    }
}
