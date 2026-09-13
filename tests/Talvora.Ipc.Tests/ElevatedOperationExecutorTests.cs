using Talvora.Ipc.Contracts;
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
    public async Task OperationExecutorExposesSingleRequestEntryPoint()
    {
        const string operationId = "op-powershell-001";
        var request = new ElevatedOperationRequest
        {
            OperationId = operationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = 10_000,
            Shell = new ShellExecutionOperation
            {
                Shell = ElevatedShellKind.Powershell,
                Command = "Write-Output 'talvora-elevated-ok'; [Console]::Error.WriteLine('talvora-elevated-err'); exit 7",
            },
        };

        var response = await new Talvora.ElevatedBroker.ElevatedOperationExecutor()
            .ExecuteAsync(request, CancellationToken.None);

        Assert.AreEqual(operationId, response.OperationId);
        Assert.AreEqual(BrokerProtocol.CurrentVersion, response.ProtocolVersion);
        Assert.IsTrue(response.Success);
        Assert.IsNotNull(response.Execution);
        Assert.IsTrue(response.Execution.ProcessId > 0);
        Assert.AreEqual(7, response.Execution.ExitCode);
        StringAssert.Contains(response.Execution.Stdout, "talvora-elevated-ok");
        StringAssert.Contains(response.Execution.Stderr, "talvora-elevated-err");
    }
}
