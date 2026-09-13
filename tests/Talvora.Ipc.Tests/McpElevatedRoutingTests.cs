using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpElevatedRoutingTests
{
    [TestMethod]
    public async Task RunShellRoutesElevatedExecutionToBroker()
    {
        var localShell = new RecordingShellService();
        var brokerClient = new RecordingBrokerClient();
        var operationExecutor = new OperationExecutor(new DefaultErrorMapper());

        var constructor = typeof(ShellTools).GetConstructor(
            [typeof(IShellService), typeof(IOperationExecutor), typeof(IBrokerClient)]);
        Assert.IsNotNull(
            constructor,
            "ShellTools must accept IBrokerClient so elevated execution can use the broker without exposing gRPC types.");

        var tools = (ShellTools)constructor.Invoke([localShell, operationExecutor, brokerClient]);
        var method = typeof(ShellTools).GetMethod(nameof(ShellTools.RunShell));
        Assert.IsNotNull(method);

        var parameters = method.GetParameters();
        var elevatedParameter = parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, "elevated", StringComparison.Ordinal) &&
            parameter.ParameterType == typeof(bool));
        Assert.IsNotNull(elevatedParameter, "RunShell must expose an explicit elevated boolean option.");

        var arguments = parameters.Select(parameter => parameter.Name switch
        {
            "command" => (object)"echo elevated-routing",
            "shell" => ShellKind.Cmd,
            "workingDirectory" => @"C:\Windows",
            "loadProfile" => false,
            "elevated" => true,
            "cancellationToken" => CancellationToken.None,
            _ => throw new InvalidOperationException($"Unexpected RunShell parameter '{parameter.Name}'."),
        }).ToArray();

        var invocation = method.Invoke(tools, arguments);
        var invocationTask = invocation as Task<ToolEnvelope<ShellExecutionResult>>;
        Assert.IsNotNull(invocationTask);
        var envelope = await invocationTask;

        Assert.IsTrue(envelope.Ok, envelope.Error?.Message);
        Assert.IsNotNull(envelope.Data);
        Assert.AreEqual(0, localShell.CallCount, "Elevated shell execution must not run through the non-elevated local shell service.");
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
    }
}
