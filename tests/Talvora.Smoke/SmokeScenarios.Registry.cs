using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunRegistryAsync(IReadOnlyDictionary<string, McpClientTool> byName, string smokeId)
    {
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
                subKeys.ValueKind != System.Text.Json.JsonValueKind.Array ||
                !listJson.TryGetProperty("count", out var listCount) ||
                listCount.GetInt32() != 6 ||
                !listJson.TryGetProperty("totalEntries", out var totalEntries) ||
                totalEntries.GetInt32() != 6 ||
                !listJson.TryGetProperty("truncated", out var listTruncated) ||
                listTruncated.GetBoolean())
            {
                throw new InvalidOperationException("registry list did not return subKeys.");
            }
            var subKeyNames = subKeys.EnumerateArray().Select(item => item.GetString()).ToArray();
            if (!subKeyNames.SequenceEqual(new[] { "Beta", "Zulu", "alpha" }, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("registry subkeys were not returned in deterministic ordinal order.");
            }

            var firstRegistryPage = await EnsureSuccess(byName["talvora_registry_list"], new()
            {
                ["hive"] = "HKCU",
                ["path"] = registryPath,
                ["view"] = "default",
                ["maxResults"] = 2,
                ["resultOffset"] = 0,
            });
            if (firstRegistryPage.StructuredContent is not { } firstRegistryPageJson ||
                firstRegistryPageJson.GetProperty("count").GetInt32() != 2 ||
                !firstRegistryPageJson.GetProperty("truncated").GetBoolean() ||
                firstRegistryPageJson.GetProperty("nextResultOffset").GetInt32() != 2 ||
                firstRegistryPageJson.GetProperty("subKeys").GetArrayLength() != 2)
            {
                throw new InvalidOperationException("registry list first page was not bounded correctly.");
            }

            var secondRegistryPage = await EnsureSuccess(byName["talvora_registry_list"], new()
            {
                ["hive"] = "HKCU",
                ["path"] = registryPath,
                ["view"] = "default",
                ["maxResults"] = 2,
                ["resultOffset"] = 2,
            });
            if (secondRegistryPage.StructuredContent is not { } secondRegistryPageJson ||
                secondRegistryPageJson.GetProperty("count").GetInt32() != 2 ||
                !secondRegistryPageJson.GetProperty("truncated").GetBoolean() ||
                secondRegistryPageJson.GetProperty("nextResultOffset").GetInt32() != 4 ||
                secondRegistryPageJson.GetProperty("subKeys").GetArrayLength() != 1 ||
                secondRegistryPageJson.GetProperty("values").GetArrayLength() != 1)
            {
                throw new InvalidOperationException("registry list continuation page was not deterministic.");
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
    }
}
