using System.Text.Json;

internal static partial class SmokeScenarios
{
    internal static async Task RunKnowledgeS6LiveAsync(string endpoint)
    {
        await using var client = await CreateClientAsync(endpoint);
        const string envName = "TALVORA_KNOWLEDGE_ROOTS";

        async Task<JsonElement> Call(string name, Dictionary<string, object?> args)
        {
            var result = await client.CallToolAsync(name, args);
            if (result.IsError is true || result.StructuredContent is not { } structured)
            {
                throw new InvalidOperationException(
                    "S6 live MCP call failed: " + name);
            }
            return structured.Clone();
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("S6 live: " + message);
            }
        }

        var previous = await Call("talvora_env_get", new()
        {
            ["name"] = envName,
            ["target"] = "process",
        });
        var hadPrior = previous.GetProperty("found").GetBoolean();
        var oldValue = hadPrior ? previous.GetProperty("value").GetString() : null;

        var scratch = Path.Combine(Path.GetTempPath(),
            "Talvora-S6-Mcp-" + Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(scratch, "allowed");
        var denied = Path.Combine(scratch, "denied");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(denied);
        var configured = false;

        try
        {
            const string marker = "TALVORA_S6_MCP_MARKER_CE2";
            var inside = Path.Combine(allowed, "inside-s6.txt");
            var outside = Path.Combine(denied, "outside-s6.txt");
            await File.WriteAllTextAsync(inside, marker);
            await File.WriteAllTextAsync(outside, marker);

            await Call("talvora_env_set", new()
            {
                ["name"] = envName,
                ["value"] = allowed,
                ["target"] = "process",
            });
            configured = true;

            var search = await Call("search", new() { ["query"] = marker });
            var found = search.GetProperty("results");
            Check(found.GetArrayLength() == 1 &&
                string.Equals(
                    found[0].GetProperty("id").GetString(), inside,
                    StringComparison.OrdinalIgnoreCase),
                "search crossed the configured root");

            var fetchInside = await Call("fetch", new() { ["id"] = inside });
            Check(fetchInside.GetProperty("text").GetString() == marker,
                "authorized fetch failed");

            var fetchedOutside = await client.CallToolAsync("fetch",
                new Dictionary<string, object?> { ["id"] = outside });
            Check(fetchedOutside.IsError is true,
                "an outside-root fetch unexpectedly succeeded");
            Check(fetchedOutside.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .Any(block => block.Text.Contains("talvora_read_text_range",
                    StringComparison.Ordinal)),
                "outside-root error did not include the Turkish reader guidance");

            var link = Path.Combine(allowed, "link-s6.txt");
            try
            {
                File.CreateSymbolicLink(link, outside);
                var fetchedLink = await client.CallToolAsync("fetch",
                    new Dictionary<string, object?> { ["id"] = link });
                Check(fetchedLink.IsError is true,
                    "an out-of-root symlink was read");
            }
            catch (Exception ex) when (ex is
                UnauthorizedAccessException or IOException)
            {
                Console.WriteLine("SKIP optional live symlink fixture: " +
                    ex.GetType().Name);
            }

            var offline = Path.Combine(allowed, "offline-s6.txt");
            await File.WriteAllTextAsync(offline, "TALVORA_S6_OFFLINE_CE2");
            var original = File.GetAttributes(offline);
            try
            {
                File.SetAttributes(offline, original | FileAttributes.Offline);
                var offlineSearch = await Call("search", new()
                {
                    ["query"] = "TALVORA_S6_OFFLINE_CE2",
                });
                Check(offlineSearch.GetProperty("results").GetArrayLength() == 0,
                    "search hydrated an Offline placeholder");
                var offlineFetch = await client.CallToolAsync("fetch",
                    new Dictionary<string, object?> { ["id"] = offline });
                Check(offlineFetch.IsError is true,
                    "fetch hydrated an Offline placeholder");
            }
            finally
            {
                File.SetAttributes(offline, original);
            }

            Console.WriteLine("TALVORA S6 KNOWLEDGE LIVE GREEN");
        }
        finally
        {
            try
            {
                if (configured)
                {
                    await Call(hadPrior ? "talvora_env_set" : "talvora_env_delete",
                        hadPrior
                            ? new() { ["name"] = envName,
                                ["target"] = "process", ["value"] = oldValue ?? "" }
                            : new() { ["name"] = envName,
                                ["target"] = "process" });
                }
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
    }
}
