using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Modules.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryMcpTests
{
    private static readonly string[] ListedSubKeys = ["Beta", "Zulu", "alpha"];
    private static readonly string[] ListedValueNames = ["", "Beta", "Zulu", "alpha"];

    [TestMethod]
    public async Task ReadRegistryValueRoutesParametersThroughRegistryService()
    {
        var registry = new RecordingRegistryService();
        var executor = new PassthroughOperationExecutor();
        var toolType = typeof(FileSystemTools).Assembly
            .GetType("Talvora.Adapter.Mcp.RegistryTools", throwOnError: false);

        Assert.IsNotNull(toolType, "Talvora.Adapter.Mcp must expose RegistryTools.");

        var tool = Activator.CreateInstance(toolType, registry, executor);
        Assert.IsNotNull(tool);

        var method = toolType.GetMethod("ReadRegistryValue");
        Assert.IsNotNull(method, "RegistryTools must expose ReadRegistryValue.");

        var invocation = method.Invoke(
            tool,
            [
                RegistryHiveId.CurrentUser,
                "Software\\Talvora",
                "Greeting",
                RegistryViewId.Registry64,
                false,
                CancellationToken.None,
            ]);

        var envelope = await (Task<ToolEnvelope<RegistryValueData>>)invocation!;

        Assert.IsTrue(envelope.Ok);
        Assert.IsNotNull(envelope.Data);
        Assert.AreEqual("registry.read", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.CurrentUser, registry.LastHive);
        Assert.AreEqual("Software\\Talvora", registry.LastSubKeyPath);
        Assert.AreEqual("Greeting", registry.LastValueName);
        Assert.AreEqual(RegistryViewId.Registry64, registry.LastView);
        Assert.IsFalse(registry.LastExpandEnvironmentStrings);
    }

    [TestMethod]
    public async Task ListRegistrySubKeysRoutesParametersThroughRegistryService()
    {
        var registry = new RecordingRegistryService();
        var executor = new PassthroughOperationExecutor();
        var toolType = typeof(FileSystemTools).Assembly
            .GetType("Talvora.Adapter.Mcp.RegistryTools", throwOnError: false);

        Assert.IsNotNull(toolType, "Talvora.Adapter.Mcp must expose RegistryTools.");

        var tool = Activator.CreateInstance(toolType, registry, executor);
        Assert.IsNotNull(tool);

        var method = toolType.GetMethod("ListRegistrySubKeys");
        Assert.IsNotNull(method, "RegistryTools must expose ListRegistrySubKeys.");

        var invocation = method.Invoke(
            tool,
            [
                RegistryHiveId.CurrentUser,
                "Software\\Talvora",
                RegistryViewId.Registry64,
                CancellationToken.None,
            ]);

        var envelope = await (Task<ToolEnvelope<IReadOnlyList<string>>>)invocation!;

        Assert.IsTrue(envelope.Ok);
        Assert.IsNotNull(envelope.Data);
        CollectionAssert.AreEqual(ListedSubKeys, envelope.Data.ToArray());
        Assert.AreEqual("registry.list_subkeys", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.CurrentUser, registry.LastHive);
        Assert.AreEqual("Software\\Talvora", registry.LastSubKeyPath);
        Assert.AreEqual(RegistryViewId.Registry64, registry.LastView);
    }

    [TestMethod]
    public async Task ListRegistryValueNamesRoutesParametersThroughRegistryService()
    {
        var registry = new RecordingRegistryService();
        var executor = new PassthroughOperationExecutor();
        var toolType = typeof(FileSystemTools).Assembly
            .GetType("Talvora.Adapter.Mcp.RegistryTools", throwOnError: false);

        Assert.IsNotNull(toolType, "Talvora.Adapter.Mcp must expose RegistryTools.");

        var tool = Activator.CreateInstance(toolType, registry, executor);
        Assert.IsNotNull(tool);

        var method = toolType.GetMethod("ListRegistryValueNames");
        Assert.IsNotNull(method, "RegistryTools must expose ListRegistryValueNames.");

        var invocation = method.Invoke(
            tool,
            [
                RegistryHiveId.CurrentUser,
                "Software\\Talvora",
                RegistryViewId.Registry64,
                CancellationToken.None,
            ]);

        var envelope = await (Task<ToolEnvelope<IReadOnlyList<string>>>)invocation!;

        Assert.IsTrue(envelope.Ok);
        Assert.IsNotNull(envelope.Data);
        CollectionAssert.AreEqual(ListedValueNames, envelope.Data.ToArray());
        Assert.AreEqual("registry.list_value_names", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.CurrentUser, registry.LastHive);
        Assert.AreEqual("Software\\Talvora", registry.LastSubKeyPath);
        Assert.AreEqual(RegistryViewId.Registry64, registry.LastView);
    }

    private sealed class RecordingRegistryService : IRegistryService
    {
        public RegistryHiveId? LastHive { get; private set; }
        public string? LastSubKeyPath { get; private set; }
        public string? LastValueName { get; private set; }
        public RegistryViewId? LastView { get; private set; }
        public bool LastExpandEnvironmentStrings { get; private set; }

        public ValueTask<RegistryValueData> ReadValueAsync(
            RegistryHiveId hive,
            string subKeyPath,
            string? valueName = null,
            RegistryViewId view = RegistryViewId.Default,
            bool expandEnvironmentStrings = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastValueName = valueName;
            LastView = view;
            LastExpandEnvironmentStrings = expandEnvironmentStrings;

            return ValueTask.FromResult(new RegistryValueData(
                hive,
                subKeyPath,
                valueName,
                RegistryValueType.Text,
                StringValue: "hello"));
        }

        public ValueTask WriteValueAsync(
            RegistryValueData value,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastView = view;

            return ValueTask.FromResult<IReadOnlyList<string>>(ListedSubKeys);
        }

        public ValueTask<IReadOnlyList<string>> ListValueNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastView = view;

            return ValueTask.FromResult<IReadOnlyList<string>>(ListedValueNames);
        }
    }

    private sealed class PassthroughOperationExecutor : IOperationExecutor
    {
        public string? LastOperation { get; private set; }

        public async ValueTask<TalvoraResult<T>> ExecuteAsync<T>(
            string operation,
            Func<CancellationToken, ValueTask<T>> action,
            CancellationToken cancellationToken = default)
        {
            LastOperation = operation;
            var value = await action(cancellationToken);
            return TalvoraResult.Success(value);
        }
    }
}
