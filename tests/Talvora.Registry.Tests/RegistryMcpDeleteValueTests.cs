using System.Reflection;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Modules.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryMcpDeleteValueTests
{
    [TestMethod]
    public async Task DeleteRegistryValueRoutesParametersThroughRegistryService()
    {
        var registry = new RecordingRegistryService();
        var executor = new PassthroughOperationExecutor();
        var toolType = typeof(FileSystemTools).Assembly
            .GetType("Talvora.Adapter.Mcp.RegistryTools", throwOnError: false);

        Assert.IsNotNull(toolType, "Talvora.Adapter.Mcp must expose RegistryTools.");

        var tool = Activator.CreateInstance(toolType, registry, executor);
        Assert.IsNotNull(tool);

        var method = toolType.GetMethod("DeleteRegistryValue");
        Assert.IsNotNull(method, "RegistryTools must expose DeleteRegistryValue.");

        var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.IsNotNull(attribute, "DeleteRegistryValue must be exposed as an MCP tool.");
        Assert.IsTrue(attribute.Destructive);
        Assert.IsTrue(attribute.Idempotent);
        Assert.IsFalse(attribute.ReadOnly);
        Assert.IsFalse(attribute.OpenWorld);

        var invocation = method.Invoke(
            tool,
            [
                RegistryHiveId.CurrentUser,
                "Software\\Talvora",
                "Obsolete",
                RegistryViewId.Registry64,
                CancellationToken.None,
            ]);

        var envelope = await (Task<ToolEnvelope<bool>>)invocation!;

        Assert.IsTrue(envelope.Ok);
        Assert.IsTrue(envelope.Data);
        Assert.AreEqual("registry.delete_value", executor.LastOperation);
        Assert.AreEqual(RegistryHiveId.CurrentUser, registry.LastHive);
        Assert.AreEqual("Software\\Talvora", registry.LastSubKeyPath);
        Assert.AreEqual("Obsolete", registry.LastValueName);
        Assert.AreEqual(RegistryViewId.Registry64, registry.LastView);
    }

    private sealed class RecordingRegistryService : IRegistryService
    {
        public RegistryHiveId? LastHive { get; private set; }
        public string? LastSubKeyPath { get; private set; }
        public string? LastValueName { get; private set; }
        public RegistryViewId? LastView { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHive = hive;
            LastSubKeyPath = subKeyPath;
            LastValueName = valueName;
            LastView = view;
            return ValueTask.CompletedTask;
        }

        public ValueTask CreateKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DeleteKeyAsync(
            RegistryHiveId hive,
            string subKeyPath,
            bool recursive = false,
            RegistryViewId view = RegistryViewId.Default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
