using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Modules.Registry;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class RegistryTools(
    IRegistryService registry,
    IOperationExecutor executor)
{
    [McpServerTool, Description("Reads a Windows Registry value using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<RegistryValueData>> ReadRegistryValue(
        [Description("Registry hive containing the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Value name. Use null or empty for the unnamed (Default) value.")] string? valueName = null,
        [Description("Registry view to read: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        [Description("Expand environment variables in expandable string values when true; return the raw value when false.")] bool expandEnvironmentStrings = false,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.read",
            token => registry.ReadValueAsync(
                hive,
                subKeyPath,
                valueName,
                view,
                expandEnvironmentStrings,
                token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool, Description("Lists direct child Windows Registry subkey names using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<IReadOnlyList<string>>> ListRegistrySubKeys(
        [Description("Registry hive containing the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Registry view to read: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.list_subkeys",
            token => registry.ListSubKeyNamesAsync(
                hive,
                subKeyPath,
                view,
                token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }
}
