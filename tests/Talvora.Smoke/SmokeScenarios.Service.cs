using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunServiceAsync(IReadOnlyDictionary<string, McpClientTool> byName)
    {
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
                !eventItem.TryGetProperty("recordId", out _) ||
                !eventItem.TryGetProperty("messageTruncated", out _))
            {
                throw new InvalidOperationException("event log query returned an incomplete structured event.");
            }
        }

        await EnsureError(byName["talvora_eventlog_query"], new()
        {
            ["logName"] = "System",
            ["xpath"] = "*[System[(EventID=99999999)]]",
            ["maxEvents"] = 501,
            ["newestFirst"] = true,
        });
        
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
    }
}
