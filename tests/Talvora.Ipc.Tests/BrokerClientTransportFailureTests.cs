using System.Diagnostics;
using Talvora.Ipc.Client;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerClientTransportFailureTests
{
    [TestMethod]
    public async Task ExecuteShellNormalizesUnavailableBrokerWithoutHanging()
    {
        var constructor = typeof(BrokerClient).GetConstructor([typeof(string), typeof(TimeSpan)]);
        Assert.IsNotNull(
            constructor,
            "BrokerClient must expose a connection-timeout overload so broker availability cannot hang an execution RPC indefinitely.");

        var pipeName = $"Talvora.MissingBroker.{Guid.NewGuid():N}";
        using var client = (BrokerClient)constructor.Invoke([pipeName, TimeSpan.FromMilliseconds(250)]);
        var stopwatch = Stopwatch.StartNew();

        var result = await client.ExecuteShellAsync(
            new BrokerShellExecutionRequest("Write-Output 'must-not-run'"),
            CancellationToken.None);

        stopwatch.Stop();
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual("broker_unavailable", result.Error.Code);
        Assert.AreEqual("elevated.execute", result.Error.Operation);
        Assert.IsTrue(result.Error.Retryable);
        Assert.IsTrue(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Unavailable broker should fail promptly. Elapsed: {stopwatch.Elapsed}.");
    }
}
