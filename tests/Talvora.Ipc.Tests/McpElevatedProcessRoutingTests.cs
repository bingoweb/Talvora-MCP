using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpElevatedProcessRoutingTests
{
    private static readonly string[] ExpectedArguments = ["/d", "/c", "exit 0"];

    [TestMethod]
    public async Task StartProcessRoutesElevatedExecutionToBroker()
    {
        var localProcesses = new RecordingProcessService();
        var brokerClient = new RecordingBrokerClient();
        var operationExecutor = new OperationExecutor(new DefaultErrorMapper());

        var constructor = typeof(ProcessTools).GetConstructor(
            [typeof(IProcessService), typeof(IOperationExecutor), typeof(IBrokerClient)]);
        Assert.IsNotNull(
            constructor,
            "ProcessTools must accept IBrokerClient so elevated process starts can use the broker without exposing gRPC types.");

        var tools = (ProcessTools)constructor.Invoke([localProcesses, operationExecutor, brokerClient]);
        var method = typeof(ProcessTools).GetMethod(nameof(ProcessTools.StartProcess));
        Assert.IsNotNull(method);

        var parameters = method.GetParameters();
        var elevatedParameter = parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, "elevated", StringComparison.Ordinal) &&
            parameter.ParameterType == typeof(bool));
        Assert.IsNotNull(elevatedParameter, "StartProcess must expose an explicit elevated boolean option.");

        var arguments = parameters.Select(parameter => parameter.Name switch
        {
            "fileName" => (object)"cmd.exe",
            "arguments" => ExpectedArguments,
            "workingDirectory" => @"C:\Windows",
            "elevated" => true,
            "cancellationToken" => CancellationToken.None,
            _ => throw new InvalidOperationException($"Unexpected StartProcess parameter '{parameter.Name}'."),
        }).ToArray();

        var invocation = method.Invoke(tools, arguments);
        var invocationTask = invocation as Task<ToolEnvelope<ProcessStartResult>>;
        Assert.IsNotNull(invocationTask);
        var envelope = await invocationTask;

        Assert.IsTrue(envelope.Ok, envelope.Error?.Message);
        Assert.IsNotNull(envelope.Data);
        Assert.AreEqual(0, localProcesses.StartCallCount, "Elevated process start must not run through the non-elevated local process service.");
        Assert.AreEqual(1, brokerClient.ProcessCallCount);
        Assert.IsNotNull(brokerClient.LastProcessRequest);
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
                0,
                string.Empty,
                string.Empty,
                TimeSpan.FromMilliseconds(10))));
        }
    }
}
