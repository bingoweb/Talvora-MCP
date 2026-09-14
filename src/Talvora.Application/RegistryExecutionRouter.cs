using System.ComponentModel;
using Talvora.Abstractions;
using Talvora.Ipc.Client;
using Talvora.Modules.Registry;

namespace Talvora.Application;

public sealed class RegistryExecutionRouter(
    IRegistryService registryService,
    IOperationExecutor executor,
    IBrokerClient brokerClient) : IRegistryExecutionRouter
{
    public ValueTask<TalvoraResult<bool>> WriteValueAsync(
        RegistryValueData value,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        var request = new BrokerRegistryMutationRequest(
            BrokerRegistryMutationKind.WriteValue,
            MapHive(value.Hive),
            value.SubKeyPath,
            value.ValueName,
            MapView(view),
            ValueType: MapValueType(value.Type),
            StringValue: value.StringValue,
            DWordValue: value.DWordValue,
            QWordValue: value.QWordValue,
            MultiStringValue: value.MultiStringValue,
            BinaryValue: value.BinaryValue);

        return ExecuteAsync(
            "registry.write_value",
            privilege,
            token => registryService.WriteValueAsync(value, view, token),
            request,
            cancellationToken);
    }

    public ValueTask<TalvoraResult<bool>> DeleteValueAsync(
        RegistryHiveId hive,
        string subKeyPath,
        string? valueName,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        var request = new BrokerRegistryMutationRequest(
            BrokerRegistryMutationKind.DeleteValue,
            MapHive(hive),
            subKeyPath,
            valueName,
            MapView(view));

        return ExecuteAsync(
            "registry.delete_value",
            privilege,
            token => registryService.DeleteValueAsync(hive, subKeyPath, valueName, view, token),
            request,
            cancellationToken);
    }

    public ValueTask<TalvoraResult<bool>> CreateKeyAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        var request = new BrokerRegistryMutationRequest(
            BrokerRegistryMutationKind.CreateKey,
            MapHive(hive),
            subKeyPath,
            View: MapView(view));

        return ExecuteAsync(
            "registry.create_key",
            privilege,
            token => registryService.CreateKeyAsync(hive, subKeyPath, view, token),
            request,
            cancellationToken);
    }

    public ValueTask<TalvoraResult<bool>> DeleteKeyAsync(
        RegistryHiveId hive,
        string subKeyPath,
        bool recursive,
        RegistryViewId view,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        var request = new BrokerRegistryMutationRequest(
            BrokerRegistryMutationKind.DeleteKey,
            MapHive(hive),
            subKeyPath,
            View: MapView(view),
            Recursive: recursive);

        return ExecuteAsync(
            "registry.delete_key",
            privilege,
            token => registryService.DeleteKeyAsync(hive, subKeyPath, recursive, view, token),
            request,
            cancellationToken);
    }

    private async ValueTask<TalvoraResult<bool>> ExecuteAsync(
        string operation,
        ExecutionPrivilege privilege,
        Func<CancellationToken, ValueTask> localAction,
        BrokerRegistryMutationRequest brokerRequest,
        CancellationToken cancellationToken)
    {
        return privilege switch
        {
            ExecutionPrivilege.Auto =>
                await ExecuteAutoAsync(operation, localAction, brokerRequest, cancellationToken).ConfigureAwait(false),
            ExecutionPrivilege.Normal =>
                await ExecuteLocalAsync(operation, localAction, cancellationToken).ConfigureAwait(false),
            ExecutionPrivilege.Elevated =>
                await ExecuteElevatedAsync(operation, brokerRequest, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidEnumArgumentException(nameof(privilege), (int)privilege, typeof(ExecutionPrivilege)),
        };
    }

    private async ValueTask<TalvoraResult<bool>> ExecuteAutoAsync(
        string operation,
        Func<CancellationToken, ValueTask> localAction,
        BrokerRegistryMutationRequest brokerRequest,
        CancellationToken cancellationToken)
    {
        var localResult = await ExecuteLocalAsync(operation, localAction, cancellationToken).ConfigureAwait(false);
        if (localResult.IsSuccess || !RequiresElevation(localResult.Error))
        {
            return localResult;
        }

        return await ExecuteElevatedAsync(operation, brokerRequest, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<TalvoraResult<bool>> ExecuteLocalAsync(
        string operation,
        Func<CancellationToken, ValueTask> localAction,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            operation,
            async token =>
            {
                await localAction(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private async ValueTask<TalvoraResult<bool>> ExecuteElevatedAsync(
        string operation,
        BrokerRegistryMutationRequest request,
        CancellationToken cancellationToken)
    {
        var brokerResult = await brokerClient.ExecuteRegistryAsync(request, cancellationToken).ConfigureAwait(false);
        if (!brokerResult.IsSuccess)
        {
            return TalvoraResult.Failure<bool>(
                brokerResult.Error ?? MissingBrokerError(operation));
        }

        if (brokerResult.Value is not { Completed: true })
        {
            return TalvoraResult.Failure<bool>(MissingBrokerError(operation));
        }

        return TalvoraResult.Success(true);
    }

    private static bool RequiresElevation(TalvoraError? error) =>
        error is not null &&
        (string.Equals(error.Code, "access_denied", StringComparison.Ordinal) ||
         error.NativeCode is 5 or 740);

    private static TalvoraError MissingBrokerError(string operation) =>
        new(
            "operation_failed",
            "Elevated Broker returned a successful Registry mutation without a completed result.",
            operation);

    private static BrokerRegistryHive MapHive(RegistryHiveId hive) => hive switch
    {
        RegistryHiveId.ClassesRoot => BrokerRegistryHive.ClassesRoot,
        RegistryHiveId.CurrentUser => BrokerRegistryHive.CurrentUser,
        RegistryHiveId.LocalMachine => BrokerRegistryHive.LocalMachine,
        RegistryHiveId.Users => BrokerRegistryHive.Users,
        RegistryHiveId.CurrentConfig => BrokerRegistryHive.CurrentConfig,
        _ => throw new InvalidEnumArgumentException(nameof(hive), (int)hive, typeof(RegistryHiveId)),
    };

    private static BrokerRegistryView MapView(RegistryViewId view) => view switch
    {
        RegistryViewId.Default => BrokerRegistryView.Default,
        RegistryViewId.Registry32 => BrokerRegistryView.Registry32,
        RegistryViewId.Registry64 => BrokerRegistryView.Registry64,
        _ => throw new InvalidEnumArgumentException(nameof(view), (int)view, typeof(RegistryViewId)),
    };

    private static BrokerRegistryValueType MapValueType(RegistryValueType type) => type switch
    {
        RegistryValueType.Unknown => BrokerRegistryValueType.Unknown,
        RegistryValueType.None => BrokerRegistryValueType.None,
        RegistryValueType.Text => BrokerRegistryValueType.Text,
        RegistryValueType.ExpandableText => BrokerRegistryValueType.ExpandableText,
        RegistryValueType.Binary => BrokerRegistryValueType.Binary,
        RegistryValueType.DWord => BrokerRegistryValueType.DWord,
        RegistryValueType.MultiText => BrokerRegistryValueType.MultiText,
        RegistryValueType.QWord => BrokerRegistryValueType.QWord,
        _ => throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(RegistryValueType)),
    };
}
