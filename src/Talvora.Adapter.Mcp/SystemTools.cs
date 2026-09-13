using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class SystemTools(
    IPlatformInfoProvider platformInfo,
    IOperationExecutor executor)
{
    [McpServerTool, Description("Returns Talvora's current Windows runtime and privilege context.")]
    public async Task<ToolEnvelope<PlatformInfo>> GetSystemInfo(
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "system.info",
            token => platformInfo.GetAsync(token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }
}
