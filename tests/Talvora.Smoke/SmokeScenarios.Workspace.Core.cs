using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceCoreAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var root = context.Root;
        var file = context.File;
        var nestedDirectory = context.NestedDirectory;
        var developerBinaryFile = context.DeveloperBinaryFile;
        var developerPatchFile = context.DeveloperPatchFile;
        var developerProjectRoot = context.DeveloperProjectRoot;
        var developerPackageJson = context.DeveloperPackageJson;
        var interactiveSessionMarker = context.InteractiveSessionMarker;
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
    }
}
