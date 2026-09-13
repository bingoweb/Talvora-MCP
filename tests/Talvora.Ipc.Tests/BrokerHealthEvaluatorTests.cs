using Grpc.Core;
using Talvora.ElevatedBroker;
using Talvora.Ipc.Contracts;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerHealthEvaluatorTests
{
    [TestMethod]
    public void EvaluateReturnsCurrentProtocolAndReadyState()
    {
        var response = BrokerHealthEvaluator.Evaluate(BrokerProtocol.CurrentVersion);

        Assert.AreEqual(BrokerProtocol.CurrentVersion, response.ProtocolVersion);
        Assert.IsTrue(response.Ready);
        Assert.IsTrue(response.ProcessId > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.ServiceVersion));
    }

    [TestMethod]
    public void EvaluateRejectsUnsupportedProtocolVersion()
    {
        try
        {
            _ = BrokerHealthEvaluator.Evaluate(BrokerProtocol.CurrentVersion + 1);
            Assert.Fail("Expected an RpcException for an unsupported broker protocol version.");
        }
        catch (RpcException exception)
        {
            Assert.AreEqual(StatusCode.FailedPrecondition, exception.StatusCode);
        }
    }
}
