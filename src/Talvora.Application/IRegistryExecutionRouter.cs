using Talvora.Abstractions;
using Talvora.Modules.Registry;

namespace Talvora.Application;

public interface IRegistryExecutionRouter
{
    ValueTask<TalvoraResult<bool>> WriteValueAsync(
        RegistryValueData value,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);

    ValueTask<TalvoraResult<bool>> DeleteValueAsync(
        RegistryHiveId hive,
        string subKeyPath,
        string? valueName,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);

    ValueTask<TalvoraResult<bool>> CreateKeyAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);

    ValueTask<TalvoraResult<bool>> DeleteKeyAsync(
        RegistryHiveId hive,
        string subKeyPath,
        bool recursive,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);
}
