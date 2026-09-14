using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpElevatedProcessRoutingTests
{
    private static readonly string[] ExpectedArguments = ["/d", "/c", "exit 0"];

    [TestMethod]
    public async Task StartProcessRoutesAdministratorRequestToBroker()
    {
        var localProcesses = new RecordingProcessService();
        var brokerClient = new RecordingBrokerClient();
        var operationExecutor = new OperationExecutor(new DefaultErrorMapper());
        var router = new ExecutionRouter(
            new UnusedShellService(),
            localProcesses,
            operationExecutor,
            brokerClient);
        var tools = new ProcessTools(localProcesses, operationExecutor, router);

        var envelope = await tools.StartProcess(
            "cmd.exe",
            ExpectedArguments,
            @"C:\Windows",
            runAsAdministrator: true,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(envelope.Ok, envelope.Error?.Message);
        Assert.IsNotNull(envelope.Data);
        Assert.AreEqual(0, localProcesses.StartCallCount, "Explicit administrator process start must bypass the normal local process service.");
        Assert.AreEqual(1, brokerClient.ProcessCallCount);
        Assert.IsNotNull(brokerClient.LastProcessRequest);
        Assert.AreEqual(BrokerProcessExecutionMode.StartOnly, brokerClient.LastProcessRequest.Mode);
        Assert.AreEqual("cmd.exe", brokerClient.LastProcessRequest.FileName);
        CollectionAssert.AreEqual(ExpectedArguments, brokerClient.LastProcessRequest.Arguments?.ToArray());
        Assert.AreEqual(@"C:\Windows", brokerClient.LastProcessRequest.WorkingDirectory);
        Assert.AreEqual(5151, envelope.Data.ProcessId);
    }

    private sealed class RecordingProcessService : IProcessService
    {
        public int StartCallCount { get; private set; }

        public ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ProcessSnapshot>>(Array.Empty<ProcessSnapshot>());

        public ValueTask<ProcessStartResult> StartAsync(
            StartProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return ValueTask.FromResult(new ProcessStartResult(-1));
        }

        public ValueTask StopAsync(
            int processId,
            bool entireProcessTree = true,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class UnusedShellService : IShellService
    {
        public ValueTask<ShellExecutionResult> ExecuteAsync(
            ShellExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBrokerClient : IBrokerClient
    {
        public int ProcessCallCount { get; private set; }

        public BrokerProcessExecutionRequest? LastProcessRequest { get; private set; }

        public Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BrokerProbeResult> ProbeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteShellAsync(
            BrokerShellExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TalvoraResult<BrokerExecutionResult>> ExecuteProcessAsync(
            BrokerProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ProcessCallCount++;
            LastProcessRequest = request;
            return Task.FromResult(TalvoraResult.Success(new BrokerExecutionResult(
                request.OperationId ?? "broker-generated-process-operation",
                5151,
                null,
                string.Empty,
                string.Empty,
                TimeSpan.FromMilliseconds(10))));
        }

        public Task<TalvoraResult<BrokerRegistryMutationResult>> ExecuteRegistryAsync(
            BrokerRegistryMutationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
