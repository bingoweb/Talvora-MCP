using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:7676/mcp";
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(20),
});

await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

string[] required =
[
    "talvora_system_info",
    "talvora_read_text",
    "talvora_write_text",
    "talvora_delete",
    "talvora_list",
    "talvora_run_process",
    "talvora_run_powershell",
    "talvora_service_list",
    "talvora_service_get",
    "talvora_service_start",
    "talvora_service_stop",
    "talvora_service_restart",
    "search",
    "fetch",
    "talvora_registry_create_key",
    "talvora_registry_get",
    "talvora_registry_set",
    "talvora_registry_list",
    "talvora_registry_delete_value",
    "talvora_registry_delete_key",
];

foreach (var name in required)
{
    if (!byName.ContainsKey(name)) throw new InvalidOperationException($"Missing MCP tool: {name}");
}

static async Task<CallToolResult> EnsureSuccess(
    McpClientTool tool,
    Dictionary<string, object?> arguments,
    CancellationToken cancellationToken = default)
{
    var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
    if (result.IsError is true)
    {
        throw new InvalidOperationException($"Tool failed: {tool.Name}");
    }
    return result;
}

await EnsureSuccess(byName["talvora_system_info"], []);

var serviceListResult = await EnsureSuccess(byName["talvora_service_list"], new()
{
    ["query"] = "EventLog",
});
if (serviceListResult.StructuredContent is not { } serviceListJson ||
    !serviceListJson.TryGetProperty("services", out var serviceList) ||
    serviceList.ValueKind != System.Text.Json.JsonValueKind.Array ||
    !serviceList.EnumerateArray().Any(item =>
        item.TryGetProperty("serviceName", out var name) &&
        string.Equals(name.GetString(), "EventLog", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("service list did not return EventLog.");
}

var eventLogResult = await EnsureSuccess(byName["talvora_service_get"], new()
{
    ["serviceName"] = "EventLog",
});
if (eventLogResult.StructuredContent is not { } eventLogJson ||
    !eventLogJson.TryGetProperty("found", out var eventLogFound) ||
    !eventLogFound.GetBoolean() ||
    !eventLogJson.TryGetProperty("service", out var eventLogService) ||
    !eventLogService.TryGetProperty("serviceName", out var eventLogName) ||
    !string.Equals(eventLogName.GetString(), "EventLog", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("service get did not return EventLog.");
}

var missingServiceResult = await EnsureSuccess(byName["talvora_service_get"], new()
{
    ["serviceName"] = "Talvora-Smoke-Missing-" + Guid.NewGuid().ToString("N"),
});
if (missingServiceResult.StructuredContent is not { } missingServiceJson ||
    !missingServiceJson.TryGetProperty("found", out var missingServiceFound) ||
    missingServiceFound.GetBoolean())
{
    throw new InvalidOperationException("service get must return found=false for a missing service.");
}

var bitsResult = await EnsureSuccess(byName["talvora_service_get"], new()
{
    ["serviceName"] = "BITS",
});
if (bitsResult.StructuredContent is not { } bitsJson ||
    !bitsJson.TryGetProperty("found", out var bitsFound) ||
    !bitsFound.GetBoolean() ||
    !bitsJson.TryGetProperty("service", out var bitsService) ||
    !bitsService.TryGetProperty("status", out var bitsStatusJson) ||
    !bitsService.TryGetProperty("startType", out var bitsStartTypeJson))
{
    throw new InvalidOperationException("BITS service is required for service control smoke.");
}

var bitsInitialStatus = bitsStatusJson.GetString();
var bitsStartType = bitsStartTypeJson.GetString();
if (!string.Equals(bitsStartType, "Disabled", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        if (string.Equals(bitsInitialStatus, "Running", StringComparison.OrdinalIgnoreCase))
        {
            var stopped = await EnsureSuccess(byName["talvora_service_stop"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
            if (stopped.StructuredContent is not { } stoppedJson ||
                !stoppedJson.TryGetProperty("afterStatus", out var stoppedStatus) ||
                !string.Equals(stoppedStatus.GetString(), "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("service stop did not stop BITS.");
            }

            var started = await EnsureSuccess(byName["talvora_service_start"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
            if (started.StructuredContent is not { } startedJson ||
                !startedJson.TryGetProperty("afterStatus", out var startedStatus) ||
                !string.Equals(startedStatus.GetString(), "Running", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("service start did not restart BITS.");
            }
        }
        else if (string.Equals(bitsInitialStatus, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            var started = await EnsureSuccess(byName["talvora_service_start"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
            if (started.StructuredContent is not { } startedJson ||
                !startedJson.TryGetProperty("afterStatus", out var startedStatus) ||
                !string.Equals(startedStatus.GetString(), "Running", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("service start did not start BITS.");
            }

            var stopped = await EnsureSuccess(byName["talvora_service_stop"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
            if (stopped.StructuredContent is not { } stoppedJson ||
                !stoppedJson.TryGetProperty("afterStatus", out var stoppedStatus) ||
                !string.Equals(stoppedStatus.GetString(), "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("service stop did not restore BITS.");
            }
        }

        var restarted = await EnsureSuccess(byName["talvora_service_restart"], new()
        {
            ["serviceName"] = "BITS",
            ["timeoutSeconds"] = 30,
        });
        if (restarted.StructuredContent is not { } restartedJson ||
            !restartedJson.TryGetProperty("afterStatus", out var restartedStatus) ||
            !string.Equals(restartedStatus.GetString(), "Running", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("service restart did not leave BITS running.");
        }
    }
    finally
    {
        var currentBits = await EnsureSuccess(byName["talvora_service_get"], new() { ["serviceName"] = "BITS" });
        var currentStatus = currentBits.StructuredContent!.Value.GetProperty("service").GetProperty("status").GetString();

        if (string.Equals(bitsInitialStatus, "Stopped", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentStatus, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSuccess(byName["talvora_service_stop"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
        }
        else if (string.Equals(bitsInitialStatus, "Running", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(currentStatus, "Running", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSuccess(byName["talvora_service_start"], new()
            {
                ["serviceName"] = "BITS",
                ["timeoutSeconds"] = 30,
            });
        }
    }
}

var smokeId = Guid.NewGuid().ToString("N");
var publicDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
if (string.IsNullOrWhiteSpace(publicDocuments))
{
    publicDocuments = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora", "SmokeDocuments");
}
var root = Path.Combine(publicDocuments, "Talvora-Smoke-" + smokeId);
var file = Path.Combine(root, "hello.txt");
var searchToken = "talvora-search-" + smokeId;
try
{
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = file,
        ["content"] = searchToken,
    });

    await EnsureSuccess(byName["talvora_read_text"], new() { ["path"] = file });
    await EnsureSuccess(byName["talvora_list"], new() { ["path"] = root });
    await EnsureSuccess(byName["talvora_run_process"], new()
    {
        ["executable"] = "cmd.exe",
        ["arguments"] = new[] { "/d", "/c", "echo", "talvora-smoke" },
        ["timeoutSeconds"] = 30,
    });

    var powerShellRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Talvora",
        "PowerShell Smoke " + smokeId);
    Directory.CreateDirectory(powerShellRoot);
    try
    {
        var powerShellResult = await EnsureSuccess(byName["talvora_run_powershell"], new()
        {
            ["script"] = """
                $ErrorActionPreference = 'Stop'
                [Console]::Out.WriteLine('talvora-ps-stdout')
                [Console]::Error.WriteLine('talvora-ps-stderr')
                [Console]::Out.WriteLine((Get-Location).Path)
                exit 7
                """,
            ["engine"] = "auto",
            ["workingDirectory"] = powerShellRoot,
            ["timeoutSeconds"] = 30,
        });
        if (powerShellResult.StructuredContent is not { } psJson ||
            !psJson.TryGetProperty("exitCode", out var psExit) ||
            psExit.GetInt32() != 7 ||
            !psJson.TryGetProperty("timedOut", out var psTimedOut) ||
            psTimedOut.GetBoolean() ||
            !psJson.TryGetProperty("standardOutput", out var psStdout) ||
            !psStdout.GetString()!.Contains("talvora-ps-stdout", StringComparison.Ordinal) ||
            !psStdout.GetString()!.Contains(powerShellRoot, StringComparison.OrdinalIgnoreCase) ||
            !psJson.TryGetProperty("standardError", out var psStderr) ||
            !psStderr.GetString()!.Contains("talvora-ps-stderr", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PowerShell tool did not preserve multiline script, working directory, stdout/stderr, and exit code.");
        }
    }
    finally
    {
        if (Directory.Exists(powerShellRoot)) Directory.Delete(powerShellRoot, recursive: true);
    }

    using var searchDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var searchResult = await EnsureSuccess(
        byName["search"],
        new() { ["query"] = searchToken },
        searchDeadline.Token);
    if (searchResult.StructuredContent is not { } searchJson)
        throw new InvalidOperationException("search did not return structured content.");
    if (!searchJson.TryGetProperty("results", out var results) || results.ValueKind != System.Text.Json.JsonValueKind.Array)
        throw new InvalidOperationException("search did not return a results array.");

    var found = results.EnumerateArray().Any(item =>
        item.TryGetProperty("id", out var id) &&
        string.Equals(id.GetString(), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
    if (!found) throw new InvalidOperationException("search did not return the smoke document.");

    var fetchResult = await EnsureSuccess(byName["fetch"], new() { ["id"] = Path.GetFullPath(file) });
    if (fetchResult.StructuredContent is not { } fetchJson)
        throw new InvalidOperationException("fetch did not return structured content.");
    if (!fetchJson.TryGetProperty("text", out var text) || !string.Equals(text.GetString(), searchToken, StringComparison.Ordinal))
        throw new InvalidOperationException("fetch did not return the full smoke document content.");

    await EnsureSuccess(byName["talvora_delete"], new() { ["path"] = file });
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

var registryPath = $"Software\\Talvora\\Smoke\\{smokeId}";
try
{
    await EnsureSuccess(byName["talvora_registry_create_key"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["view"] = "default",
    });
    foreach (var child in new[] { "Zulu", "alpha", "Beta" })
    {
        await EnsureSuccess(byName["talvora_registry_create_key"], new()
        {
            ["hive"] = "HKCU",
            ["path"] = registryPath + "\\" + child,
            ["view"] = "default",
        });
    }

    await EnsureSuccess(byName["talvora_registry_set"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "TextValue",
        ["kind"] = "String",
        ["text"] = "talvora-registry-" + smokeId,
        ["view"] = "default",
    });
    await EnsureSuccess(byName["talvora_registry_set"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "NumberValue",
        ["kind"] = "DWord",
        ["number"] = 424242,
        ["view"] = "default",
    });
    await EnsureSuccess(byName["talvora_registry_set"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "MultiValue",
        ["kind"] = "MultiString",
        ["strings"] = new[] { "alpha", "beta" },
        ["view"] = "default",
    });

    var getResult = await EnsureSuccess(byName["talvora_registry_get"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "TextValue",
        ["view"] = "default",
    });
    if (getResult.StructuredContent is not { } getJson ||
        !getJson.TryGetProperty("found", out var getFound) ||
        !getFound.GetBoolean() ||
        !getJson.TryGetProperty("value", out var valueJson) ||
        !valueJson.TryGetProperty("text", out var registryText) ||
        !string.Equals(registryText.GetString(), "talvora-registry-" + smokeId, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("registry get did not return the expected string value.");
    }

    var listResult = await EnsureSuccess(byName["talvora_registry_list"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["view"] = "default",
    });
    if (listResult.StructuredContent is not { } listJson ||
        !listJson.TryGetProperty("subKeys", out var subKeys) ||
        subKeys.ValueKind != System.Text.Json.JsonValueKind.Array)
    {
        throw new InvalidOperationException("registry list did not return subKeys.");
    }
    var subKeyNames = subKeys.EnumerateArray().Select(item => item.GetString()).ToArray();
    if (!subKeyNames.SequenceEqual(new[] { "Beta", "Zulu", "alpha" }, StringComparer.Ordinal))
    {
        throw new InvalidOperationException("registry subkeys were not returned in deterministic ordinal order.");
    }

    await EnsureSuccess(byName["talvora_registry_delete_value"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "TextValue",
        ["view"] = "default",
    });
    var missingResult = await EnsureSuccess(byName["talvora_registry_get"], new()
    {
        ["hive"] = "HKCU",
        ["path"] = registryPath,
        ["valueName"] = "TextValue",
        ["view"] = "default",
    });
    if (missingResult.StructuredContent is not { } missingJson ||
        !missingJson.TryGetProperty("found", out var missingFound) ||
        missingFound.GetBoolean())
    {
        throw new InvalidOperationException("registry delete value did not remove the value.");
    }
}
finally
{
    if (byName.TryGetValue("talvora_registry_delete_key", out var deleteKeyTool))
    {
        await EnsureSuccess(deleteKeyTool, new()
        {
            ["hive"] = "HKCU",
            ["path"] = registryPath,
            ["recursive"] = true,
            ["view"] = "default",
        });
    }
}

Console.WriteLine("TALVORA MCP SMOKE GREEN");
Console.WriteLine($"endpoint={endpoint}");
Console.WriteLine($"tools={string.Join(',', required)}");
