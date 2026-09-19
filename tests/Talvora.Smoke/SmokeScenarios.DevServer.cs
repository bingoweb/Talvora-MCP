using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunDevServerAsync(IReadOnlyDictionary<string, McpClientTool> byName, string smokeId, string repositoryPath)
    {
        var devServerPort = GetFreeLoopbackTcpPort();
        var devServerBody = "talvora-dev-server-" + smokeId;
        var fixtureAssemblyPath =
            System.Reflection.Assembly
                .GetExecutingAssembly()
                .Location;
        var devServerDotnetExecutable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Smoke process executable path is unavailable.");
        
        var devServerStartResult = await EnsureSuccess(byName["talvora_dev_server_start"], new()
        {
            ["executable"] = devServerDotnetExecutable,
            ["arguments"] = new[]
            {
                fixtureAssemblyPath,
                "--dev-server-fixture",
                devServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                devServerBody,
            },
            ["workingDirectory"] = repositoryPath,
            ["tcpHost"] = "127.0.0.1",
            ["tcpPort"] = devServerPort,
            ["httpUrl"] = $"http://127.0.0.1:{devServerPort}/health",
            ["expectedStatusCodes"] = new[] { 200 },
            ["requireAll"] = true,
            ["timeoutSeconds"] = 15,
            ["probeTimeoutSeconds"] = 2,
            ["pollIntervalMilliseconds"] = 100,
            ["stopOnFailure"] = true,
            ["logTailBytes"] = 4096,
        });
        
        if (devServerStartResult.StructuredContent is not { } devServerStartJson ||
            !devServerStartJson.GetProperty("ready").GetBoolean() ||
            !devServerStartJson.GetProperty("tcpProbe").GetProperty("ready").GetBoolean() ||
            !devServerStartJson.GetProperty("httpProbe").GetProperty("ready").GetBoolean())
        {
            throw new InvalidOperationException("dev-server start did not reach combined TCP/HTTP readiness.");
        }
        
        var devServerJobId = devServerStartJson.GetProperty("jobId").GetString()
            ?? throw new InvalidOperationException("dev-server start returned no job ID.");
        
        try
        {
            var devServerGetResult = await EnsureSuccess(byName["talvora_dev_server_get"], new()
            {
                ["jobId"] = devServerJobId,
                ["logTailBytes"] = 4096,
            });
        
            if (devServerGetResult.StructuredContent is not { } devServerGetJson ||
                !devServerGetJson.GetProperty("ready").GetBoolean() ||
                !string.Equals(
                    devServerGetJson.GetProperty("state").GetString(),
                    "Running",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("dev-server get did not report a ready running server.");
            }
        
            var devServerListResult = await EnsureSuccess(byName["talvora_dev_server_list"], new()
            {
                ["includeExited"] = false,
                ["maxResults"] = 0,
            });
        
            if (devServerListResult.StructuredContent is not { } devServerListJson ||
                !devServerListJson.GetProperty("servers").EnumerateArray().Any(server =>
                    string.Equals(
                        server.GetProperty("jobId").GetString(),
                        devServerJobId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("dev-server list did not include the running smoke server.");
            }
        
            var devServerWaitResult = await EnsureSuccess(byName["talvora_dev_server_wait"], new()
            {
                ["jobId"] = devServerJobId,
                ["timeoutSeconds"] = 5,
                ["stopOnFailure"] = false,
                ["logTailBytes"] = 4096,
            });
        
            if (devServerWaitResult.StructuredContent is not { } devServerWaitJson ||
                !devServerWaitJson.GetProperty("ready").GetBoolean())
            {
                throw new InvalidOperationException("dev-server wait did not preserve ready state.");
            }
        
            var directHttpResult = await EnsureSuccess(byName["talvora_http_request"], new()
            {
                ["method"] = "GET",
                ["url"] = $"http://127.0.0.1:{devServerPort}/smoke",
                ["responseMode"] = "text",
                ["maxResponseBytes"] = 4096,
            });
        
            if (directHttpResult.StructuredContent is not { } directHttpJson ||
                directHttpJson.GetProperty("statusCode").GetInt32() != 200 ||
                !string.Equals(
                    directHttpJson.GetProperty("body").GetString(),
                    devServerBody,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("dev-server smoke endpoint returned an unexpected response.");
            }
        }
        finally
        {
            var devServerStopResult = await EnsureSuccess(byName["talvora_dev_server_stop"], new()
            {
                ["jobId"] = devServerJobId,
                ["entireProcessTree"] = true,
                ["timeoutSeconds"] = 15,
                ["deleteArtifacts"] = true,
            });
        
            if (devServerStopResult.StructuredContent is not { } devServerStopJson ||
                !devServerStopJson.GetProperty("exited").GetBoolean() ||
                !devServerStopJson.GetProperty("deleted").GetBoolean())
            {
                throw new InvalidOperationException("dev-server stop did not exit and clean up the smoke server.");
            }
        }
    }
}
