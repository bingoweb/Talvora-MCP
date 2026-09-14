using System.Reflection;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Modules.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryMcpKeyManagementTests
{
    [TestMethod]
    public async Task RegistryKeyToolsRouteParametersThroughRegistryService()
    {
        var registry = new RecordingRegistryService();
        var executor = new PassthroughOperationExecutor();
        var toolType = typeof(FileSystemTools).Assembly
            .GetType("Talvora.Adapter.Mcp.RegistryTools", throwOnError: false);

        Assert.IsNotNull(toolType, "Talvora.Adapter.Mcp must expose RegistryTools.");
        var tool = Activator.CreateInstance(toolType, registry, executor);
        Assert.IsNotNull(tool);

        var createMethod = toolType.GetMethod("CreateRegistryKey");
        Assert.IsNotNull(createMethod, "RegistryTools must expose CreateRegistryKey.");
        AssertMutatingToolMetadata(createMethod);

        var createInvocation = createMethod.Invoke(
            tool,
            [RegistryHiveId.CurrentUser, "Software\\Talvora\\Created", RegistryViewId.Registry64, CancellationToken.None]);
        var createEnvelope = await (Task<ToolEnvelope<bool>>)createInvocation!;

        Assert.IsTrue(createEnvelope.Ok);
        Assert.IsTrue(createEnvelope.Data);
        Assert.AreEqual("registry.create_key", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.CurrentUser, registry.LastHive);
        Assert.AreEqual("Software\\Talvora\\Created", registry.LastSubKeyPath);
        Assert.AreEqual(RegistryViewId.Registry64, registry.LastView);

        var deleteMethod = toolType.GetMethod("DeleteRegistryKey");
        Assert.IsNotNull(deleteMethod, "RegistryTools must expose DeleteRegistryKey.");
        AssertMutatingToolMetadata(deleteMethod);

        var deleteInvocation = deleteMethod.Invoke(
            tool,
            [RegistryHiveId.LocalMachine, "Software\\Talvora\\Obsolete", true, RegistryViewId.Registry32, CancellationToken.None]);
        var deleteEnvelope = await (Task<ToolEnvelope<bool>>)deleteInvocation!;

        Assert.IsTrue(deleteEnvelope.Ok);
        Assert.IsTrue(deleteEnvelope.Data);
        Assert.AreEqual("registry.delete_key", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.LocalMachine, registry.LastHive);
        Assert.AreEqual("Software\\Talvora\\Obsolete", registry.LastSubKeyPath);
        Assert.IsTrue(registry.LastRecursive);
        Assert.AreEqual(RegistryViewId.Registry32, registry.LastView);
    }

    private static void AssertMutatingToolMetadata(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.IsNotNull(attribute, $"{method.Name} must be exposed as an MCP tool.");
        Assert.IsTrue(attribute.Destructive);
        Assert.IsTrue(attribute.Idempotent);
        Assert.IsFalse(attribute.ReadOnly);
        Assert.IsFalse(attribute.OpenWorld);
    }

    private sealed class RecordingRegistryService : IRegistryService
    {
        public RegistryHiveId? LastHive { get; private set; }
        public string? LastSubKeyPath { get; private set; }
        public RegistryViewId? LastView { get; private set; }
        public bool LastRecursive { get; private set; }

        public ValueTask<RegistryValueData> ReadValueAsync(
            RegistryHiveId hive,
            string subKeyPath,
            string? valueName = null,
            RegistryViewId view = RegistryViewId.Default,
            bool expandEnvironmentStrings = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask WriteValueAsync(
            RegistryValueData value,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DeleteValueAsync(
            RegistryHiveId hive,
            string subKeyPath,
            string? valueName = null,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask CreateKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastView = view;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            bool recursive = false,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastRecursive = recursive;
            LastView = view;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> ListValueNamesAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
