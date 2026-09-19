using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunStructuredConfigAsync(IReadOnlyDictionary<string, McpClientTool> byName, string smokeId)
    {
        var structuredConfigDirectory = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Smoke-StructuredConfig-" + smokeId);
        Directory.CreateDirectory(structuredConfigDirectory);
        
        var yamlPath = Path.Combine(structuredConfigDirectory, "compose.yaml");
        var yamlBackupPath = yamlPath + ".bak";
        var tomlPath = Path.Combine(structuredConfigDirectory, "pyproject.toml");
        
        try
        {
            await File.WriteAllTextAsync(
                yamlPath,
                """
                services:
                  api:
                    image: "example/api:1"
                    ports:
                      - 8080
                features:
                  - name: alpha
                    enabled: true
                """);
        
            var yamlGetResult = await EnsureSuccess(byName["talvora_yaml_get"], new()
            {
                ["path"] = yamlPath,
                ["pointer"] = "/services/api/image",
                ["indented"] = false,
            });
        
            if (yamlGetResult.StructuredContent is not { } yamlGetJson ||
                !yamlGetJson.GetProperty("found").GetBoolean() ||
                !string.Equals(
                    yamlGetJson.GetProperty("valueJson").GetString(),
                    "\"example/api:1\"",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("YAML get did not return the expected nested value.");
            }
        
            await EnsureSuccess(byName["talvora_yaml_set"], new()
            {
                ["path"] = yamlPath,
                ["pointer"] = "/services/api/image",
                ["valueJson"] = "\"example/api:2\"",
                ["createMissing"] = true,
                ["createBackup"] = true,
            });
        
            await EnsureSuccess(byName["talvora_yaml_set"], new()
            {
                ["path"] = yamlPath,
                ["pointer"] = "/services/api/environment/ASPNETCORE_ENVIRONMENT",
                ["valueJson"] = "\"Development\"",
                ["createMissing"] = true,
                ["createBackup"] = false,
            });
        
            await EnsureSuccess(byName["talvora_yaml_delete"], new()
            {
                ["path"] = yamlPath,
                ["pointer"] = "/features/0/enabled",
                ["createBackup"] = false,
            });
        
            var yamlRootResult = await EnsureSuccess(byName["talvora_yaml_get"], new()
            {
                ["path"] = yamlPath,
                ["pointer"] = "",
                ["indented"] = false,
            });
        
            if (yamlRootResult.StructuredContent is not { } yamlRootJson ||
                !yamlRootJson.GetProperty("found").GetBoolean())
            {
                throw new InvalidOperationException("YAML root get failed after mutation.");
            }
        
            using (var yamlDocument = System.Text.Json.JsonDocument.Parse(
                yamlRootJson.GetProperty("valueJson").GetString()
                    ?? throw new InvalidOperationException("YAML root returned no JSON value.")))
            {
                var yamlRoot = yamlDocument.RootElement;
                var api = yamlRoot
                    .GetProperty("services")
                    .GetProperty("api");
        
                if (!string.Equals(
                        api.GetProperty("image").GetString(),
                        "example/api:2",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        api.GetProperty("environment")
                            .GetProperty("ASPNETCORE_ENVIRONMENT")
                            .GetString(),
                        "Development",
                        StringComparison.Ordinal) ||
                    yamlRoot.GetProperty("features")[0]
                        .TryGetProperty("enabled", out _))
                {
                    throw new InvalidOperationException("YAML mutation round-trip was not preserved.");
                }
            }
        
            if (!File.Exists(yamlBackupPath))
            {
                throw new InvalidOperationException("YAML mutation did not create the requested backup.");
            }
        
            await File.WriteAllTextAsync(
                tomlPath,
                """
                title = "Talvora Smoke"
        
                [database]
                host = "localhost"
                port = 5432
        
                [tool.talvora]
                enabled = true
        
                [[servers]]
                name = "alpha"
                port = 7001
        
                [[servers]]
                name = "beta"
                port = 7002
                """);
        
            var tomlGetResult = await EnsureSuccess(byName["talvora_toml_get"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "/servers/1/name",
                ["indented"] = false,
            });
        
            if (tomlGetResult.StructuredContent is not { } tomlGetJson ||
                !tomlGetJson.GetProperty("found").GetBoolean() ||
                !string.Equals(
                    tomlGetJson.GetProperty("valueJson").GetString(),
                    "\"beta\"",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("TOML get did not return the expected array-of-tables value.");
            }
        
            await EnsureSuccess(byName["talvora_toml_set"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "/database/port",
                ["valueJson"] = "6432",
                ["createMissing"] = true,
                ["createBackup"] = false,
            });
        
            await EnsureSuccess(byName["talvora_toml_set"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "/servers/0/port",
                ["valueJson"] = "7101",
                ["createMissing"] = true,
                ["createBackup"] = false,
            });
        
            await EnsureSuccess(byName["talvora_toml_set"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "/tool/talvora/tags",
                ["valueJson"] = "[\"dev\",\"mcp\"]",
                ["createMissing"] = true,
                ["createBackup"] = false,
            });
        
            await EnsureSuccess(byName["talvora_toml_delete"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "/tool/talvora/enabled",
                ["createBackup"] = false,
            });
        
            var tomlRootResult = await EnsureSuccess(byName["talvora_toml_get"], new()
            {
                ["path"] = tomlPath,
                ["pointer"] = "",
                ["indented"] = false,
            });
        
            if (tomlRootResult.StructuredContent is not { } tomlRootJson ||
                !tomlRootJson.GetProperty("found").GetBoolean())
            {
                throw new InvalidOperationException("TOML root get failed after mutation.");
            }
        
            using (var tomlDocument = System.Text.Json.JsonDocument.Parse(
                tomlRootJson.GetProperty("valueJson").GetString()
                    ?? throw new InvalidOperationException("TOML root returned no JSON value.")))
            {
                var tomlRoot = tomlDocument.RootElement;
        
                if (tomlRoot.GetProperty("database").GetProperty("port").GetInt64() != 6432 ||
                    tomlRoot.GetProperty("servers")[0].GetProperty("port").GetInt64() != 7101 ||
                    !string.Equals(
                        tomlRoot.GetProperty("servers")[1].GetProperty("name").GetString(),
                        "beta",
                        StringComparison.Ordinal) ||
                    tomlRoot.GetProperty("tool").GetProperty("talvora")
                        .TryGetProperty("enabled", out _) ||
                    tomlRoot.GetProperty("tool").GetProperty("talvora")
                        .GetProperty("tags").GetArrayLength() != 2)
                {
                    throw new InvalidOperationException("TOML mutation round-trip was not preserved.");
                }
            }
        
            var reparsedToml = await File.ReadAllTextAsync(tomlPath);
            if (!reparsedToml.Contains("[[servers]]", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("TOML array-of-tables serialization was not preserved.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(structuredConfigDirectory))
                {
                    Directory.Delete(structuredConfigDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
