using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpElevatedRoutingTests
{
    [TestMethod]
    public async Task RunShellRoutesAdministratorRequestToBroker()
    {
        var localShell = new RecordingShellService();
        var brokerClient = new RecordingBrokerClient();
        var operationExecutor = new OperationExecutor(new DefaultErrorMapper());
        var router = new ExecutionRouter(
            localShell,
            new UnusedProcessService(),
            operationExecutor,
            brokerClient);
        var tools = new ShellTools(router);

        var envelope = await tools.RunShell(
            "echo elevated-routing",
            ShellKind.Cmd,
            @"C:\Windows",
            loadProfile: false,
            runAsAdministrator: true,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(envelope.Ok, envelope.Error?.Message);
        Assert.IsNotNull(envelope.Data);
        Assert.AreEqual(0, localShell.CallCount, "Explicit administrator shell execution must bypass the normal local shell service.");
        Assert.AreEqual(1, brokerClient.ShellCallCount);
        Assert.IsNotNull(brokerClient.LastShellRequest);
        Assert.AreEqual("echo elevated-routing", brokerClient.LastShellRequest.Command);
        Assert.AreEqual(BrokerShellKind.Cmd, brokerClient.LastShellRequest.Shell);
        Assert.AreEqual(@"C:\Windows", brokerClient.LastShellRequest.WorkingDirectory);
        Assert.AreEqual(4242, envelope.Data.ProcessId);
        Assert.AreEqual(7, envelope.Data.ExitCode);
        Assert.AreEqual("broker-stdout", envelope.Data.StandardOutput);
        Assert.AreEqual("broker-stderr", envelope.Data.StandardError);
    }

    private sealed class RecordingShellService : IShellService
    {
        public int CallCount { get; private set; }

        public ValueTask<ShellExecutionResult> ExecuteAsync(
            ShellExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new ShellExecutionResult(
                -1,
                -1,
                "local-stdout",
                "local-stderr",
                TimeSpan.Zero));
        }
    }

    private sealed class UnusedProcessService : IProcessService
    {
        public ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessStartResult> StartAsync(
            StartProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask StopAsync(
            int processId,
            bool entireProcessTree = true,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBrokerClient : IBrokerClient
    {
        public int ShellCallCount { get; private set; }

        public BrokerShellExecutionRequest? LastShellRequest { get; private set; }

        public Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BrokerProbeResult> ProbeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteShellAsync(
            BrokerShellExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ShellCallCount++;
            LastShellRequest = request;
            return Task.FromResult(TalvoraResult.Success(new BrokerExecutionResult(
                request.OperationId ?? "broker-generated-operation",
                4242,
                7,
                "broker-stdout",
                "broker-stderr",
                TimeSpan.FromMilliseconds(25))));
        }

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteProcessAsync(
            BrokerProcessExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TalvoraResult<BrokerRegistryMutationResult>> ExecuteRegistryAsync(
            BrokerRegistryMutationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
