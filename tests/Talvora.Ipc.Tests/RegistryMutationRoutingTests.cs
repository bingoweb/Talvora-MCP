using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Registry;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class RegistryMutationRoutingTests
{
    [TestMethod]
    public async Task RegistryMutationsFallBackToBrokerAndHonorExplicitElevation()
    {
        var routerType = typeof(ExecutionRouter).Assembly.GetType(
            "Talvora.Application.RegistryExecutionRouter",
            throwOnError: false,
            ignoreCase: false);
        Assert.IsNotNull(routerType, "Application must expose a dedicated Registry execution router.");

        var localRegistry = new ElevationRequiredRegistryService();
        var brokerClient = new RecordingBrokerClient();
        var executor = new OperationExecutor(new DefaultErrorMapper());
        var router = Activator.CreateInstance(routerType, localRegistry, executor, brokerClient);
        Assert.IsNotNull(router);

        var writeMethod = routerType.GetMethod(
            "WriteValueAsync",
            [typeof(RegistryValueData), typeof(RegistryViewId), typeof(ExecutionPrivilege), typeof(CancellationToken)]);
        Assert.IsNotNull(writeMethod);

        var value = new RegistryValueData(
            RegistryHiveId.LocalMachine,
            @"Software\Talvora.Tests\Routing",
            "Answer",
            RegistryValueType.DWord,
            DWordValue: 42);

        var autoPending = writeMethod.Invoke(
            router,
            [value, RegistryViewId.Registry64, ExecutionPrivilege.Auto, CancellationToken.None]);
        var autoValueTask = Assert.IsInstanceOfType<ValueTask<TalvoraResult<bool>>>(autoPending);
        var autoResult = await autoValueTask.ConfigureAwait(false);

        Assert.IsTrue(autoResult.IsSuccess, autoResult.Error?.Message);
        Assert.AreEqual(1, localRegistry.WriteCallCount);
        Assert.AreEqual(1, brokerClient.RegistryCallCount);
        Assert.IsNotNull(brokerClient.LastRegistryRequest);
        Assert.AreEqual(BrokerRegistryMutationKind.WriteValue, brokerClient.LastRegistryRequest.Kind);
        Assert.AreEqual(BrokerRegistryHive.LocalMachine, brokerClient.LastRegistryRequest.Hive);
        Assert.AreEqual(BrokerRegistryView.Registry64, brokerClient.LastRegistryRequest.View);
        Assert.AreEqual(42, brokerClient.LastRegistryRequest.DWordValue);

        var tools = Activator.CreateInstance(typeof(RegistryTools), localRegistry, executor, router);
        Assert.IsNotNull(tools, "RegistryTools must accept the Registry execution router through DI.");
        var toolMethod = typeof(RegistryTools).GetMethod(
            "WriteRegistryValue",
            [typeof(RegistryValueData), typeof(RegistryViewId), typeof(bool), typeof(CancellationToken)]);
        Assert.IsNotNull(toolMethod, "WriteRegistryValue must expose explicit administrator routing.");

        var toolPending = toolMethod.Invoke(
            tools,
            [value, RegistryViewId.Registry64, true, CancellationToken.None]);
        var toolTask = Assert.IsInstanceOfType<Task<ToolEnvelope<bool>>>(toolPending);
        var envelope = await toolTask.ConfigureAwait(false);

        Assert.IsTrue(envelope.Ok, envelope.Error?.Message);
        Assert.AreEqual(1, localRegistry.WriteCallCount, "Explicit elevation must bypass the normal Registry service.");
        Assert.AreEqual(2, brokerClient.RegistryCallCount);
    }

    private sealed class ElevationRequiredRegistryService : IRegistryService
    {
        public int WriteCallCount { get; private set; }

        public ValueTask<RegistryValueData> ReadValueAsync(
            RegistryHiveId hive,
            string subKeyPath,
            string? valueName = null,
            RegistryViewId view = RegistryViewId.Default,
            bool expandEnvironmentStrings = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> ListValueNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask WriteValueAsync(
            RegistryValueData value,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            WriteCallCount++;
            throw new UnauthorizedAccessException("Elevation required for Registry write.");
        }

        public ValueTask DeleteValueAsync(
            RegistryHiveId hive,
            string subKeyPath,
            string? valueName = null,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask CreateKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DeleteKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            bool recursive = false,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingBrokerClient : IBrokerClient
    {
        public int RegistryCallCount { get; private set; }
        public BrokerRegistryMutationRequest? LastRegistryRequest { get; private set; }

        public Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BrokerProbeResult> ProbeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteShellAsync(
            BrokerShellExecutionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteProcessAsync(
            BrokerProcessExecutionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TalvoraResult<BrokerRegistryMutationResult>> ExecuteRegistryAsync(
            BrokerRegistryMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            RegistryCallCount++;
            LastRegistryRequest = request;
            return Task.FromResult(TalvoraResult.Success(new BrokerRegistryMutationResult(
                request.OperationId ?? $"registry-{RegistryCallCount}",
                Completed: true)));
        }
    }
}
