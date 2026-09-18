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
    "talvora_create_directory",
    "talvora_copy",
    "talvora_move",
    "talvora_env_get",
    "talvora_env_list",
    "talvora_env_set",
    "talvora_env_delete",
    "talvora_eventlog_list",
    "talvora_eventlog_query",
    "talvora_run_process",
    "talvora_run_powershell",
    "talvora_process_list",
    "talvora_process_get",
    "talvora_process_kill",
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

static async Task<CallToolResult> EnsureError(
    McpClientTool tool,
    Dictionary<string, object?> arguments,
    CancellationToken cancellationToken = default)
{
    var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
    if (result.IsError is not true)
    {
        throw new InvalidOperationException($"Tool unexpectedly succeeded: {tool.Name}");
    }
    return result;
}

static async Task<string> ReadToolText(
    McpClientTool tool,
    string path,
    CancellationToken cancellationToken = default)
{
    var result = await EnsureSuccess(
        tool,
        new Dictionary<string, object?> { ["path"] = path },
        cancellationToken);
    return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
        ?? throw new InvalidOperationException($"Tool did not return text content: {tool.Name}");
}

await EnsureSuccess(byName["talvora_system_info"], []);

var eventLogListResult = await EnsureSuccess(byName["talvora_eventlog_list"], new()
{
    ["query"] = "System",
});
if (eventLogListResult.StructuredContent is not { } eventLogListJson ||
    !eventLogListJson.TryGetProperty("logs", out var eventLogs) ||
    eventLogs.ValueKind != System.Text.Json.JsonValueKind.Array ||
    !eventLogs.EnumerateArray().Any(item =>
        item.TryGetProperty("logName", out var logName) &&
        string.Equals(logName.GetString(), "System", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("event log list did not return the System log.");
}

var eventLogQueryResult = await EnsureSuccess(byName["talvora_eventlog_query"], new()
{
    ["logName"] = "System",
    ["xpath"] = "*",
    ["maxEvents"] = 5,
    ["newestFirst"] = true,
});
if (eventLogQueryResult.StructuredContent is not { } eventLogQueryJson ||
    !eventLogQueryJson.TryGetProperty("events", out var eventLogEvents) ||
    eventLogEvents.ValueKind != System.Text.Json.JsonValueKind.Array ||
    eventLogEvents.GetArrayLength() == 0 ||
    eventLogEvents.GetArrayLength() > 5)
{
    throw new InvalidOperationException("event log query did not return a bounded System event result.");
}
foreach (var eventItem in eventLogEvents.EnumerateArray())
{
    if (!eventItem.TryGetProperty("logName", out var eventItemLogName) ||
        !string.Equals(eventItemLogName.GetString(), "System", StringComparison.OrdinalIgnoreCase) ||
        !eventItem.TryGetProperty("id", out _) ||
        !eventItem.TryGetProperty("recordId", out _))
    {
        throw new InvalidOperationException("event log query returned an incomplete structured event.");
    }
}

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

if (!eventLogService.TryGetProperty("displayName", out var eventLogDisplayNameJson) ||
    string.IsNullOrWhiteSpace(eventLogDisplayNameJson.GetString()))
{
    throw new InvalidOperationException("EventLog service did not expose a display name.");
}

var eventLogByDisplayName = await EnsureSuccess(byName["talvora_service_get"], new()
{
    ["serviceName"] = eventLogDisplayNameJson.GetString()!,
});
if (eventLogByDisplayName.StructuredContent is not { } eventLogByDisplayJson ||
    !eventLogByDisplayJson.TryGetProperty("found", out var eventLogByDisplayFound) ||
    !eventLogByDisplayFound.GetBoolean() ||
    !eventLogByDisplayJson.TryGetProperty("service", out var eventLogByDisplayService) ||
    !eventLogByDisplayService.TryGetProperty("serviceName", out var eventLogByDisplayServiceName) ||
    !string.Equals(eventLogByDisplayServiceName.GetString(), "EventLog", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("service get by display name did not return EventLog.");
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
var processEnvironmentName = "TALVORA_SMOKE_PROCESS_" + smokeId.ToUpperInvariant();
var userEnvironmentName = "TALVORA_SMOKE_USER_" + smokeId.ToUpperInvariant();
var machineEnvironmentName = "TALVORA_SMOKE_MACHINE_" + smokeId.ToUpperInvariant();
try
{
    foreach (var item in new[]
    {
        (Name: processEnvironmentName, Target: "process"),
        (Name: userEnvironmentName, Target: "user"),
        (Name: machineEnvironmentName, Target: "machine"),
    })
    {
        await EnsureSuccess(byName["talvora_env_delete"], new()
        {
            ["name"] = item.Name,
            ["target"] = item.Target,
        });
    }

    await EnsureSuccess(byName["talvora_env_set"], new()
    {
        ["name"] = processEnvironmentName,
        ["value"] = "process-" + smokeId,
        ["target"] = "process",
    });
    var processEnvironmentGet = await EnsureSuccess(byName["talvora_env_get"], new()
    {
        ["name"] = processEnvironmentName,
        ["target"] = "process",
    });
    if (processEnvironmentGet.StructuredContent is not { } processEnvironmentJson ||
        !processEnvironmentJson.TryGetProperty("found", out var processEnvironmentFound) ||
        !processEnvironmentFound.GetBoolean() ||
        !processEnvironmentJson.TryGetProperty("value", out var processEnvironmentValue) ||
        !string.Equals(processEnvironmentValue.GetString(), "process-" + smokeId, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("environment get did not return the process value.");
    }

    var processEnvironmentList = await EnsureSuccess(byName["talvora_env_list"], new()
    {
        ["target"] = "process",
        ["query"] = processEnvironmentName,
    });
    if (processEnvironmentList.StructuredContent is not { } processEnvironmentListJson ||
        !processEnvironmentListJson.TryGetProperty("variables", out var processEnvironmentVariables) ||
        processEnvironmentVariables.ValueKind != System.Text.Json.JsonValueKind.Array ||
        !processEnvironmentVariables.EnumerateArray().Any(item =>
            item.TryGetProperty("name", out var environmentName) &&
            string.Equals(environmentName.GetString(), processEnvironmentName, StringComparison.OrdinalIgnoreCase) &&
            item.TryGetProperty("value", out var environmentValue) &&
            string.Equals(environmentValue.GetString(), "process-" + smokeId, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("environment list did not return the process value.");
    }

    await EnsureSuccess(byName["talvora_env_set"], new()
    {
        ["name"] = processEnvironmentName,
        ["value"] = "",
        ["target"] = "process",
    });
    var emptyEnvironmentGet = await EnsureSuccess(byName["talvora_env_get"], new()
    {
        ["name"] = processEnvironmentName,
        ["target"] = "process",
    });
    if (emptyEnvironmentGet.StructuredContent is not { } emptyEnvironmentJson ||
        !emptyEnvironmentJson.TryGetProperty("found", out var emptyEnvironmentFound) ||
        !emptyEnvironmentFound.GetBoolean() ||
        !emptyEnvironmentJson.TryGetProperty("value", out var emptyEnvironmentValue) ||
        emptyEnvironmentValue.GetString() != string.Empty)
    {
        throw new InvalidOperationException("environment set must preserve an explicit empty string value.");
    }

    foreach (var item in new[]
    {
        (Name: userEnvironmentName, Target: "user", Value: "user-" + smokeId),
        (Name: machineEnvironmentName, Target: "machine", Value: "machine-" + smokeId),
    })
    {
        await EnsureSuccess(byName["talvora_env_set"], new()
        {
            ["name"] = item.Name,
            ["value"] = item.Value,
            ["target"] = item.Target,
        });
        var persistedEnvironmentGet = await EnsureSuccess(byName["talvora_env_get"], new()
        {
            ["name"] = item.Name,
            ["target"] = item.Target,
        });
        if (persistedEnvironmentGet.StructuredContent is not { } persistedEnvironmentJson ||
            !persistedEnvironmentJson.TryGetProperty("found", out var persistedEnvironmentFound) ||
            !persistedEnvironmentFound.GetBoolean() ||
            !persistedEnvironmentJson.TryGetProperty("value", out var persistedEnvironmentValue) ||
            !string.Equals(persistedEnvironmentValue.GetString(), item.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"environment get did not return the {item.Target} value.");
        }
    }

    var firstDelete = await EnsureSuccess(byName["talvora_env_delete"], new()
    {
        ["name"] = processEnvironmentName,
        ["target"] = "process",
    });
    if (firstDelete.StructuredContent is not { } firstDeleteJson ||
        !firstDeleteJson.TryGetProperty("deleted", out var firstDeleted) ||
        !firstDeleted.GetBoolean())
    {
        throw new InvalidOperationException("environment delete did not report removal of an existing value.");
    }

    var missingEnvironment = await EnsureSuccess(byName["talvora_env_get"], new()
    {
        ["name"] = processEnvironmentName,
        ["target"] = "process",
    });
    if (missingEnvironment.StructuredContent is not { } missingEnvironmentJson ||
        !missingEnvironmentJson.TryGetProperty("found", out var missingEnvironmentFound) ||
        missingEnvironmentFound.GetBoolean())
    {
        throw new InvalidOperationException("environment get must return found=false after deletion.");
    }

    var secondDelete = await EnsureSuccess(byName["talvora_env_delete"], new()
    {
        ["name"] = processEnvironmentName,
        ["target"] = "process",
    });
    if (secondDelete.StructuredContent is not { } secondDeleteJson ||
        !secondDeleteJson.TryGetProperty("deleted", out var secondDeleted) ||
        secondDeleted.GetBoolean())
    {
        throw new InvalidOperationException("environment delete must be idempotent for a missing value.");
    }
}
finally
{
    foreach (var item in new[]
    {
        (Name: processEnvironmentName, Target: "process"),
        (Name: userEnvironmentName, Target: "user"),
        (Name: machineEnvironmentName, Target: "machine"),
    })
    {
        if (byName.TryGetValue("talvora_env_delete", out var deleteEnvironmentTool))
        {
            await EnsureSuccess(deleteEnvironmentTool, new()
            {
                ["name"] = item.Name,
                ["target"] = item.Target,
            });
        }
    }
}

var publicDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
if (string.IsNullOrWhiteSpace(publicDocuments))
{
    publicDocuments = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora", "SmokeDocuments");
}
var root = Path.Combine(publicDocuments, "Talvora-Smoke-" + smokeId);
var file = Path.Combine(root, "hello.txt");
var searchToken = "talvora-search-" + smokeId;
var nestedDirectory = Path.Combine(root, "created", "nested", "leaf");
var copySourceFile = Path.Combine(root, "copy-source.txt");
var copyDestinationFile = Path.Combine(root, "copy-destination.txt");
var copyCollisionFile = Path.Combine(root, "copy-collision.txt");
var directorySource = Path.Combine(root, "directory-source");
var directorySourceNested = Path.Combine(directorySource, "child", "grandchild");
var directoryDestination = Path.Combine(root, "directory-destination");
var directoryNonRecursiveDestination = Path.Combine(root, "directory-nonrecursive");
var reparseSource = Path.Combine(root, "reparse-source");
var reparseLoop = Path.Combine(reparseSource, "loop");
var reparseDestination = Path.Combine(root, "reparse-destination");
var moveFileSource = Path.Combine(root, "move-file-source.txt");
var moveFileDestination = Path.Combine(root, "move-file-destination.txt");
var moveDirectorySource = Path.Combine(root, "move-directory-source");
var moveDirectoryDestination = Path.Combine(root, "move-directory-destination");
var moveCollisionSource = Path.Combine(root, "move-collision-source.txt");
var moveCollisionDestination = Path.Combine(root, "move-collision-destination.txt");
try
{
    var createdDirectory = await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = nestedDirectory,
    });
    if (createdDirectory.StructuredContent is not { } createdDirectoryJson ||
        !createdDirectoryJson.TryGetProperty("path", out var createdPath) ||
        !string.Equals(createdPath.GetString(), Path.GetFullPath(nestedDirectory), StringComparison.OrdinalIgnoreCase) ||
        !createdDirectoryJson.TryGetProperty("created", out var createdChanged) ||
        !createdChanged.GetBoolean())
    {
        throw new InvalidOperationException("create_directory did not report the first nested directory creation.");
    }

    var createAgain = await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = nestedDirectory,
    });
    if (createAgain.StructuredContent is not { } createAgainJson ||
        !createAgainJson.TryGetProperty("created", out var createAgainChanged) ||
        createAgainChanged.GetBoolean())
    {
        throw new InvalidOperationException("create_directory must be idempotent for an existing directory.");
    }
    await EnsureSuccess(byName["talvora_list"], new() { ["path"] = nestedDirectory });

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = copySourceFile,
        ["content"] = "copy-source-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_copy"], new()
    {
        ["source"] = copySourceFile,
        ["destination"] = copyDestinationFile,
    });
    if (!string.Equals(
        await ReadToolText(byName["talvora_read_text"], copyDestinationFile),
        "copy-source-" + smokeId,
        StringComparison.Ordinal))
    {
        throw new InvalidOperationException("copy did not preserve file content.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = copyCollisionFile,
        ["content"] = "copy-old-" + smokeId,
    });
    await EnsureError(byName["talvora_copy"], new()
    {
        ["source"] = copySourceFile,
        ["destination"] = copyCollisionFile,
        ["overwrite"] = false,
    });
    if (!string.Equals(
        await ReadToolText(byName["talvora_read_text"], copyCollisionFile),
        "copy-old-" + smokeId,
        StringComparison.Ordinal))
    {
        throw new InvalidOperationException("copy overwrite=false modified an existing destination.");
    }

    await EnsureSuccess(byName["talvora_copy"], new()
    {
        ["source"] = copySourceFile,
        ["destination"] = copyCollisionFile,
        ["overwrite"] = true,
    });
    if (!string.Equals(
        await ReadToolText(byName["talvora_read_text"], copyCollisionFile),
        "copy-source-" + smokeId,
        StringComparison.Ordinal))
    {
        throw new InvalidOperationException("copy overwrite=true did not replace the destination file.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = directorySourceNested,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(directorySource, "root.txt"),
        ["content"] = "directory-root-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(directorySourceNested, "nested.txt"),
        ["content"] = "directory-nested-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_copy"], new()
    {
        ["source"] = directorySource,
        ["destination"] = directoryDestination,
        ["recursive"] = true,
    });
    if (!string.Equals(
        await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "child", "grandchild", "nested.txt")),
        "directory-nested-" + smokeId,
        StringComparison.Ordinal))
    {
        throw new InvalidOperationException("recursive directory copy did not preserve nested content.");
    }

    await EnsureError(byName["talvora_copy"], new()
    {
        ["source"] = directorySource,
        ["destination"] = directoryNonRecursiveDestination,
        ["recursive"] = false,
    });
    if (Directory.Exists(directoryNonRecursiveDestination))
    {
        throw new InvalidOperationException("recursive=false must fail before creating a partial directory copy.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(directoryDestination, "child", "grandchild", "nested.txt"),
        ["content"] = "directory-stale-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(directoryDestination, "keep.txt"),
        ["content"] = "directory-keep-" + smokeId,
    });
    await EnsureError(byName["talvora_copy"], new()
    {
        ["source"] = directorySource,
        ["destination"] = directoryDestination,
        ["overwrite"] = false,
        ["recursive"] = true,
    });
    await EnsureSuccess(byName["talvora_copy"], new()
    {
        ["source"] = directorySource,
        ["destination"] = directoryDestination,
        ["overwrite"] = true,
        ["recursive"] = true,
    });
    if (!string.Equals(
        await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "child", "grandchild", "nested.txt")),
        "directory-nested-" + smokeId,
        StringComparison.Ordinal) ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "keep.txt")),
            "directory-keep-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("directory overwrite=true must replace conflicting entries while merging non-conflicting destination entries.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = reparseSource,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(reparseSource, "payload.txt"),
        ["content"] = "reparse-payload-" + smokeId,
    });
    var reparsePathBase64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(reparseLoop));
    var reparseTargetBase64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(reparseSource));
    await EnsureSuccess(byName["talvora_run_powershell"], new()
    {
        ["script"] = $"""
            $link = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{reparsePathBase64}'))
            $target = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{reparseTargetBase64}'))
            New-Item -ItemType Junction -Path $link -Target $target -Force | Out-Null
            """,
        ["engine"] = "auto",
        ["timeoutSeconds"] = 30,
    });
    await EnsureError(byName["talvora_copy"], new()
    {
        ["source"] = reparseSource,
        ["destination"] = reparseDestination,
        ["overwrite"] = true,
        ["recursive"] = true,
    });
    if (Directory.Exists(reparseDestination))
    {
        throw new InvalidOperationException("reparse-point rejection must occur before a partial destination tree is created.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = moveFileSource,
        ["content"] = "move-file-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_move"], new()
    {
        ["source"] = moveFileSource,
        ["destination"] = moveFileDestination,
    });
    if (File.Exists(moveFileSource) ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], moveFileDestination),
            "move-file-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("move did not relocate the source file.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = Path.Combine(moveDirectorySource, "nested"),
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(moveDirectorySource, "nested", "moved.txt"),
        ["content"] = "move-directory-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_move"], new()
    {
        ["source"] = moveDirectorySource,
        ["destination"] = moveDirectoryDestination,
    });
    if (Directory.Exists(moveDirectorySource) ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], Path.Combine(moveDirectoryDestination, "nested", "moved.txt")),
            "move-directory-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("move did not relocate the source directory.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = moveCollisionSource,
        ["content"] = "move-new-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = moveCollisionDestination,
        ["content"] = "move-old-" + smokeId,
    });
    await EnsureError(byName["talvora_move"], new()
    {
        ["source"] = moveCollisionSource,
        ["destination"] = moveCollisionDestination,
        ["overwrite"] = false,
    });
    if (!File.Exists(moveCollisionSource) ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], moveCollisionDestination),
            "move-old-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("move overwrite=false changed source or destination on collision.");
    }

    await EnsureSuccess(byName["talvora_move"], new()
    {
        ["source"] = moveCollisionSource,
        ["destination"] = moveCollisionDestination,
        ["overwrite"] = true,
    });
    if (File.Exists(moveCollisionSource) ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], moveCollisionDestination),
            "move-new-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("move overwrite=true did not replace the existing destination.");
    }
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

    int? processSmokePid = null;
    try
    {
        var processStart = await EnsureSuccess(byName["talvora_run_powershell"], new()
        {
            ["script"] = """
                $exe = Join-Path $env:SystemRoot 'System32\cmd.exe'
                $p = Start-Process -FilePath $exe -ArgumentList @('/d','/c','ping -t 127.0.0.1 >NUL') -WindowStyle Hidden -PassThru
                [Console]::Out.Write($p.Id)
                """,
            ["engine"] = "auto",
            ["timeoutSeconds"] = 30,
        });
        if (processStart.StructuredContent is not { } processStartJson ||
            !processStartJson.TryGetProperty("standardOutput", out var processStartOutput) ||
            !int.TryParse(processStartOutput.GetString()?.Trim(), out var processChildPid))
        {
            throw new InvalidOperationException("failed to spawn process smoke child.");
        }
        processSmokePid = processChildPid;

        var processGet = await EnsureSuccess(byName["talvora_process_get"], new()
        {
            ["processId"] = processChildPid,
        });
        if (processGet.StructuredContent is not { } processGetJson ||
            !processGetJson.TryGetProperty("found", out var processFound) ||
            !processFound.GetBoolean() ||
            !processGetJson.TryGetProperty("process", out var processInfo) ||
            !processInfo.TryGetProperty("processId", out var returnedPid) ||
            returnedPid.GetInt32() != processChildPid)
        {
            throw new InvalidOperationException("process get did not return the spawned process.");
        }

        var processList = await EnsureSuccess(byName["talvora_process_list"], new()
        {
            ["query"] = "cmd",
        });
        if (processList.StructuredContent is not { } processListJson ||
            !processListJson.TryGetProperty("processes", out var processes) ||
            processes.ValueKind != System.Text.Json.JsonValueKind.Array ||
            !processes.EnumerateArray().Any(item =>
                item.TryGetProperty("processId", out var listedPid) &&
                listedPid.GetInt32() == processChildPid))
        {
            throw new InvalidOperationException("process list did not return the spawned process.");
        }

        var processKill = await EnsureSuccess(byName["talvora_process_kill"], new()
        {
            ["processId"] = processChildPid,
            ["entireProcessTree"] = true,
            ["timeoutSeconds"] = 30,
        });
        if (processKill.StructuredContent is not { } processKillJson ||
            !processKillJson.TryGetProperty("found", out var killFound) ||
            !killFound.GetBoolean() ||
            !processKillJson.TryGetProperty("exited", out var exited) ||
            !exited.GetBoolean())
        {
            throw new InvalidOperationException("process kill did not terminate the spawned process.");
        }

        var processMissing = await EnsureSuccess(byName["talvora_process_get"], new()
        {
            ["processId"] = processChildPid,
        });
        if (processMissing.StructuredContent is not { } processMissingJson ||
            !processMissingJson.TryGetProperty("found", out var processMissingFound) ||
            processMissingFound.GetBoolean())
        {
            throw new InvalidOperationException("process get must return found=false after process termination.");
        }

        processSmokePid = null;
    }
    finally
    {
        if (processSmokePid is int leakedPid)
        {
            await EnsureSuccess(byName["talvora_run_powershell"], new()
            {
                ["script"] = $"Stop-Process -Id {leakedPid} -Force -ErrorAction SilentlyContinue",
                ["engine"] = "auto",
                ["timeoutSeconds"] = 30,
            });
        }
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
    if (Directory.Exists(reparseLoop)) Directory.Delete(reparseLoop);
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
