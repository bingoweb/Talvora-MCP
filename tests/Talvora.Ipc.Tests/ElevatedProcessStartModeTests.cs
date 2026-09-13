using System.Diagnostics;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedProcessStartModeTests
{
    [TestMethod]
    public async Task StartOnlyReturnsBeforeChildProcessExits()
    {
        var processOperation = new ProcessExecutionOperation
        {
            FileName = "cmd.exe",
            CreateNoWindow = true,
        };
        processOperation.Arguments.Add("/d");
        processOperation.Arguments.Add("/s");
        processOperation.Arguments.Add("/c");
        processOperation.Arguments.Add("ping 127.0.0.1 -n 6 >nul");

        var modeProperty = typeof(ProcessExecutionOperation).GetProperty("Mode");
        Assert.IsNotNull(
            modeProperty,
            "ProcessExecutionOperation must expose an explicit execution mode so StartProcess can return immediately without changing existing wait-for-exit semantics.");

        var startOnly = Enum.Parse(modeProperty.PropertyType, "StartOnly", ignoreCase: false);
        modeProperty.SetValue(processOperation, startOnly);

        var request = new ElevatedOperationRequest
        {
            OperationId = "op-process-start-only-001",
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Process = processOperation,
        };

        var executor = new Talvora.ElevatedBroker.ElevatedOperationExecutor();
        var stopwatch = Stopwatch.StartNew();
        var response = await executor.ExecuteAsync(request, CancellationToken.None);
        stopwatch.Stop();

        Assert.IsTrue(response.Success, response.Error?.Message);
        Assert.IsNotNull(response.Execution);
        Assert.IsTrue(response.Execution.ProcessId > 0);
        Assert.IsTrue(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"StartOnly must return before the child exits. Elapsed={stopwatch.Elapsed}.");

        TryTerminate(response.Execution.ProcessId);
    }

    private static void TryTerminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
