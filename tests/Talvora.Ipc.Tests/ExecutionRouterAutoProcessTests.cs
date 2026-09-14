using System.ComponentModel;
using Talvora.Abstractions;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ExecutionRouterAutoProcessTests
{
    private static readonly string[] ExpectedArguments = ["--test"];

    [TestMethod]
    public async Task AutoProcessStartFallsBackToBrokerWhenWindowsRequiresElevation()
    {
        var localProcessService = new ElevationRequiredProcessService();
        var brokerClient = new RecordingBrokerClient();
        var router = new ExecutionRouter(
            new UnusedShellService(),
            localProcessService,
            new OperationExecutor(new DefaultErrorMapper()),
            brokerClient);

        var result = await router.StartProcessAsync(
            new StartProcessRequest("requires-admin.exe", ExpectedArguments, @"C:\Windows"),
            ExecutionPrivilege.Auto,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, localProcessService.StartCallCount);
        Assert.AreEqual(1, brokerClient.ProcessCallCount);
        Assert.IsNotNull(brokerClient.LastProcessRequest);
        Assert.AreEqual(BrokerProcessExecutionMode.StartOnly, brokerClient.LastProcessRequest.Mode);
        Assert.AreEqual("requires-admin.exe", brokerClient.LastProcessRequest.FileName);
        CollectionAssert.AreEqual(ExpectedArguments, brokerClient.LastProcessRequest.Arguments?.ToArray());
        Assert.AreEqual(@"C:\Windows", brokerClient.LastProcessRequest.WorkingDirectory);
        Assert.AreEqual(9090, result.Value.ProcessId);
    }

    private sealed class ElevationRequiredProcessService : IProcessService
    {
        public int StartCallCount { get; private set; }

        public ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessStartResult> StartAsync(
            StartProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            throw new Win32Exception(740, "The requested operation requires elevation.");
        }

        public ValueTask StopAsync(
            int processId,
            bool entireProcessTree = true,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
                request.OperationId ?? "auto-process-operation",
                9090,
                null,
                string.Empty,
                string.Empty,
                TimeSpan.FromMilliseconds(2))));
        }
    }
}
