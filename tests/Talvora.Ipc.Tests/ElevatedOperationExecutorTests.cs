using System.Reflection;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedOperationExecutorTests
{
    [TestMethod]
    public void ElevatedBrokerExposesOperationExecutor()
    {
        var executorType = typeof(Talvora.ElevatedBroker.BrokerControlService).Assembly.GetType(
            "Talvora.ElevatedBroker.ElevatedOperationExecutor",
            throwOnError: false,
            ignoreCase: false);

        Assert.IsNotNull(
            executorType,
            "Elevated Broker must own an execution-layer dispatcher instead of putting process logic into the gRPC service.");
    }

    [TestMethod]
    public void OperationExecutorExposesSingleRequestEntryPoint()
    {
        var executeMethod = typeof(Talvora.ElevatedBroker.ElevatedOperationExecutor).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(ElevatedOperationRequest), typeof(CancellationToken)],
            modifiers: null);

        Assert.IsNotNull(
            executeMethod,
            "Elevated execution must flow through ExecuteAsync(ElevatedOperationRequest, CancellationToken).");
        Assert.AreEqual(typeof(Task<ElevatedOperationResponse>), executeMethod.ReturnType);
    }
}
