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

    [McpServerTool, Description("Lists Windows Registry value names for a key using the Windows account running Talvora. The unnamed default value is returned as an empty string.")]
    public async Task<ToolEnvelope<IReadOnlyList<string>>> ListRegistryValueNames(
        [Description("Registry hive containing the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Registry view to read: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.list_value_names",
            token => registry.ListValueNamesAsync(
                hive,
                subKeyPath,
                view,
                token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool(Destructive = true, Idempotent = true, OpenWorld = false, ReadOnly = false),
     Description("Creates or updates a Windows Registry value using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> WriteRegistryValue(
        [Description("Typed Registry value payload including hive, key path, value name, value type, and value data.")] RegistryValueData value,
        [Description("Registry view to write: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.write_value",
            async token =>
            {
                await registry.WriteValueAsync(value, view, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool(Destructive = true, Idempotent = true, OpenWorld = false, ReadOnly = false),
     Description("Deletes a Windows Registry value using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> DeleteRegistryValue(
        [Description("Registry hive containing the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Value name. Use null or empty for the unnamed (Default) value.")] string? valueName = null,
        [Description("Registry view to write: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.delete_value",
            async token =>
            {
                await registry.DeleteValueAsync(hive, subKeyPath, valueName, view, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool(Destructive = true, Idempotent = true, OpenWorld = false, ReadOnly = false),
     Description("Creates a Windows Registry key, including missing parent keys, using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> CreateRegistryKey(
        [Description("Registry hive in which to create the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Registry view to write: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.create_key",
            async token =>
            {
                await registry.CreateKeyAsync(hive, subKeyPath, view, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool(Destructive = true, Idempotent = true, OpenWorld = false, ReadOnly = false),
     Description("Deletes a Windows Registry key using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> DeleteRegistryKey(
        [Description("Registry hive containing the key.")] RegistryHiveId hive,
        [Description("Registry subkey path relative to the selected hive.")] string subKeyPath,
        [Description("Delete all descendant subkeys when true; require the target key to be empty when false.")] bool recursive = false,
        [Description("Registry view to write: Default, Registry32, or Registry64.")] RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "registry.delete_key",
            async token =>
            {
                await registry.DeleteKeyAsync(hive, subKeyPath, recursive, view, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }
}
