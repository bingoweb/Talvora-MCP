namespace Talvora.Modules.Registry;

public interface IRegistryService
{
    ValueTask<RegistryValueData> ReadValueAsync(
        RegistryHiveId hive,
        string subKeyPath,
        string? valueName = null,
        RegistryViewId view = RegistryViewId.Default,
        bool expandEnvironmentStrings = false,
        CancellationToken cancellationToken = default);

    ValueTask WriteValueAsync(
        RegistryValueData value,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> ListValueNamesAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default);
}
