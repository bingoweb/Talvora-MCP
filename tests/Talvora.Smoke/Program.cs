using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

if (args.Length >= 3 &&
    string.Equals(
        args[0],
        "--dev-server-fixture",
        StringComparison.Ordinal))
{
    if (!int.TryParse(
            args[1],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var fixturePort))
    {
        throw new ArgumentException("Invalid dev-server fixture port.");
    }

    await RunDevServerFixtureAsync(
        fixturePort,
        args[2]);
    return;
}

var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:7676/mcp";
var repositoryPath = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
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
        "talvora_path_info",
        "talvora_file_hash",
        "talvora_find_files",
        "talvora_search_text",
        "talvora_read_bytes",
        "talvora_write_bytes",
        "talvora_replace_text",
        "talvora_http_request",
        "talvora_tcp_connections",
        "talvora_tcp_listeners",
        "talvora_wait_tcp",
        "talvora_project_discover",
        "talvora_resolve_command",
    "talvora_job_start",
    "talvora_job_get",
    "talvora_job_list",
    "talvora_job_read_output",
    "talvora_job_write_stdin",
    "talvora_job_stop",
    "talvora_job_delete",
    "talvora_git_info",
    "talvora_git_status",
    "talvora_git_diff",
    "talvora_git_log",
    "talvora_git_branches",
    "talvora_git_run",
    "talvora_read_text_range",
    "talvora_tail_text",
    "talvora_append_text",
    "talvora_json_get",
    "talvora_json_set",
    "talvora_json_delete",
    "talvora_archive_list",
    "talvora_archive_create",
    "talvora_archive_extract",
    "talvora_http_download",
    "talvora_watch_start",
    "talvora_watch_get",
    "talvora_watch_list",
    "talvora_watch_read",
    "talvora_watch_wait",
    "talvora_watch_stop",
    "talvora_choco_info",
    "talvora_choco_list",
    "talvora_choco_search",
    "talvora_choco_install",
    "talvora_choco_upgrade",
    "talvora_choco_uninstall",
    "talvora_choco_run",
    "talvora_dotnet_info",
    "talvora_dotnet_restore",
    "talvora_dotnet_build",
    "talvora_dotnet_test",
    "talvora_dotnet_publish",
    "talvora_dotnet_run",
    "talvora_node_info",
    "talvora_npm_install",
    "talvora_npm_ci",
    "talvora_npm_run_script",
    "talvora_npm_run",
    "talvora_session_list",
    "talvora_session_get",
    "talvora_user_process_start",
    "talvora_python_info",
    "talvora_python_run",
    "talvora_python_venv_create",
    "talvora_pip_install",
    "talvora_pip_run",
    "talvora_docker_info",
    "talvora_docker_ps",
    "talvora_docker_images",
    "talvora_docker_logs",
    "talvora_docker_exec",
    "talvora_docker_run",
    "talvora_docker_compose_run",
    "talvora_network_interfaces",
    "talvora_dns_lookup",
    "talvora_ping",
    "talvora_tcp_exchange",
    "talvora_tls_inspect",
    "talvora_websocket_exchange",
    "talvora_dotenv_list",
    "talvora_dotenv_get",
    "talvora_dotenv_set",
    "talvora_dotenv_delete",
    "talvora_ini_list",
    "talvora_ini_get",
    "talvora_ini_set",
    "talvora_ini_delete",
    "talvora_xml_query",
    "talvora_xml_set",
    "talvora_xml_delete",
    "talvora_test_report_summary",
    "talvora_windows_toolchain_info",
    "talvora_vs_instances",
    "talvora_windows_sdk_list",
    "talvora_vsdev_environment",
    "talvora_visual_studio_instances",
    "talvora_vs_dev_environment",
    "talvora_msbuild_info",
    "talvora_msbuild_run",
    "talvora_windows_sdk_info",
    "talvora_cmake_info",
    "talvora_cmake_run",
    "talvora_ninja_info",
    "talvora_ninja_run",
    "talvora_pe_info",
    "talvora_file_version_info",
    "talvora_http_mock_start",
    "talvora_http_mock_get",
    "talvora_http_mock_list",
    "talvora_http_mock_read",
    "talvora_http_mock_reply",
    "talvora_http_mock_stop",
    "talvora_sqlite_info",
    "talvora_sqlite_query",
    "talvora_sqlite_execute",
    "talvora_sqlite_schema",
    "talvora_sqlite_backup",
    "talvora_dev_server_start",
    "talvora_dev_server_get",
    "talvora_dev_server_list",
    "talvora_dev_server_wait",
    "talvora_dev_server_stop",
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

static int GetFreeLoopbackTcpPort()
{
    var listener = new System.Net.Sockets.TcpListener(
        System.Net.IPAddress.Loopback,
        0);
    listener.Start();
    try
    {
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}

static async Task RunDevServerFixtureAsync(
    int port,
    string body)
{
    var listener =
        new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback,
            port);

    listener.Start();

    Console.WriteLine("READY");
    await Console.Out.FlushAsync();

    var bodyBytes =
        System.Text.Encoding.UTF8.GetBytes(body);

    try
    {
        while (true)
        {
            using var client =
                await listener.AcceptTcpClientAsync();
            using var stream =
                client.GetStream();

            try
            {
                var requestBuffer =
                    new byte[4096];
                var bytesRead =
                    await stream.ReadAsync(
                        requestBuffer);

                if (bytesRead == 0)
                {
                    continue;
                }

                var headerText =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/plain; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n";
                var headerBytes =
                    System.Text.Encoding.ASCII.GetBytes(
                        headerText);

                await stream.WriteAsync(
                    headerBytes);
                await stream.WriteAsync(
                    bodyBytes);
                await stream.FlushAsync();
            }
            catch (IOException)
            {
            }
            catch (System.Net.Sockets.SocketException)
            {
            }
        }
    }
    finally
    {
        listener.Stop();
    }
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
var developerBinaryFile = Path.Combine(root, "developer-bytes.bin");
var developerPatchFile = Path.Combine(root, "developer-patch.txt");
var developerProjectRoot = Path.Combine(root, "developer-project");
var developerPackageJson = Path.Combine(developerProjectRoot, "package.json");
var interactiveSessionMarker = Path.Combine(root, "interactive-session-" + smokeId + ".json");
var developerWatchFile = Path.Combine(root, "watch-" + smokeId + ".txt");
var dotnetProjectRoot = Path.Combine(root, "dotnet-smoke");
var dotnetProjectFile = Path.Combine(dotnetProjectRoot, "Talvora.Dotnet.Smoke.csproj");
var dotnetProgramFile = Path.Combine(dotnetProjectRoot, "Program.cs");
var dotnetOutputDll = Path.Combine(dotnetProjectRoot, "bin", "Release", "net10.0", "Talvora.Dotnet.Smoke.dll");
var pythonVenvRoot = Path.Combine(root, "python-venv");

var developerRangeFile = Path.Combine(root, "developer-range.txt");
var developerJsonFile = Path.Combine(root, "developer-config.json");
var developerDotenvFile = Path.Combine(root, ".env");
var developerIniFile = Path.Combine(root, "developer.ini");
var developerXmlFile = Path.Combine(root, "developer.xml");
var developerJUnitFile = Path.Combine(root, "junit.xml");
var developerArchiveSource = Path.Combine(root, "developer-archive-source");
var developerArchiveZip = Path.Combine(root, "developer-assets.zip");
var developerArchiveExtract = Path.Combine(root, "developer-archive-extract");
var developerDownloadFile = Path.Combine(root, "developer-health-download.json");
const string knowledgeRootsEnvironmentName = "TALVORA_KNOWLEDGE_ROOTS";
var knowledgeRootsCaptured = false;
var knowledgeRootsWasPresent = false;
string? knowledgeRootsOriginalValue = null;
try
{
    var knowledgeRootsBefore = await EnsureSuccess(byName["talvora_env_get"], new()
    {
        ["name"] = knowledgeRootsEnvironmentName,
        ["target"] = "process",
    });
    if (knowledgeRootsBefore.StructuredContent is not { } knowledgeRootsBeforeJson ||
        !knowledgeRootsBeforeJson.TryGetProperty("found", out var knowledgeRootsFoundJson))
    {
        throw new InvalidOperationException("environment get did not return the knowledge roots state.");
    }

    knowledgeRootsWasPresent = knowledgeRootsFoundJson.GetBoolean();
    if (knowledgeRootsWasPresent)
    {
        if (!knowledgeRootsBeforeJson.TryGetProperty("value", out var knowledgeRootsValueJson))
        {
            throw new InvalidOperationException("environment get did not return the knowledge roots value.");
        }
        knowledgeRootsOriginalValue = knowledgeRootsValueJson.GetString() ?? string.Empty;
    }
    knowledgeRootsCaptured = true;

    await EnsureSuccess(byName["talvora_env_set"], new()
    {
        ["name"] = knowledgeRootsEnvironmentName,
        ["value"] = root,
        ["target"] = "process",
    });
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

    var pathInfoResult = await EnsureSuccess(byName["talvora_path_info"], new()
    {
        ["path"] = nestedDirectory,
    });
    if (pathInfoResult.StructuredContent is not { } pathInfoJson ||
        !pathInfoJson.TryGetProperty("exists", out var pathInfoExists) ||
        !pathInfoExists.GetBoolean() ||
        !pathInfoJson.TryGetProperty("kind", out var pathInfoKind) ||
        !string.Equals(pathInfoKind.GetString(), "directory", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("path_info did not report the created directory.");
    }

    var sessionListResult = await EnsureSuccess(byName["talvora_session_list"], new());
    if (sessionListResult.StructuredContent is not { } sessionListJson ||
        !sessionListJson.TryGetProperty("sessions", out var sessionsJson) ||
        sessionsJson.ValueKind != System.Text.Json.JsonValueKind.Array)
    {
        throw new InvalidOperationException("session_list did not return a sessions array.");
    }

    var activeSession = sessionsJson
        .EnumerateArray()
        .FirstOrDefault(session =>
            session.TryGetProperty("isActive", out var activeJson) &&
            activeJson.GetBoolean() &&
            session.TryGetProperty("userName", out var userNameJson) &&
            !string.IsNullOrWhiteSpace(userNameJson.GetString()));

    if (activeSession.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        throw new InvalidOperationException("session_list did not return an active logged-on user session.");
    }

    var activeSessionId = activeSession.GetProperty("sessionId").GetInt32();
    var activeSessionUser = activeSession.GetProperty("user").GetString() ?? string.Empty;
    if (activeSessionId <= 0 || string.IsNullOrWhiteSpace(activeSessionUser))
    {
        throw new InvalidOperationException("active session identity is incomplete.");
    }

    var sessionGetResult = await EnsureSuccess(byName["talvora_session_get"], new()
    {
        ["sessionId"] = activeSessionId,
    });
    if (sessionGetResult.StructuredContent is not { } sessionGetJson ||
        !sessionGetJson.GetProperty("found").GetBoolean() ||
        sessionGetJson.GetProperty("session").GetProperty("sessionId").GetInt32() != activeSessionId)
    {
        throw new InvalidOperationException("session_get did not return the selected active session.");
    }

    var markerPathBase64 = Convert.ToBase64String(
        System.Text.Encoding.Unicode.GetBytes(interactiveSessionMarker));
    var sessionProbeScript = $$"""
        $path = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{markerPathBase64}}'))
        $payload = [ordered]@{
            user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
            sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
        }
        [IO.File]::WriteAllText(
            $path,
            ($payload | ConvertTo-Json -Compress),
            [Text.UTF8Encoding]::new($false))
        """;
    var encodedSessionProbe = Convert.ToBase64String(
        System.Text.Encoding.Unicode.GetBytes(sessionProbeScript));
    var interactivePowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    var userProcessResult = await EnsureSuccess(byName["talvora_user_process_start"], new()
    {
        ["executable"] = interactivePowerShell,
        ["arguments"] = new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-EncodedCommand",
            encodedSessionProbe,
        },
        ["sessionId"] = activeSessionId,
        ["workingDirectory"] = root,
        ["visible"] = false,
        ["newConsole"] = false,
    });
    if (userProcessResult.StructuredContent is not { } userProcessJson ||
        userProcessJson.GetProperty("sessionId").GetInt32() != activeSessionId ||
        userProcessJson.GetProperty("processId").GetInt32() <= 0 ||
        !string.Equals(
            userProcessJson.GetProperty("user").GetString(),
            activeSessionUser,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("user_process_start did not target the expected interactive session.");
    }

    var interactiveMarkerFound = false;
    for (var attempt = 0; attempt < 100 && !interactiveMarkerFound; attempt++)
    {
        await Task.Delay(100);
        var markerInfoResult = await EnsureSuccess(byName["talvora_path_info"], new()
        {
            ["path"] = interactiveSessionMarker,
        });

        if (markerInfoResult.StructuredContent is { } markerInfoJson &&
            markerInfoJson.GetProperty("exists").GetBoolean())
        {
            interactiveMarkerFound = true;
        }
    }

    if (!interactiveMarkerFound)
    {
        throw new InvalidOperationException("interactive user process did not create its identity marker.");
    }

    var interactiveMarkerText = await ReadToolText(
        byName["talvora_read_text"],
        interactiveSessionMarker);
    using (var interactiveMarkerDocument = System.Text.Json.JsonDocument.Parse(interactiveMarkerText))
    {
        var markerRoot = interactiveMarkerDocument.RootElement;
        var launchedUser = markerRoot.GetProperty("user").GetString() ?? string.Empty;
        var launchedSessionId = markerRoot.GetProperty("sessionId").GetInt32();

        if (launchedSessionId != activeSessionId ||
            !string.Equals(
                launchedUser,
                activeSessionUser,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"interactive process identity mismatch. Expected={activeSessionUser}/session {activeSessionId}; Actual={launchedUser}/session {launchedSessionId}");
        }
    }

    await EnsureSuccess(byName["talvora_delete"], new()
    {
        ["path"] = interactiveSessionMarker,
    });

    var binaryPayload = new byte[] { 0, 1, 2, 3, 127, 128, 254, 255 };
    var binaryBase64 = Convert.ToBase64String(binaryPayload);
    var writeBytesResult = await EnsureSuccess(byName["talvora_write_bytes"], new()
    {
        ["path"] = developerBinaryFile,
        ["base64"] = binaryBase64,
    });
    if (writeBytesResult.StructuredContent is not { } writeBytesJson ||
        writeBytesJson.GetProperty("bytesWritten").GetInt32() != binaryPayload.Length)
    {
        throw new InvalidOperationException("write_bytes did not report the expected byte count.");
    }

    var readBytesResult = await EnsureSuccess(byName["talvora_read_bytes"], new()
    {
        ["path"] = developerBinaryFile,
    });
    if (readBytesResult.StructuredContent is not { } readBytesJson ||
        !string.Equals(readBytesJson.GetProperty("base64").GetString(), binaryBase64, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("read_bytes did not round-trip the binary payload.");
    }

    var fileHashResult = await EnsureSuccess(byName["talvora_file_hash"], new()
    {
        ["path"] = developerBinaryFile,
        ["algorithm"] = "SHA256",
    });
    var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(binaryPayload));
    if (fileHashResult.StructuredContent is not { } fileHashJson ||
        !string.Equals(fileHashJson.GetProperty("hash").GetString(), expectedHash, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("file_hash did not return the expected SHA256.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerPatchFile,
        ["content"] = "alpha PATCH_TOKEN omega",
    });
    var replaceResult = await EnsureSuccess(byName["talvora_replace_text"], new()
    {
        ["path"] = developerPatchFile,
        ["search"] = "PATCH_TOKEN",
        ["replacement"] = "PATCHED_TOKEN",
        ["expectedMatches"] = 1,
    });
    if (replaceResult.StructuredContent is not { } replaceJson ||
        replaceJson.GetProperty("replacements").GetInt32() != 1 ||
        !replaceJson.GetProperty("changed").GetBoolean() ||
        !string.Equals(
            await ReadToolText(byName["talvora_read_text"], developerPatchFile),
            "alpha PATCHED_TOKEN omega",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("replace_text did not patch exactly one occurrence.");
    }

    var findFilesResult = await EnsureSuccess(byName["talvora_find_files"], new()
    {
        ["root"] = root,
        ["patterns"] = new[] { "*.txt" },
        ["recursive"] = true,
        ["maxResults"] = 0,
    });
    if (findFilesResult.StructuredContent is not { } findFilesJson ||
        !findFilesJson.TryGetProperty("entries", out var foundEntries) ||
        !foundEntries.EnumerateArray().Any(entry =>
            entry.TryGetProperty("path", out var foundPath) &&
            string.Equals(foundPath.GetString(), developerPatchFile, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("find_files did not return the expected text file.");
    }

    var searchTextResult = await EnsureSuccess(byName["talvora_search_text"], new()
    {
        ["root"] = root,
        ["query"] = "PATCHED_[A-Z]+",
        ["regex"] = true,
        ["includePatterns"] = new[] { "*.txt" },
        ["maxMatches"] = 0,
    });
    if (searchTextResult.StructuredContent is not { } searchTextJson ||
        searchTextJson.GetProperty("matchCount").GetInt32() < 1 ||
        !searchTextJson.GetProperty("matches").EnumerateArray().Any(match =>
            match.TryGetProperty("path", out var matchPath) &&
            string.Equals(matchPath.GetString(), developerPatchFile, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("search_text did not return the regex match.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = developerProjectRoot,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerPackageJson,
        ["content"] = "{\"name\":\"talvora-smoke-project\",\"private\":true}",
    });
    var projectDiscoverResult = await EnsureSuccess(byName["talvora_project_discover"], new()
    {
        ["root"] = root,
        ["recursive"] = true,
        ["maxResults"] = 0,
    });
    if (projectDiscoverResult.StructuredContent is not { } projectDiscoverJson ||
        !projectDiscoverJson.GetProperty("projects").EnumerateArray().Any(project =>
            project.TryGetProperty("type", out var projectType) &&
            string.Equals(projectType.GetString(), "node", StringComparison.Ordinal) &&
            project.TryGetProperty("path", out var projectPath) &&
            string.Equals(projectPath.GetString(), developerPackageJson, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("project_discover did not identify package.json as a Node project.");
    }

    var resolveCommandResult = await EnsureSuccess(byName["talvora_resolve_command"], new()
    {
        ["command"] = "cmd.exe",
    });
    if (resolveCommandResult.StructuredContent is not { } resolveCommandJson ||
        !resolveCommandJson.GetProperty("found").GetBoolean() ||
        resolveCommandJson.GetProperty("paths").GetArrayLength() < 1)
    {
        throw new InvalidOperationException("resolve_command did not resolve cmd.exe.");
    }

    var chocoInfoResult = await EnsureSuccess(byName["talvora_choco_info"], new());
    if (chocoInfoResult.StructuredContent is not { } chocoInfoJson ||
        string.IsNullOrWhiteSpace(chocoInfoJson.GetProperty("executable").GetString()) ||
        string.IsNullOrWhiteSpace(chocoInfoJson.GetProperty("version").GetString()))
    {
        throw new InvalidOperationException("choco_info did not return executable/version metadata.");
    }

    var chocoVersion = chocoInfoJson.GetProperty("version").GetString()!;

    var chocoListResult = await EnsureSuccess(byName["talvora_choco_list"], new()
    {
        ["timeoutSeconds"] = 120,
    });
    if (chocoListResult.StructuredContent is not { } chocoListJson ||
        chocoListJson.GetProperty("exitCode").GetInt32() != 0 ||
        chocoListJson.GetProperty("timedOut").GetBoolean())
    {
        throw new InvalidOperationException("choco_list did not complete successfully.");
    }

    var chocoRunResult = await EnsureSuccess(byName["talvora_choco_run"], new()
    {
        ["arguments"] = new[] { "--version" },
        ["timeoutSeconds"] = 30,
    });
    if (chocoRunResult.StructuredContent is not { } chocoRunJson ||
        chocoRunJson.GetProperty("exitCode").GetInt32() != 0 ||
        chocoRunJson.GetProperty("timedOut").GetBoolean() ||
        !string.Equals(
            (chocoRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
            chocoVersion,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("choco_run --version did not match choco_info.");
    }

    var dotnetInfoResult = await EnsureSuccess(byName["talvora_dotnet_info"], new());
    if (dotnetInfoResult.StructuredContent is not { } dotnetInfoJson ||
        !dotnetInfoJson.GetProperty("found").GetBoolean() ||
        string.IsNullOrWhiteSpace(dotnetInfoJson.GetProperty("executable").GetString()) ||
        string.IsNullOrWhiteSpace(dotnetInfoJson.GetProperty("version").GetString()) ||
        dotnetInfoJson.GetProperty("sdks").GetArrayLength() < 1)
    {
        throw new InvalidOperationException("dotnet_info did not report the installed .NET SDK.");
    }

    var dotnetVersion = dotnetInfoJson.GetProperty("version").GetString()!;

    var dotnetRunResult = await EnsureSuccess(byName["talvora_dotnet_run"], new()
    {
        ["workingDirectory"] = root,
        ["arguments"] = new[] { "--version" },
        ["timeoutSeconds"] = 30,
    });
    if (dotnetRunResult.StructuredContent is not { } dotnetRunJson ||
        dotnetRunJson.GetProperty("exitCode").GetInt32() != 0 ||
        dotnetRunJson.GetProperty("timedOut").GetBoolean() ||
        !string.Equals(
            (dotnetRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
            dotnetVersion,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("dotnet_run --version did not match dotnet_info.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = dotnetProjectRoot,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = dotnetProjectFile,
        ["content"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>",
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = dotnetProgramFile,
        ["content"] = "Console.WriteLine(\"TALVORA_DOTNET_SMOKE\");",
    });

    var dotnetRestoreResult = await EnsureSuccess(byName["talvora_dotnet_restore"], new()
    {
        ["workingDirectory"] = dotnetProjectRoot,
        ["target"] = dotnetProjectFile,
        ["timeoutSeconds"] = 120,
    });
    if (dotnetRestoreResult.StructuredContent is not { } dotnetRestoreJson ||
        dotnetRestoreJson.GetProperty("exitCode").GetInt32() != 0 ||
        dotnetRestoreJson.GetProperty("timedOut").GetBoolean())
    {
        throw new InvalidOperationException("dotnet_restore failed for the smoke project.");
    }

    var dotnetBuildResult = await EnsureSuccess(byName["talvora_dotnet_build"], new()
    {
        ["workingDirectory"] = dotnetProjectRoot,
        ["target"] = dotnetProjectFile,
        ["configuration"] = "Release",
        ["noRestore"] = true,
        ["additionalArguments"] = new[] { "--nologo" },
        ["timeoutSeconds"] = 120,
    });
    if (dotnetBuildResult.StructuredContent is not { } dotnetBuildJson ||
        dotnetBuildJson.GetProperty("exitCode").GetInt32() != 0 ||
        dotnetBuildJson.GetProperty("timedOut").GetBoolean())
    {
        throw new InvalidOperationException("dotnet_build failed for the smoke project.");
    }

    var dotnetOutputInfo = await EnsureSuccess(byName["talvora_path_info"], new()
    {
        ["path"] = dotnetOutputDll,
    });
    if (dotnetOutputInfo.StructuredContent is not { } dotnetOutputJson ||
        !dotnetOutputJson.GetProperty("exists").GetBoolean() ||
        !string.Equals(dotnetOutputJson.GetProperty("kind").GetString(), "file", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("dotnet_build did not create the expected output assembly.");
    }

    var dotnetExecutable = dotnetInfoJson.GetProperty("executable").GetString()
        ?? throw new InvalidOperationException("dotnet_info executable was empty.");

    var peInfoResult = await EnsureSuccess(byName["talvora_pe_info"], new()
    {
        ["path"] = dotnetExecutable,
    });
    if (peInfoResult.StructuredContent is not { } peInfoJson ||
        !peInfoJson.GetProperty("isPe").GetBoolean() ||
        peInfoJson.GetProperty("length").GetInt64() < 1 ||
        string.IsNullOrWhiteSpace(peInfoJson.GetProperty("machine").GetString()))
    {
        throw new InvalidOperationException("pe_info did not identify dotnet.exe as a PE image.");
    }

    var fileVersionResult = await EnsureSuccess(byName["talvora_file_version_info"], new()
    {
        ["path"] = dotnetExecutable,
    });
    if (fileVersionResult.StructuredContent is not { } fileVersionJson ||
        !string.Equals(
            Path.GetFullPath(fileVersionJson.GetProperty("path").GetString() ?? string.Empty),
            Path.GetFullPath(dotnetExecutable),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("file_version_info returned an unexpected file path.");
    }

    var msbuildInfoResult = await EnsureSuccess(byName["talvora_msbuild_info"], new());
    if (msbuildInfoResult.StructuredContent is not { } msbuildInfoJson ||
        !msbuildInfoJson.GetProperty("found").GetBoolean() ||
        string.IsNullOrWhiteSpace(msbuildInfoJson.GetProperty("executable").GetString()) ||
        string.IsNullOrWhiteSpace(msbuildInfoJson.GetProperty("version").GetString()))
    {
        throw new InvalidOperationException("msbuild_info did not resolve MSBuild or dotnet msbuild.");
    }

    var msbuildRunResult = await EnsureSuccess(byName["talvora_msbuild_run"], new()
    {
        ["workingDirectory"] = root,
        ["arguments"] = new[] { "-version", "-nologo" },
        ["timeoutSeconds"] = 60,
    });
    if (msbuildRunResult.StructuredContent is not { } msbuildRunJson ||
        msbuildRunJson.GetProperty("exitCode").GetInt32() != 0 ||
        msbuildRunJson.GetProperty("timedOut").GetBoolean() ||
        string.IsNullOrWhiteSpace(msbuildRunJson.GetProperty("standardOutput").GetString()))
    {
        throw new InvalidOperationException("msbuild_run -version failed.");
    }

    var visualStudioResult = await EnsureSuccess(byName["talvora_visual_studio_instances"], new());
    if (visualStudioResult.StructuredContent is not { } visualStudioJson)
    {
        throw new InvalidOperationException("visual_studio_instances did not return structured content.");
    }

    var visualStudioCount = visualStudioJson.GetProperty("count").GetInt32();
    var visualStudioAliasResult = await EnsureSuccess(byName["talvora_vs_instances"], new());
    if (visualStudioAliasResult.StructuredContent is not { } visualStudioAliasJson ||
        visualStudioAliasJson.GetProperty("count").GetInt32() != visualStudioCount)
    {
        throw new InvalidOperationException("vs_instances alias did not match visual_studio_instances.");
    }

    var toolchainInfoResult = await EnsureSuccess(byName["talvora_windows_toolchain_info"], new());
    if (toolchainInfoResult.StructuredContent is not { } toolchainInfoJson ||
        !toolchainInfoJson.TryGetProperty("msbuild", out var aggregateMsbuild) ||
        !aggregateMsbuild.GetProperty("found").GetBoolean())
    {
        throw new InvalidOperationException("windows_toolchain_info did not report the resolved MSBuild toolchain.");
    }

    if (visualStudioCount > 0)
    {
        var usableInstance = visualStudioJson
            .GetProperty("instances")
            .EnumerateArray()
            .FirstOrDefault(instance =>
                instance.GetProperty("isComplete").GetBoolean() &&
                instance.GetProperty("isLaunchable").GetBoolean() &&
                !string.IsNullOrWhiteSpace(instance.GetProperty("installationPath").GetString()));

        if (usableInstance.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var installationPath = usableInstance.GetProperty("installationPath").GetString()!;
            var devEnvironmentResult = await EnsureSuccess(byName["talvora_vs_dev_environment"], new()
            {
                ["installationPath"] = installationPath,
                ["architecture"] = "x64",
                ["hostArchitecture"] = "x64",
                ["timeoutSeconds"] = 120,
            });

            if (devEnvironmentResult.StructuredContent is not { } devEnvironmentJson ||
                devEnvironmentJson.GetProperty("count").GetInt32() < 1 ||
                string.IsNullOrWhiteSpace(devEnvironmentJson.GetProperty("scriptPath").GetString()))
            {
                throw new InvalidOperationException("vs_dev_environment did not return a Visual Studio developer environment.");
            }
        }
    }

    var windowsSdkResult = await EnsureSuccess(byName["talvora_windows_sdk_info"], new());
    if (windowsSdkResult.StructuredContent is not { } windowsSdkJson)
    {
        throw new InvalidOperationException("windows_sdk_info did not return structured content.");
    }
    if (windowsSdkJson.GetProperty("found").GetBoolean() &&
        string.IsNullOrWhiteSpace(windowsSdkJson.GetProperty("kitsRoot10").GetString()))
    {
        throw new InvalidOperationException("windows_sdk_info reported an SDK without KitsRoot10.");
    }

    var windowsSdkAliasResult = await EnsureSuccess(byName["talvora_windows_sdk_list"], new());
    if (windowsSdkAliasResult.StructuredContent is not { } windowsSdkAliasJson ||
        windowsSdkAliasJson.GetProperty("found").GetBoolean() != windowsSdkJson.GetProperty("found").GetBoolean())
    {
        throw new InvalidOperationException("windows_sdk_list alias did not match windows_sdk_info.");
    }

    var cmakeInfoResult = await EnsureSuccess(byName["talvora_cmake_info"], new());
    if (cmakeInfoResult.StructuredContent is not { } cmakeInfoJson)
    {
        throw new InvalidOperationException("cmake_info did not return structured content.");
    }
    if (cmakeInfoJson.GetProperty("found").GetBoolean())
    {
        var cmakeRunResult = await EnsureSuccess(byName["talvora_cmake_run"], new()
        {
            ["workingDirectory"] = root,
            ["arguments"] = new[] { "--version" },
            ["timeoutSeconds"] = 30,
        });
        if (cmakeRunResult.StructuredContent is not { } cmakeRunJson ||
            cmakeRunJson.GetProperty("exitCode").GetInt32() != 0 ||
            cmakeRunJson.GetProperty("timedOut").GetBoolean())
        {
            throw new InvalidOperationException("cmake_run --version failed.");
        }
    }

    var ninjaInfoResult = await EnsureSuccess(byName["talvora_ninja_info"], new());
    if (ninjaInfoResult.StructuredContent is not { } ninjaInfoJson)
    {
        throw new InvalidOperationException("ninja_info did not return structured content.");
    }
    if (ninjaInfoJson.GetProperty("found").GetBoolean())
    {
        var ninjaRunResult = await EnsureSuccess(byName["talvora_ninja_run"], new()
        {
            ["workingDirectory"] = root,
            ["arguments"] = new[] { "--version" },
            ["timeoutSeconds"] = 30,
        });
        if (ninjaRunResult.StructuredContent is not { } ninjaRunJson ||
            ninjaRunJson.GetProperty("exitCode").GetInt32() != 0 ||
            ninjaRunJson.GetProperty("timedOut").GetBoolean())
        {
            throw new InvalidOperationException("ninja_run --version failed.");
        }
    }

    var nodeInfoResult = await EnsureSuccess(byName["talvora_node_info"], new());
    if (nodeInfoResult.StructuredContent is not { } nodeInfoJson)
    {
        throw new InvalidOperationException("node_info did not return structured content.");
    }

    var nodeFound = nodeInfoJson.GetProperty("nodeFound").GetBoolean();
    var npmFound = nodeInfoJson.GetProperty("npmFound").GetBoolean();

    if (nodeFound && string.IsNullOrWhiteSpace(nodeInfoJson.GetProperty("nodeVersion").GetString()))
    {
        throw new InvalidOperationException("node_info reported Node.js without a version.");
    }

    if (npmFound)
    {
        var npmVersion = nodeInfoJson.GetProperty("npmVersion").GetString();
        if (string.IsNullOrWhiteSpace(npmVersion))
        {
            throw new InvalidOperationException("node_info reported npm without a version.");
        }

        var npmRunResult = await EnsureSuccess(byName["talvora_npm_run"], new()
        {
            ["workingDirectory"] = root,
            ["arguments"] = new[] { "--version" },
            ["timeoutSeconds"] = 30,
        });
        if (npmRunResult.StructuredContent is not { } npmRunJson ||
            npmRunJson.GetProperty("exitCode").GetInt32() != 0 ||
            npmRunJson.GetProperty("timedOut").GetBoolean() ||
            !string.Equals(
                (npmRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
                npmVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("npm_run --version did not match node_info.");
        }
    }

    var pythonInfoResult = await EnsureSuccess(byName["talvora_python_info"], new());
    if (pythonInfoResult.StructuredContent is not { } pythonInfoJson ||
        !pythonInfoJson.GetProperty("found").GetBoolean() ||
        string.IsNullOrWhiteSpace(pythonInfoJson.GetProperty("interpreterExecutable").GetString()) ||
        string.IsNullOrWhiteSpace(pythonInfoJson.GetProperty("version").GetString()))
    {
        throw new InvalidOperationException("python_info did not report the installed Python runtime.");
    }

    var pythonExecutable = pythonInfoJson.GetProperty("interpreterExecutable").GetString()!;

    var pythonRunResult = await EnsureSuccess(byName["talvora_python_run"], new()
    {
        ["pythonExecutable"] = pythonExecutable,
        ["workingDirectory"] = root,
        ["arguments"] = new[] { "-c", "print('TALVORA_PYTHON_SMOKE')" },
        ["timeoutSeconds"] = 30,
    });
    if (pythonRunResult.StructuredContent is not { } pythonRunJson ||
        pythonRunJson.GetProperty("exitCode").GetInt32() != 0 ||
        pythonRunJson.GetProperty("timedOut").GetBoolean() ||
        !(pythonRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
            .Contains("TALVORA_PYTHON_SMOKE", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("python_run did not execute the smoke expression.");
    }

    if (pythonInfoJson.GetProperty("pipAvailable").GetBoolean())
    {
        var pipRunResult = await EnsureSuccess(byName["talvora_pip_run"], new()
        {
            ["pythonExecutable"] = pythonExecutable,
            ["workingDirectory"] = root,
            ["arguments"] = new[] { "--version" },
            ["timeoutSeconds"] = 30,
        });
        if (pipRunResult.StructuredContent is not { } pipRunJson ||
            pipRunJson.GetProperty("exitCode").GetInt32() != 0 ||
            pipRunJson.GetProperty("timedOut").GetBoolean() ||
            !(pipRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
                .Contains("pip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("pip_run --version failed.");
        }
    }

    var venvCreateResult = await EnsureSuccess(byName["talvora_python_venv_create"], new()
    {
        ["path"] = pythonVenvRoot,
        ["pythonExecutable"] = pythonExecutable,
        ["workingDirectory"] = root,
        ["withoutPip"] = true,
        ["timeoutSeconds"] = 120,
    });
    if (venvCreateResult.StructuredContent is not { } venvCreateJson ||
        venvCreateJson.GetProperty("command").GetProperty("exitCode").GetInt32() != 0 ||
        venvCreateJson.GetProperty("command").GetProperty("timedOut").GetBoolean() ||
        string.IsNullOrWhiteSpace(venvCreateJson.GetProperty("pythonExecutable").GetString()))
    {
        throw new InvalidOperationException("python_venv_create did not create a usable virtual environment.");
    }

    var venvPythonExecutable = venvCreateJson.GetProperty("pythonExecutable").GetString()!;
    var venvInfoResult = await EnsureSuccess(byName["talvora_python_info"], new()
    {
        ["pythonExecutable"] = venvPythonExecutable,
        ["workingDirectory"] = root,
    });
    if (venvInfoResult.StructuredContent is not { } venvInfoJson ||
        !venvInfoJson.GetProperty("found").GetBoolean() ||
        !venvInfoJson.GetProperty("inVirtualEnvironment").GetBoolean())
    {
        throw new InvalidOperationException("python_info did not identify the created virtual environment.");
    }

    var dockerInfoResult = await EnsureSuccess(byName["talvora_docker_info"], new()
    {
        ["workingDirectory"] = root,
    });
    if (dockerInfoResult.StructuredContent is not { } dockerInfoJson)
    {
        throw new InvalidOperationException("docker_info did not return structured content.");
    }

    if (dockerInfoJson.GetProperty("found").GetBoolean())
    {
        var dockerExecutable = dockerInfoJson.GetProperty("executable").GetString();
        if (string.IsNullOrWhiteSpace(dockerExecutable))
        {
            throw new InvalidOperationException("docker_info reported Docker without an executable.");
        }

        var dockerRunResult = await EnsureSuccess(byName["talvora_docker_run"], new()
        {
            ["dockerExecutable"] = dockerExecutable,
            ["workingDirectory"] = root,
            ["arguments"] = new[] { "--version" },
            ["timeoutSeconds"] = 30,
        });
        if (dockerRunResult.StructuredContent is not { } dockerRunJson ||
            dockerRunJson.GetProperty("exitCode").GetInt32() != 0 ||
            dockerRunJson.GetProperty("timedOut").GetBoolean() ||
            !(dockerRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
                .Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("docker_run --version failed.");
        }
    }

    var httpMockAutoPort = GetFreeLoopbackTcpPort();
    var httpMockAutoPrefix = $"http://127.0.0.1:{httpMockAutoPort}/talvora-auto/";
    var httpMockAutoStart = await EnsureSuccess(byName["talvora_http_mock_start"], new()
    {
        ["prefixes"] = new[] { httpMockAutoPrefix },
        ["autoReply"] = true,
        ["defaultStatusCode"] = 201,
        ["defaultBody"] = "talvora-auto-reply",
        ["defaultContentType"] = "text/plain; charset=utf-8",
        ["defaultHeaders"] = new Dictionary<string, string>
        {
            ["X-Talvora-Mock"] = "auto",
        },
        ["maxQueuedRequests"] = 0,
    });
    if (httpMockAutoStart.StructuredContent is not { } httpMockAutoStartJson ||
        string.IsNullOrWhiteSpace(httpMockAutoStartJson.GetProperty("listenerId").GetString()))
    {
        throw new InvalidOperationException("http_mock_start did not create the auto-reply listener.");
    }

    var httpMockAutoId = httpMockAutoStartJson.GetProperty("listenerId").GetString()!;
    try
    {
        var httpMockGetResult = await EnsureSuccess(byName["talvora_http_mock_get"], new()
        {
            ["listenerId"] = httpMockAutoId,
        });
        if (httpMockGetResult.StructuredContent is not { } httpMockGetJson ||
            !httpMockGetJson.GetProperty("isListening").GetBoolean() ||
            !httpMockGetJson.GetProperty("autoReply").GetBoolean())
        {
            throw new InvalidOperationException("http_mock_get did not report the auto listener as active.");
        }

        var httpMockListResult = await EnsureSuccess(byName["talvora_http_mock_list"], new());
        if (httpMockListResult.StructuredContent is not { } httpMockListJson ||
            !httpMockListJson.GetProperty("listeners").EnumerateArray().Any(listener =>
                string.Equals(
                    listener.GetProperty("listenerId").GetString(),
                    httpMockAutoId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("http_mock_list did not include the auto listener.");
        }

        var autoHttpResult = await EnsureSuccess(byName["talvora_http_request"], new()
        {
            ["method"] = "POST",
            ["url"] = httpMockAutoPrefix + "capture?mode=auto",
            ["headers"] = new Dictionary<string, string>
            {
                ["X-Smoke-Header"] = "auto-value",
            },
            ["body"] = "auto-request-body",
            ["contentType"] = "text/plain; charset=utf-8",
            ["timeoutSeconds"] = 10,
            ["responseMode"] = "text",
            ["maxResponseBytes"] = 65536L,
        });
        if (autoHttpResult.StructuredContent is not { } autoHttpJson ||
            autoHttpJson.GetProperty("statusCode").GetInt32() != 201 ||
            !string.Equals(
                autoHttpJson.GetProperty("body").GetString(),
                "talvora-auto-reply",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTTP mock auto reply did not return the configured response.");
        }

        var capturedAuto = false;
        for (var attempt = 0; attempt < 30 && !capturedAuto; attempt++)
        {
            await Task.Delay(50);
            var readResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
            {
                ["listenerId"] = httpMockAutoId,
                ["maxRequests"] = 0,
                ["consume"] = false,
            });

            if (readResult.StructuredContent is not { } readJson)
            {
                continue;
            }

            capturedAuto = readJson.GetProperty("requests").EnumerateArray().Any(request =>
                string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.Ordinal) &&
                (request.GetProperty("url").GetString() ?? string.Empty).Contains("mode=auto", StringComparison.Ordinal) &&
                string.Equals(request.GetProperty("body").GetString(), "auto-request-body", StringComparison.Ordinal));
        }

        if (!capturedAuto)
        {
            throw new InvalidOperationException("http_mock_read did not capture the auto-reply request.");
        }
    }
    finally
    {
        var stopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
        {
            ["listenerId"] = httpMockAutoId,
        });
        if (stopResult.StructuredContent is not { } stopJson ||
            !stopJson.GetProperty("found").GetBoolean() ||
            !stopJson.GetProperty("stopped").GetBoolean())
        {
            throw new InvalidOperationException("http_mock_stop did not stop the auto listener.");
        }
    }

    var httpMockManualPort = GetFreeLoopbackTcpPort();
    var httpMockManualPrefix = $"http://127.0.0.1:{httpMockManualPort}/talvora-manual/";
    var httpMockManualStart = await EnsureSuccess(byName["talvora_http_mock_start"], new()
    {
        ["prefixes"] = new[] { httpMockManualPrefix },
        ["autoReply"] = false,
        ["defaultStatusCode"] = 504,
        ["defaultBody"] = "manual-timeout",
        ["pendingResponseTimeoutSeconds"] = 10,
        ["maxQueuedRequests"] = 0,
    });
    if (httpMockManualStart.StructuredContent is not { } httpMockManualStartJson ||
        string.IsNullOrWhiteSpace(httpMockManualStartJson.GetProperty("listenerId").GetString()))
    {
        throw new InvalidOperationException("http_mock_start did not create the manual listener.");
    }

    var httpMockManualId = httpMockManualStartJson.GetProperty("listenerId").GetString()!;
    try
    {
        using var manualClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        using var manualContent = new StringContent(
            "manual-request-body",
            System.Text.Encoding.UTF8,
            "text/plain");

        var manualRequestTask = manualClient.PostAsync(
            httpMockManualPrefix + "pending?mode=manual",
            manualContent);

        string? pendingRequestId = null;
        for (var attempt = 0; attempt < 60 && pendingRequestId is null; attempt++)
        {
            await Task.Delay(50);
            var readResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
            {
                ["listenerId"] = httpMockManualId,
                ["maxRequests"] = 0,
                ["consume"] = false,
            });

            if (readResult.StructuredContent is not { } readJson)
            {
                continue;
            }

            var pendingRequest = readJson
                .GetProperty("requests")
                .EnumerateArray()
                .FirstOrDefault(request =>
                    request.GetProperty("pendingResponse").GetBoolean() &&
                    string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.Ordinal) &&
                    string.Equals(request.GetProperty("body").GetString(), "manual-request-body", StringComparison.Ordinal));

            if (pendingRequest.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                pendingRequestId = pendingRequest.GetProperty("requestId").GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(pendingRequestId))
        {
            throw new InvalidOperationException("http_mock_read did not expose a pending manual request.");
        }

        var replyResult = await EnsureSuccess(byName["talvora_http_mock_reply"], new()
        {
            ["listenerId"] = httpMockManualId,
            ["requestId"] = pendingRequestId,
            ["statusCode"] = 202,
            ["body"] = "talvora-manual-reply",
            ["contentType"] = "text/plain; charset=utf-8",
            ["headers"] = new Dictionary<string, string>
            {
                ["X-Talvora-Mock"] = "manual",
            },
        });
        if (replyResult.StructuredContent is not { } replyJson ||
            !replyJson.GetProperty("found").GetBoolean() ||
            !replyJson.GetProperty("replied").GetBoolean() ||
            replyJson.GetProperty("statusCode").GetInt32() != 202)
        {
            throw new InvalidOperationException("http_mock_reply did not reply to the pending request.");
        }

        using var manualResponse = await manualRequestTask;
        var manualResponseBody = await manualResponse.Content.ReadAsStringAsync();
        if ((int)manualResponse.StatusCode != 202 ||
            !string.Equals(manualResponseBody, "talvora-manual-reply", StringComparison.Ordinal) ||
            !manualResponse.Headers.TryGetValues("X-Talvora-Mock", out var manualHeaderValues) ||
            !manualHeaderValues.Contains("manual", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("manual HTTP mock client did not receive the configured reply.");
        }
    }
    finally
    {
        var stopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
        {
            ["listenerId"] = httpMockManualId,
        });
        if (stopResult.StructuredContent is not { } stopJson ||
            !stopJson.GetProperty("found").GetBoolean() ||
            !stopJson.GetProperty("stopped").GetBoolean())
        {
            throw new InvalidOperationException("http_mock_stop did not stop the manual listener.");
        }
    }

    var httpResult = await EnsureSuccess(byName["talvora_http_request"], new()
    {
        ["method"] = "GET",
        ["url"] = "http://127.0.0.1:7676/healthz",
        ["responseMode"] = "text",
        ["maxResponseBytes"] = 0,
    });
    if (httpResult.StructuredContent is not { } httpJson ||
        httpJson.GetProperty("statusCode").GetInt32() != 200 ||
        !(httpJson.GetProperty("body").GetString() ?? string.Empty).Contains("Talvora", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("http_request did not return the Talvora health response.");
    }

    var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    portProbe.Start();
    var httpMockPort = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();

    var httpMockPrefix = $"http://127.0.0.1:{httpMockPort}/";
    var httpMockBody = "talvora-http-mock-" + smokeId;

    var httpMockStartResult = await EnsureSuccess(byName["talvora_http_mock_start"], new()
    {
        ["prefixes"] = new[] { httpMockPrefix },
        ["autoReply"] = true,
        ["defaultStatusCode"] = 202,
        ["defaultBody"] = "mock-accepted",
        ["defaultContentType"] = "text/plain; charset=utf-8",
        ["requestBodyMode"] = "text",
        ["maxRequestBodyBytes"] = 0,
        ["maxQueuedRequests"] = 0,
    });
    if (httpMockStartResult.StructuredContent is not { } httpMockStartJson ||
        string.IsNullOrWhiteSpace(httpMockStartJson.GetProperty("listenerId").GetString()))
    {
        throw new InvalidOperationException("http_mock_start did not return a listener ID.");
    }

    var httpMockListenerId = httpMockStartJson.GetProperty("listenerId").GetString()!;

    try
    {
        var httpMockGetResult = await EnsureSuccess(byName["talvora_http_mock_get"], new()
        {
            ["listenerId"] = httpMockListenerId,
        });
        if (httpMockGetResult.StructuredContent is not { } httpMockGetJson ||
            !httpMockGetJson.GetProperty("isListening").GetBoolean())
        {
            throw new InvalidOperationException("http_mock_get did not report the listener as active.");
        }

        var httpMockListResult = await EnsureSuccess(byName["talvora_http_mock_list"], new());
        if (httpMockListResult.StructuredContent is not { } httpMockListJson ||
            !httpMockListJson.GetProperty("listeners").EnumerateArray().Any(listener =>
                listener.TryGetProperty("listenerId", out var listedId) &&
                string.Equals(listedId.GetString(), httpMockListenerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("http_mock_list did not include the active listener.");
        }

        var httpMockRequestResult = await EnsureSuccess(byName["talvora_http_request"], new()
        {
            ["method"] = "POST",
            ["url"] = httpMockPrefix + "webhook?kind=smoke",
            ["headers"] = new Dictionary<string, string>
            {
                ["X-Talvora-Smoke"] = smokeId,
            },
            ["body"] = httpMockBody,
            ["contentType"] = "text/plain; charset=utf-8",
            ["responseMode"] = "text",
            ["maxResponseBytes"] = 65536L,
            ["timeoutSeconds"] = 10,
        });
        if (httpMockRequestResult.StructuredContent is not { } httpMockRequestJson ||
            httpMockRequestJson.GetProperty("statusCode").GetInt32() != 202 ||
            !string.Equals(
                httpMockRequestJson.GetProperty("body").GetString(),
                "mock-accepted",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("http mock listener did not return its configured response.");
        }

        var httpMockReadResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
        {
            ["listenerId"] = httpMockListenerId,
            ["afterSequence"] = 0L,
            ["maxRequests"] = 0,
            ["consume"] = true,
        });
        if (httpMockReadResult.StructuredContent is not { } httpMockReadJson ||
            httpMockReadJson.GetProperty("count").GetInt32() < 1 ||
            !httpMockReadJson.GetProperty("requests").EnumerateArray().Any(request =>
                string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.OrdinalIgnoreCase) &&
                (request.GetProperty("rawUrl").GetString() ?? string.Empty).Contains("/webhook?kind=smoke", StringComparison.Ordinal) &&
                string.Equals(request.GetProperty("body").GetString(), httpMockBody, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("http_mock_read did not capture the smoke request.");
        }
    }
    finally
    {
        var httpMockStopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
        {
            ["listenerId"] = httpMockListenerId,
        });
        if (httpMockStopResult.StructuredContent is not { } httpMockStopJson ||
            !httpMockStopJson.GetProperty("found").GetBoolean() ||
            !httpMockStopJson.GetProperty("stopped").GetBoolean())
        {
            throw new InvalidOperationException("http_mock_stop did not stop the listener.");
        }
    }

    var tcpListenersResult = await EnsureSuccess(byName["talvora_tcp_listeners"], new()
    {
        ["localPort"] = 7676,
    });
    if (tcpListenersResult.StructuredContent is not { } tcpListenersJson ||
        tcpListenersJson.GetProperty("count").GetInt32() < 1 ||
        !tcpListenersJson.GetProperty("connections").EnumerateArray().Any(connection =>
            connection.GetProperty("localPort").GetInt32() == 7676 &&
            connection.GetProperty("processId").GetInt32() > 0))
    {
        throw new InvalidOperationException("tcp_listeners did not report the Talvora listener.");
    }

    var tcpConnectionsResult = await EnsureSuccess(byName["talvora_tcp_connections"], new()
    {
        ["localPort"] = 7676,
    });
    if (tcpConnectionsResult.StructuredContent is not { } tcpConnectionsJson ||
        tcpConnectionsJson.GetProperty("count").GetInt32() < 1)
    {
        throw new InvalidOperationException("tcp_connections did not report Talvora TCP rows.");
    }

    var waitTcpResult = await EnsureSuccess(byName["talvora_wait_tcp"], new()
    {
        ["host"] = "127.0.0.1",
        ["port"] = 7676,
        ["timeoutSeconds"] = 5,
    });
    if (waitTcpResult.StructuredContent is not { } waitTcpJson ||
        !waitTcpJson.GetProperty("connected").GetBoolean())
    {
        throw new InvalidOperationException("wait_tcp did not connect to the Talvora listener.");
    }

    var networkInterfacesResult = await EnsureSuccess(byName["talvora_network_interfaces"], new());
    if (networkInterfacesResult.StructuredContent is not { } networkInterfacesJson ||
        networkInterfacesJson.GetProperty("count").GetInt32() < 1)
    {
        throw new InvalidOperationException("network_interfaces did not return any network interfaces.");
    }

    var dnsLookupResult = await EnsureSuccess(byName["talvora_dns_lookup"], new()
    {
        ["host"] = "localhost",
    });
    if (dnsLookupResult.StructuredContent is not { } dnsLookupJson ||
        dnsLookupJson.GetProperty("addresses").GetArrayLength() < 1)
    {
        throw new InvalidOperationException("dns_lookup did not resolve localhost.");
    }

    var pingResult = await EnsureSuccess(byName["talvora_ping"], new()
    {
        ["host"] = "127.0.0.1",
        ["timeoutMilliseconds"] = 3000,
        ["payloadBytes"] = 16,
    });
    if (pingResult.StructuredContent is not { } pingJson ||
        !string.Equals(
            pingJson.GetProperty("status").GetString(),
            "Success",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("ping did not reach loopback.");
    }

    var tcpExchangeResult = await EnsureSuccess(byName["talvora_tcp_exchange"], new()
    {
        ["host"] = "127.0.0.1",
        ["port"] = 7676,
        ["text"] = "GET /healthz HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
        ["responseMode"] = "text",
        ["maxResponseBytes"] = 65536L,
        ["timeoutSeconds"] = 10,
        ["idleReadTimeoutMilliseconds"] = 2000,
    });
    if (tcpExchangeResult.StructuredContent is not { } tcpExchangeJson ||
        tcpExchangeJson.GetProperty("bytesReceived").GetInt64() < 1 ||
        !(tcpExchangeJson.GetProperty("response").GetString() ?? string.Empty)
            .Contains("200 OK", StringComparison.OrdinalIgnoreCase) ||
        !(tcpExchangeJson.GetProperty("response").GetString() ?? string.Empty)
            .Contains("Talvora", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("tcp_exchange did not return the Talvora health response.");
    }

    var watchStartResult = await EnsureSuccess(byName["talvora_watch_start"], new()
    {
        ["path"] = root,
        ["filter"] = "watch-*.txt",
        ["includeSubdirectories"] = true,
        ["internalBufferSize"] = 32768,
        ["maxQueuedEvents"] = 0,
    });
    if (watchStartResult.StructuredContent is not { } watchStartJson ||
        !watchStartJson.TryGetProperty("watchId", out var watchIdJson) ||
        string.IsNullOrWhiteSpace(watchIdJson.GetString()))
    {
        throw new InvalidOperationException("watch_start did not return a watch ID.");
    }

    var watchId = watchIdJson.GetString()!;
    try
    {
        var watchListResult = await EnsureSuccess(byName["talvora_watch_list"], new());
        if (watchListResult.StructuredContent is not { } watchListJson ||
            !watchListJson.GetProperty("watches").EnumerateArray().Any(watch =>
                watch.TryGetProperty("watchId", out var listedWatchId) &&
                string.Equals(listedWatchId.GetString(), watchId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("watch_list did not include the active watcher.");
        }

        var watchGetResult = await EnsureSuccess(byName["talvora_watch_get"], new()
        {
            ["watchId"] = watchId,
        });
        if (watchGetResult.StructuredContent is not { } watchGetJson ||
            !string.Equals(
                watchGetJson.GetProperty("watchId").GetString(),
                watchId,
                StringComparison.OrdinalIgnoreCase) ||
            !watchGetJson.GetProperty("enabled").GetBoolean() ||
            !string.Equals(
                Path.GetFullPath(watchGetJson.GetProperty("path").GetString() ?? string.Empty),
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("watch_get did not report the active watcher.");
        }

        await EnsureSuccess(byName["talvora_write_text"], new()
        {
            ["path"] = developerWatchFile,
            ["content"] = "watch-payload-" + smokeId,
        });

        var watchWaitResult = await EnsureSuccess(byName["talvora_watch_wait"], new()
        {
            ["watchId"] = watchId,
            ["afterSequence"] = 0L,
            ["timeoutSeconds"] = 10,
            ["pollIntervalMilliseconds"] = 50,
        });
        if (watchWaitResult.StructuredContent is not { } watchWaitJson ||
            !watchWaitJson.GetProperty("signaled").GetBoolean() ||
            !watchWaitJson.TryGetProperty("event", out var waitedEvent) ||
            waitedEvent.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !string.Equals(
                waitedEvent.GetProperty("fullPath").GetString(),
                Path.GetFullPath(developerWatchFile),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("watch_wait did not observe the smoke file change.");
        }

        var watchReadResult = await EnsureSuccess(byName["talvora_watch_read"], new()
        {
            ["watchId"] = watchId,
            ["afterSequence"] = 0L,
            ["maxEvents"] = 0,
            ["consume"] = true,
        });
        if (watchReadResult.StructuredContent is not { } watchReadJson ||
            watchReadJson.GetProperty("count").GetInt32() < 1 ||
            !watchReadJson.GetProperty("events").EnumerateArray().Any(change =>
                change.TryGetProperty("fullPath", out var changedPath) &&
                string.Equals(
                    changedPath.GetString(),
                    Path.GetFullPath(developerWatchFile),
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("watch_read did not return the smoke file change.");
        }
    }
    finally
    {
        var watchStopResult = await EnsureSuccess(byName["talvora_watch_stop"], new()
        {
            ["watchId"] = watchId,
        });
        if (watchStopResult.StructuredContent is not { } watchStopJson ||
            !watchStopJson.GetProperty("found").GetBoolean() ||
            !watchStopJson.GetProperty("stopped").GetBoolean())
        {
            throw new InvalidOperationException("watch_stop did not stop the smoke watcher.");
        }
    }

    var jobToken = "TALVORA_JOB_SMOKE_" + smokeId;
    var cmdPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "cmd.exe");
    var jobStartResult = await EnsureSuccess(byName["talvora_job_start"], new()
    {
        ["executable"] = cmdPath,
        ["arguments"] = new[] { "/d", "/q" },
        ["workingDirectory"] = root,
    });
    if (jobStartResult.StructuredContent is not { } jobStartJson ||
        !jobStartJson.TryGetProperty("jobId", out var jobIdJson) ||
        string.IsNullOrWhiteSpace(jobIdJson.GetString()) ||
        !jobStartJson.TryGetProperty("processId", out var jobPidJson) ||
        jobPidJson.GetInt32() <= 0)
    {
        throw new InvalidOperationException("job_start did not return a live job identity.");
    }

    var jobId = jobIdJson.GetString()!;
    var jobPid = jobPidJson.GetInt32();

    try
    {
        var jobGetResult = await EnsureSuccess(byName["talvora_job_get"], new()
        {
            ["jobId"] = jobId,
        });
        if (jobGetResult.StructuredContent is not { } jobGetJson ||
            jobGetJson.GetProperty("processId").GetInt32() != jobPid ||
            !string.Equals(jobGetJson.GetProperty("state").GetString(), "Running", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("job_get did not report the started job as running.");
        }

        var jobListResult = await EnsureSuccess(byName["talvora_job_list"], new()
        {
            ["includeExited"] = true,
            ["maxResults"] = 0,
        });
        if (jobListResult.StructuredContent is not { } jobListJson ||
            !jobListJson.GetProperty("jobs").EnumerateArray().Any(job =>
                job.TryGetProperty("jobId", out var listedJobId) &&
                string.Equals(listedJobId.GetString(), jobId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("job_list did not include the started job.");
        }

        var stdinResult = await EnsureSuccess(byName["talvora_job_write_stdin"], new()
        {
            ["jobId"] = jobId,
            ["text"] = "echo " + jobToken,
            ["appendNewLine"] = true,
        });
        if (stdinResult.StructuredContent is not { } stdinJson ||
            stdinJson.GetProperty("processId").GetInt32() != jobPid)
        {
            throw new InvalidOperationException("job_write_stdin did not target the expected process.");
        }

        var observedOutput = false;
        for (var attempt = 0; attempt < 20 && !observedOutput; attempt++)
        {
            await Task.Delay(100);
            var outputResult = await EnsureSuccess(byName["talvora_job_read_output"], new()
            {
                ["jobId"] = jobId,
                ["stream"] = "stdout",
                ["offset"] = 0L,
                ["maxBytes"] = 0,
            });
            if (outputResult.StructuredContent is { } outputJson &&
                (outputJson.GetProperty("text").GetString() ?? string.Empty)
                    .Contains(jobToken, StringComparison.Ordinal))
            {
                observedOutput = true;
            }
        }

        if (!observedOutput)
        {
            throw new InvalidOperationException("job_read_output did not observe stdin-triggered stdout.");
        }
    }
    finally
    {
        var stopResult = await EnsureSuccess(byName["talvora_job_stop"], new()
        {
            ["jobId"] = jobId,
            ["entireProcessTree"] = true,
            ["timeoutSeconds"] = 15,
        });
        if (stopResult.StructuredContent is not { } stopJson ||
            !stopJson.GetProperty("found").GetBoolean() ||
            !stopJson.GetProperty("exited").GetBoolean())
        {
            throw new InvalidOperationException("job_stop did not terminate the smoke job.");
        }

        var deleteJobResult = await EnsureSuccess(byName["talvora_job_delete"], new()
        {
            ["jobId"] = jobId,
            ["stopIfRunning"] = false,
        });
        if (deleteJobResult.StructuredContent is not { } deleteJobJson ||
            !deleteJobJson.GetProperty("found").GetBoolean() ||
            !deleteJobJson.GetProperty("deleted").GetBoolean())
        {
            throw new InvalidOperationException("job_delete did not remove the smoke job metadata.");
        }
    }

    await EnsureError(byName["talvora_job_get"], new()
    {
        ["jobId"] = jobId,
    });

    var gitInfoResult = await EnsureSuccess(byName["talvora_git_info"], new()
    {
        ["repositoryPath"] = repositoryPath,
    });
    if (gitInfoResult.StructuredContent is not { } gitInfoJson ||
        !gitInfoJson.TryGetProperty("root", out var gitRootJson) ||
        string.IsNullOrWhiteSpace(gitRootJson.GetString()) ||
        !gitInfoJson.TryGetProperty("head", out var gitHeadJson) ||
        string.IsNullOrWhiteSpace(gitHeadJson.GetString()))
    {
        throw new InvalidOperationException("git_info did not return repository root and HEAD.");
    }

    var gitRoot = gitRootJson.GetString()!;
    var gitHead = gitHeadJson.GetString()!;

    var gitStatusResult = await EnsureSuccess(byName["talvora_git_status"], new()
    {
        ["repositoryPath"] = repositoryPath,
        ["includeUntracked"] = true,
    });
    if (gitStatusResult.StructuredContent is not { } gitStatusJson ||
        !string.Equals(
            Path.GetFullPath(gitStatusJson.GetProperty("root").GetString() ?? string.Empty),
            Path.GetFullPath(gitRoot),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("git_status returned an unexpected repository root.");
    }

    var gitDiffResult = await EnsureSuccess(byName["talvora_git_diff"], new()
    {
        ["repositoryPath"] = repositoryPath,
        ["revisionRange"] = "HEAD~1..HEAD",
        ["contextLines"] = 2,
    });
    if (gitDiffResult.StructuredContent is not { } gitDiffJson ||
        string.IsNullOrWhiteSpace(gitDiffJson.GetProperty("diff").GetString()) ||
        !(gitDiffJson.GetProperty("diff").GetString() ?? string.Empty)
            .Contains("diff --git ", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("git_diff did not return the committed HEAD~1..HEAD change.");
    }

    var gitLogResult = await EnsureSuccess(byName["talvora_git_log"], new()
    {
        ["repositoryPath"] = repositoryPath,
        ["maxCount"] = 5,
    });
    if (gitLogResult.StructuredContent is not { } gitLogJson ||
        gitLogJson.GetProperty("count").GetInt32() < 1 ||
        !gitLogJson.GetProperty("commits").EnumerateArray().Any(commit =>
            commit.TryGetProperty("commit", out var commitId) &&
            string.Equals(commitId.GetString(), gitHead, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("git_log did not include HEAD.");
    }

    var gitBranchesResult = await EnsureSuccess(byName["talvora_git_branches"], new()
    {
        ["repositoryPath"] = repositoryPath,
        ["includeRemote"] = true,
    });
    if (gitBranchesResult.StructuredContent is not { } gitBranchesJson ||
        gitBranchesJson.GetProperty("count").GetInt32() < 1 ||
        !gitBranchesJson.GetProperty("branches").EnumerateArray().Any(branch =>
            branch.TryGetProperty("current", out var current) && current.GetBoolean()))
    {
        throw new InvalidOperationException("git_branches did not identify the current branch.");
    }

    var gitRunResult = await EnsureSuccess(byName["talvora_git_run"], new()
    {
        ["repositoryPath"] = repositoryPath,
        ["arguments"] = new[] { "rev-parse", "HEAD" },
        ["timeoutSeconds"] = 30,
    });
    if (gitRunResult.StructuredContent is not { } gitRunJson ||
        gitRunJson.GetProperty("exitCode").GetInt32() != 0 ||
        !string.Equals(
            (gitRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
            gitHead,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("git_run rev-parse HEAD did not match git_info HEAD.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerRangeFile,
        ["content"] = "line-one\r\nline-two\r\nline-three",
    });
    var appendTextResult = await EnsureSuccess(byName["talvora_append_text"], new()
    {
        ["path"] = developerRangeFile,
        ["content"] = "\r\nline-four",
        ["appendNewLine"] = false,
    });
    if (appendTextResult.StructuredContent is not { } appendTextJson ||
        appendTextJson.GetProperty("charactersAppended").GetInt32() <= 0)
    {
        throw new InvalidOperationException("append_text did not append content.");
    }

    var rangeResult = await EnsureSuccess(byName["talvora_read_text_range"], new()
    {
        ["path"] = developerRangeFile,
        ["startLine"] = 2,
        ["lineCount"] = 2,
    });
    if (rangeResult.StructuredContent is not { } rangeJson ||
        rangeJson.GetProperty("linesRead").GetInt32() != 2 ||
        !string.Equals(
            rangeJson.GetProperty("text").GetString(),
            "line-two" + Environment.NewLine + "line-three",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("read_text_range returned unexpected lines.");
    }

    var tailResult = await EnsureSuccess(byName["talvora_tail_text"], new()
    {
        ["path"] = developerRangeFile,
        ["lineCount"] = 2,
    });
    if (tailResult.StructuredContent is not { } tailJson ||
        tailJson.GetProperty("totalLines").GetInt32() != 4 ||
        !string.Equals(
            tailJson.GetProperty("text").GetString(),
            "line-three" + Environment.NewLine + "line-four",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("tail_text returned unexpected lines.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerDotenvFile,
        ["content"] = "ALPHA=one\r\nexport BETA=\"two words\"\r\n",
    });

    var dotenvListResult = await EnsureSuccess(byName["talvora_dotenv_list"], new()
    {
        ["path"] = developerDotenvFile,
    });
    if (dotenvListResult.StructuredContent is not { } dotenvListJson ||
        dotenvListJson.GetProperty("count").GetInt32() != 2)
    {
        throw new InvalidOperationException("dotenv_list did not parse two entries.");
    }

    var dotenvGetResult = await EnsureSuccess(byName["talvora_dotenv_get"], new()
    {
        ["path"] = developerDotenvFile,
        ["key"] = "BETA",
    });
    if (dotenvGetResult.StructuredContent is not { } dotenvGetJson ||
        !dotenvGetJson.GetProperty("found").GetBoolean() ||
        !dotenvGetJson.GetProperty("exported").GetBoolean() ||
        !string.Equals(dotenvGetJson.GetProperty("value").GetString(), "two words", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("dotenv_get did not decode the exported quoted value.");
    }

    var dotenvSetResult = await EnsureSuccess(byName["talvora_dotenv_set"], new()
    {
        ["path"] = developerDotenvFile,
        ["key"] = "ALPHA",
        ["value"] = "updated value",
        ["replaceAll"] = true,
    });
    if (dotenvSetResult.StructuredContent is not { } dotenvSetJson ||
        !dotenvSetJson.GetProperty("changed").GetBoolean() ||
        dotenvSetJson.GetProperty("matches").GetInt32() != 1)
    {
        throw new InvalidOperationException("dotenv_set did not update ALPHA.");
    }

    var dotenvUpdatedGet = await EnsureSuccess(byName["talvora_dotenv_get"], new()
    {
        ["path"] = developerDotenvFile,
        ["key"] = "ALPHA",
    });
    if (dotenvUpdatedGet.StructuredContent is not { } dotenvUpdatedJson ||
        !string.Equals(dotenvUpdatedJson.GetProperty("value").GetString(), "updated value", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("dotenv_get did not return the updated value.");
    }

    var dotenvDeleteResult = await EnsureSuccess(byName["talvora_dotenv_delete"], new()
    {
        ["path"] = developerDotenvFile,
        ["key"] = "BETA",
    });
    if (dotenvDeleteResult.StructuredContent is not { } dotenvDeleteJson ||
        !dotenvDeleteJson.GetProperty("changed").GetBoolean() ||
        dotenvDeleteJson.GetProperty("matches").GetInt32() != 1)
    {
        throw new InvalidOperationException("dotenv_delete did not remove BETA.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerIniFile,
        ["content"] = "[app]\r\nmode=dev\r\nport=7000\r\n",
    });

    var iniGetResult = await EnsureSuccess(byName["talvora_ini_get"], new()
    {
        ["path"] = developerIniFile,
        ["section"] = "app",
        ["key"] = "mode",
    });
    if (iniGetResult.StructuredContent is not { } iniGetJson ||
        !iniGetJson.GetProperty("found").GetBoolean() ||
        !string.Equals(iniGetJson.GetProperty("value").GetString(), "dev", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("ini_get did not return app.mode.");
    }

    var iniSetResult = await EnsureSuccess(byName["talvora_ini_set"], new()
    {
        ["path"] = developerIniFile,
        ["section"] = "app",
        ["key"] = "port",
        ["value"] = "7676",
    });
    if (iniSetResult.StructuredContent is not { } iniSetJson ||
        !iniSetJson.GetProperty("changed").GetBoolean() ||
        iniSetJson.GetProperty("matches").GetInt32() != 1)
    {
        throw new InvalidOperationException("ini_set did not update app.port.");
    }

    var iniListResult = await EnsureSuccess(byName["talvora_ini_list"], new()
    {
        ["path"] = developerIniFile,
        ["section"] = "app",
    });
    if (iniListResult.StructuredContent is not { } iniListJson ||
        iniListJson.GetProperty("count").GetInt32() != 2 ||
        !iniListJson.GetProperty("entries").EnumerateArray().Any(entry =>
            string.Equals(entry.GetProperty("key").GetString(), "port", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.GetProperty("value").GetString(), "7676", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("ini_list did not show the updated app.port.");
    }

    var iniDeleteResult = await EnsureSuccess(byName["talvora_ini_delete"], new()
    {
        ["path"] = developerIniFile,
        ["section"] = "app",
        ["key"] = "mode",
    });
    if (iniDeleteResult.StructuredContent is not { } iniDeleteJson ||
        !iniDeleteJson.GetProperty("changed").GetBoolean() ||
        iniDeleteJson.GetProperty("matches").GetInt32() != 1)
    {
        throw new InvalidOperationException("ini_delete did not remove app.mode.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerXmlFile,
        ["content"] = "<root><app mode=\"dev\"><port>7000</port><remove>yes</remove></app></root>",
    });

    var xmlQueryResult = await EnsureSuccess(byName["talvora_xml_query"], new()
    {
        ["path"] = developerXmlFile,
        ["xpath"] = "/root/app/port",
    });
    if (xmlQueryResult.StructuredContent is not { } xmlQueryJson ||
        xmlQueryJson.GetProperty("count").GetInt32() != 1 ||
        !string.Equals(
            xmlQueryJson.GetProperty("nodes").EnumerateArray().Single().GetProperty("value").GetString(),
            "7000",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("xml_query did not return the port element.");
    }

    var xmlSetResult = await EnsureSuccess(byName["talvora_xml_set"], new()
    {
        ["path"] = developerXmlFile,
        ["xpath"] = "/root/app/port",
        ["value"] = "7676",
        ["expectedMatches"] = 1,
    });
    if (xmlSetResult.StructuredContent is not { } xmlSetJson ||
        !xmlSetJson.GetProperty("changed").GetBoolean() ||
        xmlSetJson.GetProperty("matches").GetInt32() != 1)
    {
        throw new InvalidOperationException("xml_set did not update the port element.");
    }

    var xmlAttributeSet = await EnsureSuccess(byName["talvora_xml_set"], new()
    {
        ["path"] = developerXmlFile,
        ["xpath"] = "/root/app/@mode",
        ["value"] = "prod",
        ["expectedMatches"] = 1,
    });
    if (xmlAttributeSet.StructuredContent is not { } xmlAttributeSetJson ||
        !xmlAttributeSetJson.GetProperty("changed").GetBoolean())
    {
        throw new InvalidOperationException("xml_set did not update the mode attribute.");
    }

    var xmlDeleteResult = await EnsureSuccess(byName["talvora_xml_delete"], new()
    {
        ["path"] = developerXmlFile,
        ["xpath"] = "/root/app/remove",
        ["expectedMatches"] = 1,
    });
    if (xmlDeleteResult.StructuredContent is not { } xmlDeleteJson ||
        !xmlDeleteJson.GetProperty("changed").GetBoolean())
    {
        throw new InvalidOperationException("xml_delete did not remove the requested element.");
    }

    var xmlVerifyResult = await EnsureSuccess(byName["talvora_xml_query"], new()
    {
        ["path"] = developerXmlFile,
        ["xpath"] = "concat(/root/app/@mode, ':', /root/app/port)",
    });
    if (xmlVerifyResult.StructuredContent is not { } xmlVerifyJson ||
        !string.Equals(xmlVerifyJson.GetProperty("scalarValue").GetString(), "prod:7676", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("xml_query scalar verification failed.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerJUnitFile,
        ["content"] = "<testsuites><testsuite name=\"smoke\" tests=\"2\" failures=\"1\" errors=\"0\" skipped=\"0\" time=\"0.12\"><testcase classname=\"Smoke\" name=\"Pass\" time=\"0.01\"/><testcase classname=\"Smoke\" name=\"Fail\" time=\"0.02\"><failure message=\"boom\">stack line</failure></testcase></testsuite></testsuites>",
    });

    var reportResult = await EnsureSuccess(byName["talvora_test_report_summary"], new()
    {
        ["path"] = developerJUnitFile,
        ["format"] = "auto",
        ["maxFailures"] = 0,
    });
    if (reportResult.StructuredContent is not { } reportJson ||
        !string.Equals(reportJson.GetProperty("format").GetString(), "junit", StringComparison.OrdinalIgnoreCase) ||
        reportJson.GetProperty("total").GetInt32() != 2 ||
        reportJson.GetProperty("passed").GetInt32() != 1 ||
        reportJson.GetProperty("failed").GetInt32() != 1 ||
        reportJson.GetProperty("failureCount").GetInt32() != 1 ||
        !reportJson.GetProperty("failures").EnumerateArray().Any(failure =>
            (failure.GetProperty("name").GetString() ?? string.Empty).Contains("Smoke.Fail", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("test_report_summary did not parse the JUnit failure.");
    }

    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = developerJsonFile,
        ["content"] = "{\"name\":\"talvora\",\"settings\":{\"mode\":\"dev\"},\"items\":[1,2]}",
    });
    var jsonSetResult = await EnsureSuccess(byName["talvora_json_set"], new()
    {
        ["path"] = developerJsonFile,
        ["pointer"] = "/settings/port",
        ["valueJson"] = "7676",
        ["createMissing"] = true,
        ["indented"] = true,
    });
    if (jsonSetResult.StructuredContent is not { } jsonSetJson ||
        !jsonSetJson.GetProperty("changed").GetBoolean())
    {
        throw new InvalidOperationException("json_set did not report the config change.");
    }

    await EnsureSuccess(byName["talvora_json_set"], new()
    {
        ["path"] = developerJsonFile,
        ["pointer"] = "/items/-",
        ["valueJson"] = "3",
        ["createMissing"] = true,
    });

    var jsonGetResult = await EnsureSuccess(byName["talvora_json_get"], new()
    {
        ["path"] = developerJsonFile,
        ["pointer"] = "/settings/port",
        ["indented"] = false,
    });
    if (jsonGetResult.StructuredContent is not { } jsonGetJson ||
        !jsonGetJson.GetProperty("found").GetBoolean() ||
        !string.Equals(jsonGetJson.GetProperty("valueJson").GetString(), "7676", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("json_get did not return the configured port.");
    }

    var jsonDeleteResult = await EnsureSuccess(byName["talvora_json_delete"], new()
    {
        ["path"] = developerJsonFile,
        ["pointer"] = "/settings/mode",
    });
    if (jsonDeleteResult.StructuredContent is not { } jsonDeleteJson ||
        !jsonDeleteJson.GetProperty("changed").GetBoolean())
    {
        throw new InvalidOperationException("json_delete did not remove the configured mode.");
    }

    var deletedJsonGetResult = await EnsureSuccess(byName["talvora_json_get"], new()
    {
        ["path"] = developerJsonFile,
        ["pointer"] = "/settings/mode",
    });
    if (deletedJsonGetResult.StructuredContent is not { } deletedJsonGetJson ||
        deletedJsonGetJson.GetProperty("found").GetBoolean())
    {
        throw new InvalidOperationException("json_get reported a deleted JSON pointer as present.");
    }

    await EnsureSuccess(byName["talvora_create_directory"], new()
    {
        ["path"] = Path.Combine(developerArchiveSource, "nested"),
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(developerArchiveSource, "a.txt"),
        ["content"] = "archive-a-" + smokeId,
    });
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = Path.Combine(developerArchiveSource, "nested", "b.txt"),
        ["content"] = "archive-b-" + smokeId,
    });

    var archiveCreateResult = await EnsureSuccess(byName["talvora_archive_create"], new()
    {
        ["sourceDirectory"] = developerArchiveSource,
        ["archivePath"] = developerArchiveZip,
        ["overwrite"] = true,
        ["includeBaseDirectory"] = false,
        ["compression"] = "optimal",
    });
    if (archiveCreateResult.StructuredContent is not { } archiveCreateJson ||
        archiveCreateJson.GetProperty("entryCount").GetInt32() < 2)
    {
        throw new InvalidOperationException("archive_create did not create the expected entries.");
    }

    var archiveListResult = await EnsureSuccess(byName["talvora_archive_list"], new()
    {
        ["archivePath"] = developerArchiveZip,
    });
    if (archiveListResult.StructuredContent is not { } archiveListJson ||
        archiveListJson.GetProperty("count").GetInt32() < 2 ||
        !archiveListJson.GetProperty("entries").EnumerateArray().Any(entry =>
            entry.TryGetProperty("fullName", out var fullName) &&
            fullName.GetString() is { } entryName &&
            entryName.Replace('\\', '/').EndsWith("nested/b.txt", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("archive_list did not report the nested entry.");
    }

    var archiveExtractResult = await EnsureSuccess(byName["talvora_archive_extract"], new()
    {
        ["archivePath"] = developerArchiveZip,
        ["destinationDirectory"] = developerArchiveExtract,
        ["overwrite"] = true,
        ["allowOutsideDestination"] = false,
    });
    if (archiveExtractResult.StructuredContent is not { } archiveExtractJson ||
        archiveExtractJson.GetProperty("entriesExtracted").GetInt32() < 2 ||
        !string.Equals(
            await ReadToolText(
                byName["talvora_read_text"],
                Path.Combine(developerArchiveExtract, "nested", "b.txt")),
            "archive-b-" + smokeId,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("archive_extract did not restore the nested payload.");
    }

    var downloadResult = await EnsureSuccess(byName["talvora_http_download"], new()
    {
        ["url"] = "http://127.0.0.1:7676/healthz",
        ["destinationPath"] = developerDownloadFile,
        ["overwrite"] = true,
        ["resume"] = false,
        ["timeoutSeconds"] = 30,
    });
    if (downloadResult.StructuredContent is not { } downloadJson ||
        downloadJson.GetProperty("statusCode").GetInt32() != 200 ||
        downloadJson.GetProperty("fileLength").GetInt64() <= 0 ||
        string.IsNullOrWhiteSpace(downloadJson.GetProperty("sha256").GetString()) ||
        !(await ReadToolText(byName["talvora_read_text"], developerDownloadFile))
            .Contains("Talvora", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("http_download did not persist the Talvora health response.");
    }

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
    try
    {
        if (knowledgeRootsCaptured)
        {
            if (knowledgeRootsWasPresent)
            {
                await EnsureSuccess(byName["talvora_env_set"], new()
                {
                    ["name"] = knowledgeRootsEnvironmentName,
                    ["value"] = knowledgeRootsOriginalValue ?? string.Empty,
                    ["target"] = "process",
                });
            }
            else
            {
                await EnsureSuccess(byName["talvora_env_delete"], new()
                {
                    ["name"] = knowledgeRootsEnvironmentName,
                    ["target"] = "process",
                });
            }
        }
    }
    finally
    {
        if (Directory.Exists(reparseLoop)) Directory.Delete(reparseLoop);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
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


var sqliteInfoResult = await EnsureSuccess(byName["talvora_sqlite_info"], new());
if (sqliteInfoResult.StructuredContent is not { } sqliteInfoJson ||
    string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("providerVersion").GetString()) ||
    string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("sqliteVersion").GetString()) ||
    string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("sqliteSourceId").GetString()))
{
    throw new InvalidOperationException("sqlite info did not return provider/native version metadata.");
}

var sqliteSmokeDirectory = Path.Combine(
    Path.GetTempPath(),
    "Talvora-Sqlite-Smoke-" + smokeId);
var sqliteDatabasePath = Path.Combine(sqliteSmokeDirectory, "app.db");
var sqliteBackupPath = Path.Combine(sqliteSmokeDirectory, "app.backup.db");
Directory.CreateDirectory(sqliteSmokeDirectory);

try
{
    var createTableResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["sql"] = "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, score INTEGER NOT NULL, metadata TEXT NULL);",
        ["transactional"] = true,
        ["createIfMissing"] = true,
    });
    if (createTableResult.StructuredContent is not { } createTableJson ||
        !createTableJson.GetProperty("transactional").GetBoolean())
    {
        throw new InvalidOperationException("sqlite execute did not create the smoke table transactionally.");
    }

    var firstInsertResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["sql"] = "INSERT INTO items(name, score, metadata) VALUES ($name, $score, $metadata);",
        ["parameters"] = new Dictionary<string, object?>
        {
            ["name"] = "alpha",
            ["score"] = 7,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["source"] = "smoke",
                ["active"] = true,
            },
        },
    });
    if (firstInsertResult.StructuredContent is not { } firstInsertJson ||
        firstInsertJson.GetProperty("changes").GetInt64() != 1 ||
        firstInsertJson.GetProperty("lastInsertRowId").GetInt64() <= 0)
    {
        throw new InvalidOperationException("sqlite first parameterized insert did not report one change.");
    }

    var secondInsertResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["sql"] = "INSERT INTO items(name, score, metadata) VALUES ($name, $score, NULL);",
        ["parameters"] = new Dictionary<string, object?>
        {
            ["$name"] = "beta",
            ["$score"] = 12,
        },
    });
    if (secondInsertResult.StructuredContent is not { } secondInsertJson ||
        secondInsertJson.GetProperty("changes").GetInt64() != 1)
    {
        throw new InvalidOperationException("sqlite second parameterized insert did not report one change.");
    }

    var queryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["sql"] = "SELECT id, name, score, metadata FROM items WHERE score >= $min ORDER BY score;",
        ["parameters"] = new Dictionary<string, object?>
        {
            ["min"] = 10,
        },
        ["readOnly"] = true,
        ["maxRows"] = 10,
    });
    if (queryResult.StructuredContent is not { } queryJson ||
        queryJson.GetProperty("rowCount").GetInt32() != 1 ||
        queryJson.GetProperty("truncated").GetBoolean())
    {
        throw new InvalidOperationException("sqlite parameterized query returned an unexpected row count.");
    }

    var queryRows = queryJson.GetProperty("rows");
    var betaRow = queryRows[0].GetProperty("values");
    if (!string.Equals(
            betaRow.GetProperty("name").GetProperty("text").GetString(),
            "beta",
            StringComparison.Ordinal) ||
        betaRow.GetProperty("score").GetProperty("integer").GetInt64() != 12)
    {
        throw new InvalidOperationException("sqlite query did not preserve typed row values.");
    }

    var truncatedQueryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["sql"] = "SELECT id, name FROM items ORDER BY id;",
        ["readOnly"] = true,
        ["maxRows"] = 1,
    });
    if (truncatedQueryResult.StructuredContent is not { } truncatedQueryJson ||
        truncatedQueryJson.GetProperty("rowCount").GetInt32() != 1 ||
        !truncatedQueryJson.GetProperty("truncated").GetBoolean())
    {
        throw new InvalidOperationException("sqlite maxRows truncation contract failed.");
    }

    var schemaResult = await EnsureSuccess(byName["talvora_sqlite_schema"], new()
    {
        ["databasePath"] = sqliteDatabasePath,
        ["objectType"] = "table",
        ["nameLike"] = "item%",
        ["includeInternal"] = false,
    });
    if (schemaResult.StructuredContent is not { } schemaJson ||
        !schemaJson.GetProperty("objects").EnumerateArray().Any(item =>
            string.Equals(
                item.GetProperty("name").GetString(),
                "items",
                StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("sqlite schema did not return the smoke table.");
    }

    var backupResult = await EnsureSuccess(byName["talvora_sqlite_backup"], new()
    {
        ["sourcePath"] = sqliteDatabasePath,
        ["destinationPath"] = sqliteBackupPath,
        ["overwrite"] = false,
    });
    if (backupResult.StructuredContent is not { } backupJson ||
        backupJson.GetProperty("length").GetInt64() <= 0 ||
        backupJson.GetProperty("sha256").GetString()?.Length != 64 ||
        !File.Exists(sqliteBackupPath))
    {
        throw new InvalidOperationException("sqlite online backup did not produce a valid backup file.");
    }

    var backupQueryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
    {
        ["databasePath"] = sqliteBackupPath,
        ["sql"] = "SELECT COUNT(*) AS item_count FROM items;",
        ["readOnly"] = true,
    });
    if (backupQueryResult.StructuredContent is not { } backupQueryJson ||
        backupQueryJson.GetProperty("rowCount").GetInt32() != 1 ||
        backupQueryJson.GetProperty("rows")[0]
            .GetProperty("values")
            .GetProperty("item_count")
            .GetProperty("integer")
            .GetInt64() != 2)
    {
        throw new InvalidOperationException("sqlite backup verification query returned an unexpected count.");
    }
}
finally
{
    try
    {
        if (File.Exists(sqliteBackupPath))
        {
            File.Delete(sqliteBackupPath);
        }

        if (File.Exists(sqliteDatabasePath))
        {
            File.Delete(sqliteDatabasePath);
        }

        if (Directory.Exists(sqliteSmokeDirectory))
        {
            Directory.Delete(sqliteSmokeDirectory, recursive: true);
        }
    }
    catch
    {
    }
}


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

Console.WriteLine("TALVORA MCP SMOKE GREEN");
Console.WriteLine($"endpoint={endpoint}");
Console.WriteLine($"tools={string.Join(',', required)}");