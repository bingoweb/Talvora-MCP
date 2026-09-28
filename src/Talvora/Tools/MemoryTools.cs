using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Memory;

namespace Talvora.Tools;

[McpServerToolType]
public static class MemoryTools
{
    [McpServerTool(
        Name = "talvora_memory_remember",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryMutationResult)),
     Description("Store one explicit durable Talvora memory. V1 does not automatically persist chat history.")]
    public static async Task<TalvoraMemoryMutationResult> Remember(
        string scope,
        string category,
        string title,
        string content,
        string? project = null,
        string? session = null,
        double importance = 0.5,
        double confidence = 1.0,
        string? source = null,
        string? sourceReference = null,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var item = await TalvoraMemoryRuntime.Store.RememberAsync(
            scope, category, title, content, project, session,
            importance, confidence, source, sourceReference,
            expiresAtUtc, cancellationToken);
        return new TalvoraMemoryMutationResult(true, item.Id, item);
    }

    [McpServerTool(
        Name = "talvora_memory_search",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemorySearchResult)),
     Description("Search Talvora durable local memory with FTS5 and optional strict scope filters. Expired and superseded memories are omitted.")]
    public static Task<TalvoraMemorySearchResult> Search(
        string query,
        string? scope = null,
        string? project = null,
        string? session = null,
        string? category = null,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.SearchAsync(
            query, scope, project, session, category, limit, cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_get",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryMutationResult)),
     Description("Get one durable Talvora memory by id.")]
    public static async Task<TalvoraMemoryMutationResult> Get(
        string id,
        CancellationToken cancellationToken = default)
    {
        var item =
            await TalvoraMemoryRuntime.Store.GetAsync(id, cancellationToken);
        return new TalvoraMemoryMutationResult(item is not null, id, item);
    }

    [McpServerTool(
        Name = "talvora_memory_update",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryMutationResult)),
     Description("Update one durable Talvora memory. Only supplied fields change; supersededBy can retire stale memory in favor of another existing memory.")]
    public static async Task<TalvoraMemoryMutationResult> Update(
        string id,
        string? category = null,
        string? title = null,
        string? content = null,
        double? importance = null,
        double? confidence = null,
        string? source = null,
        string? sourceReference = null,
        DateTimeOffset? expiresAtUtc = null,
        string? supersededBy = null,
        CancellationToken cancellationToken = default)
    {
        var item = await TalvoraMemoryRuntime.Store.UpdateAsync(
            id, category, title, content, importance, confidence,
            source, sourceReference, expiresAtUtc, supersededBy,
            cancellationToken);
        return new TalvoraMemoryMutationResult(item is not null, id, item);
    }

    [McpServerTool(
        Name = "talvora_memory_forget",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryForgetResult)),
     Description("Permanently delete one durable Talvora memory by id.")]
    public static async Task<TalvoraMemoryForgetResult> Forget(
        string id,
        CancellationToken cancellationToken = default)
    {
        var deleted =
            await TalvoraMemoryRuntime.Store.ForgetAsync(id, cancellationToken);
        return new TalvoraMemoryForgetResult(deleted, deleted, id);
    }

    [McpServerTool(
        Name = "talvora_memory_context",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryContextResult)),
     Description("Retrieve a bounded prompt-ready context block from Talvora memory using FTS5 plus scope filters.")]
    public static Task<TalvoraMemoryContextResult> Context(
        string query,
        string? scope = null,
        string? project = null,
        string? session = null,
        string? category = null,
        int maxItems = 12,
        int maxCharacters = 12000,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ContextAsync(
            query, scope, project, session, category,
            maxItems, maxCharacters, cancellationToken);
}
