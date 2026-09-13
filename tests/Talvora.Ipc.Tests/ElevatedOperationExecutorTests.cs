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
        var executor = new Talvora.ElevatedBroker.ElevatedOperationExecutor();

        const string operationId = "op-powershell-001";
        var powerShellRequest = new ElevatedOperationRequest
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

        var powerShellResponse = await executor.ExecuteAsync(powerShellRequest, CancellationToken.None);

        Assert.AreEqual(operationId, powerShellResponse.OperationId);
        Assert.AreEqual(BrokerProtocol.CurrentVersion, powerShellResponse.ProtocolVersion);
        Assert.IsTrue(powerShellResponse.Success);
        Assert.IsNotNull(powerShellResponse.Execution);
        Assert.IsTrue(powerShellResponse.Execution.ProcessId > 0);
        Assert.AreEqual(7, powerShellResponse.Execution.ExitCode);
        StringAssert.Contains(powerShellResponse.Execution.Stdout, "talvora-elevated-ok");
        StringAssert.Contains(powerShellResponse.Execution.Stderr, "talvora-elevated-err");

        var workingDirectory = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var cmdRequest = new ElevatedOperationRequest
        {
            OperationId = "op-cmd-001",
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = 10_000,
            Shell = new ShellExecutionOperation
            {
                Shell = ElevatedShellKind.Cmd,
                Command = "echo %TALVORA_TEST_VAR% && cd",
                WorkingDirectory = workingDirectory,
            },
        };
        cmdRequest.Shell.Environment.Add(new EnvironmentVariable
        {
            Name = "TALVORA_TEST_VAR",
            Value = "talvora-env-ok",
        });

        var cmdResponse = await executor.ExecuteAsync(cmdRequest, CancellationToken.None);

        Assert.IsTrue(cmdResponse.Success);
        Assert.IsNotNull(cmdResponse.Execution);
        Assert.AreEqual(0, cmdResponse.Execution.ExitCode);
        StringAssert.Contains(cmdResponse.Execution.Stdout, "talvora-env-ok");
        StringAssert.Contains(cmdResponse.Execution.Stdout, workingDirectory);

        var processRequest = new ElevatedOperationRequest
        {
            OperationId = "op-process-001",
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = 10_000,
            Process = new ProcessExecutionOperation
            {
                FileName = "cmd.exe",
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
            },
        };
        processRequest.Process.Arguments.Add("/d");
        processRequest.Process.Arguments.Add("/s");
        processRequest.Process.Arguments.Add("/c");
        processRequest.Process.Arguments.Add("echo %TALVORA_PROCESS_VAR% & exit /b 5");
        processRequest.Process.Environment.Add(new EnvironmentVariable
        {
            Name = "TALVORA_PROCESS_VAR",
            Value = "talvora-process-ok",
        });

        var processResponse = await executor.ExecuteAsync(processRequest, CancellationToken.None);

        Assert.IsTrue(processResponse.Success);
        Assert.IsNotNull(processResponse.Execution);
        Assert.IsTrue(processResponse.Execution.ProcessId > 0);
        Assert.AreEqual(5, processResponse.Execution.ExitCode);
        StringAssert.Contains(processResponse.Execution.Stdout, "talvora-process-ok");
    }

    [TestMethod]
    public async Task TimeoutAndCancellationAreNormalized()
    {
        var executor = new Talvora.ElevatedBroker.ElevatedOperationExecutor();
        var timeoutRequest = new ElevatedOperationRequest
        {
            OperationId = "op-timeout-001",
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = 150,
            Shell = new ShellExecutionOperation
            {
                Shell = ElevatedShellKind.Powershell,
                Command = "Start-Sleep -Seconds 5",
            },
        };

        var timeoutResponse = await executor.ExecuteAsync(timeoutRequest, CancellationToken.None);

        Assert.IsFalse(timeoutResponse.Success);
        Assert.IsNotNull(timeoutResponse.Error);
        Assert.AreEqual("timeout", timeoutResponse.Error.Code);
        Assert.AreEqual("elevated.shell.execute", timeoutResponse.Error.Operation);
        Assert.IsTrue(timeoutResponse.Error.Retryable);

        var cancellationRequest = new ElevatedOperationRequest
        {
            OperationId = "op-cancel-001",
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = 10_000,
            Shell = new ShellExecutionOperation
            {
                Shell = ElevatedShellKind.Powershell,
                Command = "Start-Sleep -Seconds 5",
            },
        };
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var cancellationResponse = await executor.ExecuteAsync(cancellationRequest, cancellationSource.Token);

        Assert.IsFalse(cancellationResponse.Success);
        Assert.IsNotNull(cancellationResponse.Error);
        Assert.AreEqual("operation_cancelled", cancellationResponse.Error.Code);
        Assert.AreEqual("elevated.shell.execute", cancellationResponse.Error.Operation);
        Assert.IsFalse(cancellationResponse.Error.Retryable);
    }
}
