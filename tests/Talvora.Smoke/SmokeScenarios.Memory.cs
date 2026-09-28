using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunMemoryAsync(
        IReadOnlyDictionary<string, McpClientTool> byName,
        string smokeId)
    {
        var token = "memorysmoke" + smokeId[..12];
        var project = "Talvora-Smoke-" + smokeId;
        var otherProject = project + "-Other";
        var createdIds = new List<string>();

        try
        {
            var first = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "decision",
                    ["title"] = "Memory smoke alpha",
                    ["content"] = $"{token} alpha durable project memory",
                    ["importance"] = 0.8,
                    ["confidence"] = 1.0,
                    ["source"] = "smoke",
                });
            var firstJson =
                first.StructuredContent
                ?? throw new InvalidOperationException(
                    "memory remember returned no structured content.");
            var firstId =
                firstJson.GetProperty("id").GetString()
                ?? throw new InvalidOperationException(
                    "memory remember returned no id.");
            createdIds.Add(firstId);

            var unrelated = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = otherProject,
                    ["category"] = "fact",
                    ["title"] = "Memory smoke unrelated",
                    ["content"] = $"{token} unrelated project memory",
                    ["source"] = "smoke",
                });
            var unrelatedId =
                unrelated.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "second memory remember returned no id.");
            createdIds.Add(unrelatedId);

            var expired = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "fact",
                    ["title"] = "Memory smoke expired",
                    ["content"] = $"{token} expired memory",
                    ["expiresAtUtc"] =
                        DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
                    ["source"] = "smoke",
                });
            var expiredId =
                expired.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "expired memory remember returned no id.");
            createdIds.Add(expiredId);

            var scopedSearch = await EnsureSuccess(
                byName["talvora_memory_search"],
                new()
                {
                    ["query"] = token,
                    ["scope"] = "project",
                    ["project"] = project,
                    ["limit"] = 20,
                });
            var searchJson =
                scopedSearch.StructuredContent
                ?? throw new InvalidOperationException(
                    "memory search returned no structured content.");
            if (searchJson.GetProperty("count").GetInt32() != 1 ||
                !string.Equals(
                    searchJson.GetProperty("items")[0]
                        .GetProperty("item")
                        .GetProperty("id")
                        .GetString(),
                    firstId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory search did not enforce project/expiry isolation.");
            }

            var update = await EnsureSuccess(
                byName["talvora_memory_update"],
                new()
                {
                    ["id"] = firstId,
                    ["title"] = "Memory smoke beta",
                    ["content"] = $"{token} beta updated durable project memory",
                    ["importance"] = 0.9,
                });
            if (update.StructuredContent is not { } updateJson ||
                !string.Equals(
                    updateJson.GetProperty("item")
                        .GetProperty("title")
                        .GetString(),
                    "Memory smoke beta",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory update did not return updated content.");
            }

            var updatedSearch = await EnsureSuccess(
                byName["talvora_memory_search"],
                new()
                {
                    ["query"] = "beta",
                    ["project"] = project,
                    ["limit"] = 10,
                });
            if (updatedSearch.StructuredContent is not { } updatedJson ||
                updatedJson.GetProperty("count").GetInt32() != 1 ||
                !string.Equals(
                    updatedJson.GetProperty("items")[0]
                        .GetProperty("item")
                        .GetProperty("id")
                        .GetString(),
                    firstId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory FTS index did not reflect update.");
            }

            var context = await EnsureSuccess(
                byName["talvora_memory_context"],
                new()
                {
                    ["query"] = "beta",
                    ["project"] = project,
                    ["maxItems"] = 5,
                    ["maxCharacters"] = 512,
                });
            if (context.StructuredContent is not { } contextJson ||
                contextJson.GetProperty("count").GetInt32() != 1 ||
                contextJson.GetProperty("context").GetString() is not { } text ||
                text.Length is 0 or > 512 ||
                !text.Contains(firstId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory context bounding contract failed.");
            }

            var authorityInferred = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "fact",
                    ["title"] = "Authority inferred",
                    ["content"] = $"{token} authority-order inferred",
                    ["source"] = "inferred",
                });
            var authorityInferredId =
                authorityInferred.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "authority inferred memory returned no id.");
            createdIds.Add(authorityInferredId);

            var authorityRuntime = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "fact",
                    ["title"] = "Authority runtime",
                    ["content"] = $"{token} authority-order runtime",
                    ["source"] = "runtime",
                });
            var authorityRuntimeId =
                authorityRuntime.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "authority runtime memory returned no id.");
            createdIds.Add(authorityRuntimeId);

            var authoritySearch = await EnsureSuccess(
                byName["talvora_memory_search"],
                new()
                {
                    ["query"] = "authority-order",
                    ["project"] = project,
                    ["limit"] = 10,
                });
            if (authoritySearch.StructuredContent is not { } authorityJson ||
                !string.Equals(
                    authorityJson.GetProperty("items")[0]
                        .GetProperty("item")
                        .GetProperty("id")
                        .GetString(),
                    authorityRuntimeId,
                    StringComparison.Ordinal) ||
                authorityJson.GetProperty("items")[0]
                    .GetProperty("sourceAuthority")
                    .GetDouble() < 0.99)
            {
                throw new InvalidOperationException(
                    "memory source-authority ordering failed.");
            }

            var duplicateLow = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "lesson",
                    ["title"] = "Duplicate memory",
                    ["content"] = $"{token} duplicate quality memory",
                    ["source"] = "inferred",
                });
            var duplicateLowId =
                duplicateLow.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "duplicate low-authority memory returned no id.");
            createdIds.Add(duplicateLowId);

            var duplicateHigh = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "lesson",
                    ["title"] = "  duplicate   memory ",
                    ["content"] = $"{token}   duplicate quality memory",
                    ["source"] = "user",
                });
            var duplicateHighId =
                duplicateHigh.StructuredContent
                    ?.GetProperty("id")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "duplicate high-authority memory returned no id.");
            createdIds.Add(duplicateHighId);

            var diagnosticsBefore = await EnsureSuccess(
                byName["talvora_memory_diagnostics"],
                new());
            if (diagnosticsBefore.StructuredContent is not { } diagnosticsBeforeJson ||
                !string.Equals(
                    diagnosticsBeforeJson.GetProperty("integrity").GetString(),
                    "ok",
                    StringComparison.OrdinalIgnoreCase) ||
                diagnosticsBeforeJson.GetProperty("duplicateGroups").GetInt32() < 1)
            {
                throw new InvalidOperationException(
                    "memory diagnostics did not detect duplicate health state.");
            }

            var consolidate = await EnsureSuccess(
                byName["talvora_memory_consolidate"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "lesson",
                });
            if (consolidate.StructuredContent is not { } consolidateJson ||
                consolidateJson.GetProperty("duplicateGroups").GetInt32() != 1 ||
                consolidateJson.GetProperty("supersededCount").GetInt32() != 1 ||
                !string.Equals(
                    consolidateJson.GetProperty("groups")[0]
                        .GetProperty("winnerId")
                        .GetString(),
                    duplicateHighId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory duplicate consolidation did not select the highest-authority winner.");
            }

            var stale = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "decision",
                    ["title"] = "Old decision",
                    ["content"] = $"{token} supersession-old",
                    ["source"] = "user",
                });
            var staleId =
                stale.StructuredContent?.GetProperty("id").GetString()
                ?? throw new InvalidOperationException(
                    "stale memory returned no id.");
            createdIds.Add(staleId);

            var replacement = await EnsureSuccess(
                byName["talvora_memory_remember"],
                new()
                {
                    ["scope"] = "project",
                    ["project"] = project,
                    ["category"] = "decision",
                    ["title"] = "New decision",
                    ["content"] = $"{token} supersession-new",
                    ["source"] = "user",
                });
            var replacementId =
                replacement.StructuredContent?.GetProperty("id").GetString()
                ?? throw new InvalidOperationException(
                    "replacement memory returned no id.");
            createdIds.Add(replacementId);

            var supersede = await EnsureSuccess(
                byName["talvora_memory_supersede"],
                new()
                {
                    ["staleId"] = staleId,
                    ["replacementId"] = replacementId,
                });
            if (supersede.StructuredContent is not { } supersedeJson ||
                !supersedeJson.GetProperty("success").GetBoolean() ||
                !string.Equals(
                    supersedeJson.GetProperty("stale")
                        .GetProperty("supersededBy")
                        .GetString(),
                    replacementId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "memory supersession workflow failed.");
            }

            var staleSearch = await EnsureSuccess(
                byName["talvora_memory_search"],
                new()
                {
                    ["query"] = "supersession-old",
                    ["project"] = project,
                });
            if (staleSearch.StructuredContent is not { } staleSearchJson ||
                staleSearchJson.GetProperty("count").GetInt32() != 0)
            {
                throw new InvalidOperationException(
                    "superseded memory remained in normal retrieval.");
            }

            var forget = await EnsureSuccess(
                byName["talvora_memory_forget"],
                new() { ["id"] = firstId });
            if (forget.StructuredContent is not { } forgetJson ||
                !forgetJson.GetProperty("deleted").GetBoolean())
            {
                throw new InvalidOperationException(
                    "memory forget did not delete the target.");
            }
            createdIds.Remove(firstId);

            var afterForget = await EnsureSuccess(
                byName["talvora_memory_search"],
                new()
                {
                    ["query"] = "beta",
                    ["project"] = project,
                    ["limit"] = 10,
                });
            if (afterForget.StructuredContent is not { } afterForgetJson ||
                afterForgetJson.GetProperty("count").GetInt32() != 0)
            {
                throw new InvalidOperationException(
                    "forgotten memory remained searchable.");
            }
        }
        finally
        {
            foreach (var id in createdIds)
            {
                try
                {
                    await EnsureSuccess(
                        byName["talvora_memory_forget"],
                        new() { ["id"] = id });
                }
                catch
                {
                }
            }
        }
    }
}

