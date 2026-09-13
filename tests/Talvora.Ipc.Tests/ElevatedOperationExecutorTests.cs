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
}
