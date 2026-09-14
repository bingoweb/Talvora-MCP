using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedOperationContractTests
{
    [TestMethod]
    public void BrokerGrpcContractExposesExecuteOperation()
    {
        var executeMethod = BrokerControl.Descriptor.Methods
            .SingleOrDefault(static method => method.Name == "Execute");

        Assert.IsNotNull(executeMethod, "BrokerControl must expose an Execute RPC for elevated operations.");
        Assert.AreEqual("ElevatedOperationRequest", executeMethod.InputType.Name);
        Assert.AreEqual("ElevatedOperationResponse", executeMethod.OutputType.Name);
    }
}
