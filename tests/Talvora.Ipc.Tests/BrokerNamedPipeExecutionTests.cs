using System.Diagnostics;
using Talvora.Ipc.Client;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerNamedPipeExecutionTests
{
    [TestMethod]
    public async Task BrokerClientExecutesShellOverRealNamedPipe()
    {
        var repositoryRoot = FindRepositoryRoot();
        var brokerAssembly = Path.Combine(
            repositoryRoot,
            "src",
            "Talvora.ElevatedBroker",
            "bin",
            "Release",
            "net10.0-windows",
            "Talvora.ElevatedBroker.dll");
        Assert.IsTrue(File.Exists(brokerAssembly), $"Broker assembly was not found at '{brokerAssembly}'.");

        var pipeName = $"Talvora.ElevatedBroker.Tests.{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(brokerAssembly);
        startInfo.ArgumentList.Add($"--Broker:PipeName={pipeName}");

        using var brokerProcess = new Process { StartInfo = startInfo };
        Assert.IsTrue(brokerProcess.Start(), "The Elevated Broker test process could not be started.");
        var brokerStdout = brokerProcess.StandardOutput.ReadToEndAsync();
        var brokerStderr = brokerProcess.StandardError.ReadToEndAsync();

        try
        {
            using var client = new BrokerClient(pipeName);
            var probe = await WaitUntilReadyAsync(client, brokerProcess);
            Assert.IsTrue(
                probe.Connected && probe.Health?.Ready == true,
                $"Broker did not become ready. Last probe error: {probe.Error}");

            const string operationId = "pipe-e2e-shell-001";
            var result = await client.ExecuteShellAsync(
                new BrokerShellExecutionRequest(
                    "Write-Output 'talvora-pipe-e2e'; [Console]::Error.WriteLine('talvora-pipe-e2e-err'); exit 3",
                    Timeout: TimeSpan.FromSeconds(5),
                    OperationId: operationId));

            Assert.IsTrue(result.IsSuccess, result.Error?.Message);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual(operationId, result.Value.OperationId);
            Assert.AreEqual(3, result.Value.ExitCode);
            StringAssert.Contains(result.Value.StandardOutput, "talvora-pipe-e2e");
            StringAssert.Contains(result.Value.StandardError, "talvora-pipe-e2e-err");
        }
        finally
        {
            if (!brokerProcess.HasExited)
            {
                brokerProcess.Kill(entireProcessTree: true);
            }

            await brokerProcess.WaitForExitAsync();
            _ = await brokerStdout;
            _ = await brokerStderr;
        }
    }

    private static async Task<BrokerProbeResult> WaitUntilReadyAsync(
        BrokerClient client,
        Process brokerProcess)
    {
        BrokerProbeResult? lastProbe = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (brokerProcess.HasExited)
            {
                break;
            }

            lastProbe = await client.ProbeAsync(TimeSpan.FromMilliseconds(250));
            if (lastProbe.Connected && lastProbe.Health?.Ready == true)
            {
                return lastProbe;
            }

            await Task.Delay(100);
        }

        return lastProbe ?? BrokerProbeResult.Failure("Broker process exited before the first health probe completed.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "Talvora.ElevatedBroker",
                    "Talvora.ElevatedBroker.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Talvora repository root from the test output directory.");
    }
}
