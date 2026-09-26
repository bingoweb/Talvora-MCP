using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunEnvironmentAsync(IReadOnlyDictionary<string, McpClientTool> byName, string smokeId)
    {
        var processEnvironmentName = "TALVORA_SMOKE_PROCESS_" + smokeId.ToUpperInvariant();
        var userEnvironmentName = "TALVORA_SMOKE_USER_" + smokeId.ToUpperInvariant();
        var machineEnvironmentName = "TALVORA_SMOKE_MACHINE_" + smokeId.ToUpperInvariant();
        var userProbePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonDocuments),
            "Talvora-Smoke-User-Env-" + smokeId + ".txt");
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

            var powershell = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            File.Delete(userProbePath);
            await EnsureSuccess(
                byName["talvora_user_process_start"],
                new()
                {
                    ["executable"] = powershell,
                    ["arguments"] = new[]
                    {
                        "-NoLogo",
                        "-NoProfile",
                        "-NonInteractive",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-Command",
                        "$v=[Environment]::GetEnvironmentVariable($args[0],[EnvironmentVariableTarget]::User); if($null -eq $v){$v='<null>'}; [IO.File]::WriteAllText($args[1],$v,[Text.UTF8Encoding]::new($false))",
                        userEnvironmentName,
                        userProbePath,
                    },
                    ["visible"] = false,
                    ["newConsole"] = false,
                });

            var probeDeadline =
                DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(userProbePath) &&
                   DateTime.UtcNow < probeDeadline)
            {
                await Task.Delay(100);
            }

            if (!File.Exists(userProbePath) ||
                !string.Equals(
                    await File.ReadAllTextAsync(userProbePath),
                    "user-" + smokeId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "target=user did not persist into the logged-on Windows user profile.");
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
            File.Delete(userProbePath);
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
    }
}
