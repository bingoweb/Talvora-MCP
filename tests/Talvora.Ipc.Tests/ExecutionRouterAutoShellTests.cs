using System.ComponentModel;
using Talvora.Abstractions;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ExecutionRouterAutoShellTests
{
    [TestMethod]
    public async Task AutoShellFallsBackToBrokerWhenWindowsRequiresElevationBeforeStart()
    {
        var localShell = new ElevationRequiredShellService();
        var brokerClient = new RecordingBrokerClient();
        var router = new ExecutionRouter(
            localShell,
            new UnusedProcessService(),
            new OperationExecutor(new DefaultErrorMapper()),
            brokerClient);

        var result = await router.ExecuteShellAsync(
            new ShellExecutionRequest(
                "echo auto-shell",
                ShellKind.Cmd,
                WorkingDirectory: @"C:\Windows"),
            ExecutionPrivilege.Auto,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, localShell.CallCount);
        Assert.AreEqual(1, brokerClient.ShellCallCount);
        Assert.IsNotNull(brokerClient.LastShellRequest);
        Assert.AreEqual("echo auto-shell", brokerClient.LastShellRequest.Command);
        Assert.AreEqual(BrokerShellKind.Cmd, brokerClient.LastShellRequest.Shell);
        Assert.AreEqual(@"C:\Windows", brokerClient.LastShellRequest.WorkingDirectory);
        Assert.AreEqual(8181, result.Value.ProcessId);
        Assert.AreEqual(0, result.Value.ExitCode);
        Assert.AreEqual("broker-shell-stdout", result.Value.StandardOutput);
    }

    private sealed class ElevationRequiredShellService : IShellService
    {
        public int CallCount { get; private set; }

        public ValueTask<ShellExecutionResult> ExecuteAsync(
            ShellExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new Win32Exception(740, "The requested operation requires elevation.");
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
                request.OperationId ?? "auto-shell-operation",
                8181,
                0,
                "broker-shell-stdout",
                string.Empty,
                TimeSpan.FromMilliseconds(3))));
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
