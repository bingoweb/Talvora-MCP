using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerProtocolTests
{
    [TestMethod]
    public void GeneratedGrpcContractMatchesCurrentProtocolVersion()
    {
        var descriptor = BrokerControl.Descriptor;
        var expectedServiceName = $"talvora.ipc.v{BrokerProtocol.CurrentVersion}.BrokerControl";

        Assert.AreEqual(expectedServiceName, descriptor.FullName);

        var packageVersion = descriptor.File.Package.Split('.').Last();
        Assert.EndsWith($".{packageVersion}", BrokerProtocol.DefaultPipeName);
    }
}
