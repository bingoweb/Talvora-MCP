using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunApplicationDevelopmentAsync(
        IReadOnlyDictionary<string, McpClientTool> byName,
        string smokeId,
        string repositoryPath)
    {
        var inspection = await EnsureSuccess(
            byName["talvora_workspace_inspect"],
            new()
            {
                ["root"] = repositoryPath,
                ["maxDepth"] = 6,
                ["includeGenerated"] = false,
            });

        if (inspection.StructuredContent is not { } inspectionJson ||
            inspectionJson.GetProperty("count").GetInt32() <= 0 ||
            !inspectionJson.GetProperty("projects").EnumerateArray().Any(project =>
                string.Equals(
                    project.GetProperty("ecosystem").GetString(),
                    "dotnet",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "workspace inspection did not discover the repository .NET projects.");
        }

        var commands = await EnsureSuccess(
            byName["talvora_workspace_commands"],
            new()
            {
                ["root"] = repositoryPath,
                ["maxDepth"] = 6,
                ["includeGenerated"] = false,
            });

        if (commands.StructuredContent is not { } commandsJson ||
            commandsJson.GetProperty("count").GetInt32() <= 0 ||
            !commandsJson.GetProperty("commands").EnumerateArray().Any(command =>
                string.Equals(command.GetProperty("ecosystem").GetString(), "dotnet", StringComparison.Ordinal) &&
                string.Equals(command.GetProperty("purpose").GetString(), "build", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "workspace command inference did not produce a .NET build command.");
        }

        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "Talvora-AppDev-Smoke-" + smokeId);
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            var lcovPath = Path.Combine(fixtureRoot, "coverage.info");
            await File.WriteAllTextAsync(
                lcovPath,
                """
                TN:
                SF:sample.cs
                FNF:2
                FNH:1
                LF:4
                LH:3
                BRF:2
                BRH:1
                end_of_record
                """);

            var coverage = await EnsureSuccess(
                byName["talvora_coverage_summary"],
                new() { ["path"] = lcovPath });

            if (coverage.StructuredContent is not { } coverageJson ||
                !string.Equals(coverageJson.GetProperty("format").GetString(), "lcov", StringComparison.Ordinal) ||
                coverageJson.GetProperty("files").GetInt32() != 1 ||
                coverageJson.GetProperty("lines").GetProperty("covered").GetInt64() != 3 ||
                coverageJson.GetProperty("lines").GetProperty("total").GetInt64() != 4 ||
                coverageJson.GetProperty("branches").GetProperty("covered").GetInt64() != 1 ||
                coverageJson.GetProperty("functions").GetProperty("covered").GetInt64() != 1)
            {
                throw new InvalidOperationException(
                    "coverage summary did not normalize the LCOV fixture correctly.");
            }

            const string diagnosticText =
                "C:\\work\\Program.cs(10,5): error CS1002: ; expected [sample.csproj]\n" +
                "src/main.c:4:2: warning: implicit conversion\n" +
                "error[E0308]: mismatched types\n" +
                " --> src/lib.rs:8:3\n";

            var diagnostics = await EnsureSuccess(
                byName["talvora_diagnostics_parse"],
                new() { ["text"] = diagnosticText });

            if (diagnostics.StructuredContent is not { } diagnosticsJson ||
                diagnosticsJson.GetProperty("count").GetInt32() != 3 ||
                diagnosticsJson.GetProperty("errors").GetInt32() != 2 ||
                diagnosticsJson.GetProperty("warnings").GetInt32() != 1)
            {
                throw new InvalidOperationException(
                    "diagnostics parser did not normalize the mixed compiler fixture.");
            }

            var artifactPath = Path.Combine(fixtureRoot, "artifact.bin");
            await File.WriteAllBytesAsync(artifactPath, [1, 2, 3, 4, 5, 6, 7, 8]);

            var inventory = await EnsureSuccess(
                byName["talvora_artifact_inventory"],
                new()
                {
                    ["root"] = fixtureRoot,
                    ["patterns"] = new[] { "*.bin" },
                    ["recursive"] = false,
                    ["maxResults"] = 10,
                    ["hashAlgorithm"] = "SHA256",
                    ["includeVersionInfo"] = false,
                });

            if (inventory.StructuredContent is not { } inventoryJson ||
                inventoryJson.GetProperty("count").GetInt32() != 1 ||
                inventoryJson.GetProperty("artifacts")[0].GetProperty("length").GetInt64() != 8 ||
                inventoryJson.GetProperty("artifacts")[0].GetProperty("hash").GetString()?.Length != 64)
            {
                throw new InvalidOperationException(
                    "artifact inventory did not hash the fixture artifact correctly.");
            }

            await ProbeOptionalToolchainsAsync(byName, repositoryPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(fixtureRoot))
                {
                    Directory.Delete(fixtureRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task ProbeOptionalToolchainsAsync(
        IReadOnlyDictionary<string, McpClientTool> byName,
        string repositoryPath)
    {
        var java = await EnsureSuccess(byName["talvora_java_info"], new());
        if (GetBoolean(java, "javaFound"))
        {
            await EnsureSuccess(byName["talvora_java_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "-version" },
                ["timeoutSeconds"] = 60,
            });
        }
        if (GetBoolean(java, "javacFound"))
        {
            await EnsureSuccess(byName["talvora_javac_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "-version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var maven = await EnsureSuccess(byName["talvora_maven_info"], new()
        {
            ["workingDirectory"] = repositoryPath,
        });
        if (GetBoolean(maven, "found"))
        {
            await EnsureSuccess(byName["talvora_maven_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var gradle = await EnsureSuccess(byName["talvora_gradle_info"], new()
        {
            ["workingDirectory"] = repositoryPath,
        });
        if (GetBoolean(gradle, "found"))
        {
            await EnsureSuccess(byName["talvora_gradle_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var go = await EnsureSuccess(byName["talvora_go_info"], new());
        if (GetBoolean(go, "found"))
        {
            await EnsureSuccess(byName["talvora_go_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var rust = await EnsureSuccess(byName["talvora_rust_info"], new());
        if (GetBoolean(rust, "rustcFound"))
        {
            await EnsureSuccess(byName["talvora_rustc_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }
        if (GetBoolean(rust, "rustupFound"))
        {
            await EnsureSuccess(byName["talvora_rustup_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }
        if (GetBoolean(rust, "cargoFound"))
        {
            await EnsureSuccess(byName["talvora_cargo_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var pnpm = await EnsureSuccess(byName["talvora_pnpm_info"], new());
        if (GetBoolean(pnpm, "found"))
        {
            await EnsureSuccess(byName["talvora_pnpm_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var yarn = await EnsureSuccess(byName["talvora_yarn_info"], new());
        if (GetBoolean(yarn, "found"))
        {
            await EnsureSuccess(byName["talvora_yarn_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var bun = await EnsureSuccess(byName["talvora_bun_info"], new());
        if (GetBoolean(bun, "found"))
        {
            await EnsureSuccess(byName["talvora_bun_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var flutter = await EnsureSuccess(byName["talvora_flutter_info"], new());
        if (GetBoolean(flutter, "flutterFound"))
        {
            await EnsureSuccess(byName["talvora_flutter_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 90,
            });
        }
        if (GetBoolean(flutter, "dartFound"))
        {
            await EnsureSuccess(byName["talvora_dart_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var android = await EnsureSuccess(byName["talvora_android_sdk_info"], new());
        if (GetString(android, "androidCliExecutable") is not null)
        {
            await EnsureSuccess(byName["talvora_android_cli_run"], new()
            {
                ["arguments"] = new[] { "--version" },
                ["workingDirectory"] = repositoryPath,
                ["timeoutSeconds"] = 60,
            });
        }
        if (GetString(android, "adbExecutable") is not null)
        {
            await EnsureSuccess(byName["talvora_adb_run"], new()
            {
                ["arguments"] = new[] { "version" },
                ["workingDirectory"] = repositoryPath,
                ["timeoutSeconds"] = 60,
            });
        }
        if (GetString(android, "emulatorExecutable") is not null)
        {
            await EnsureSuccess(byName["talvora_emulator_run"], new()
            {
                ["arguments"] = new[] { "-version" },
                ["workingDirectory"] = repositoryPath,
                ["timeoutSeconds"] = 60,
            });
        }

        var github = await EnsureSuccess(byName["talvora_gh_info"], new()
        {
            ["workingDirectory"] = repositoryPath,
        });
        if (GetBoolean(github, "found"))
        {
            await EnsureSuccess(byName["talvora_gh_run"], new()
            {
                ["workingDirectory"] = repositoryPath,
                ["arguments"] = new[] { "--version" },
                ["timeoutSeconds"] = 60,
            });
        }

        var windowsRelease = await EnsureSuccess(
            byName["talvora_windows_release_tools_info"],
            new());
        if (GetBoolean(windowsRelease, "found"))
        {
            foreach (var toolName in new[]
            {
                "talvora_signtool_run",
                "talvora_rc_run",
                "talvora_mt_run",
                "talvora_makeappx_run",
                "talvora_makepri_run",
            })
            {
                await EnsureSuccess(byName[toolName], new()
                {
                    ["workingDirectory"] = repositoryPath,
                    ["arguments"] = new[] { "/?" },
                    ["timeoutSeconds"] = 60,
                });
            }
        }
    }

    private static bool GetBoolean(CallToolResult result, string name) =>
        result.StructuredContent is { } json &&
        json.TryGetProperty(name, out var property) &&
        property.ValueKind == System.Text.Json.JsonValueKind.True;

    private static string? GetString(CallToolResult result, string name)
    {
        if (result.StructuredContent is not { } json ||
            !json.TryGetProperty(name, out var property) ||
            property.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
