using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Ipc.Contracts;

namespace Talvora.Architecture.Tests;

[TestClass]
public sealed class DependencyBoundaryTests
{
    [TestMethod]
    public void AbstractionsDoesNotReferenceMcpOrWindowsPlatformAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(TalvoraError).Assembly,
            "ModelContextProtocol",
            "Talvora.Platform.Windows");
    }

    [TestMethod]
    public void CoreDoesNotReferenceMcpOrWindowsPlatformAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(OperationExecutor).Assembly,
            "ModelContextProtocol",
            "Talvora.Platform.Windows");
    }

    [TestMethod]
    public void IpcContractsDoNotReferenceHostBrokerOrMcpAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(BrokerProtocol).Assembly,
            "Talvora.Host",
            "Talvora.ElevatedBroker",
            "ModelContextProtocol");
    }

    [TestMethod]
    public void IpcClientDoesNotReferenceHostOrMcpAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(BrokerClient).Assembly,
            "Talvora.Host",
            "ModelContextProtocol");
    }

    [TestMethod]
    public void ApplicationDoesNotReferenceAdapterHostBrokerMcpOrWindowsPlatformAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(ExecutionRouter).Assembly,
            "Talvora.Adapter.Mcp",
            "Talvora.Host",
            "Talvora.ElevatedBroker",
            "ModelContextProtocol",
            "Talvora.Platform.Windows");
    }

    [TestMethod]
    public void McpAdapterDoesNotReferenceIpcTransportBrokerHostOrWindowsPlatformAssemblies()
    {
        AssertNoForbiddenReferences(
            typeof(ShellTools).Assembly,
            "Talvora.Ipc.Client",
            "Talvora.Ipc.Contracts",
            "Talvora.ElevatedBroker",
            "Talvora.Host",
            "Talvora.Platform.Windows");
    }

    private static void AssertNoForbiddenReferences(
        System.Reflection.Assembly assembly,
        params string[] forbiddenPrefixes)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (var prefix in forbiddenPrefixes)
        {
            Assert.IsFalse(
                references.Any(reference => reference.StartsWith(prefix, StringComparison.Ordinal)),
                $"{assembly.GetName().Name} must not reference {prefix}. References: {string.Join(", ", references)}");
        }
    }
}
