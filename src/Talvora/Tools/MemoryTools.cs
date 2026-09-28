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
        string? retentionClass = null,
        string? claimKey = null,
        CancellationToken cancellationToken = default)
    {
        var item = await TalvoraMemoryRuntime.Store.RememberAsync(
            scope, category, title, content, project, session,
            importance, confidence, source, sourceReference,
            expiresAtUtc, retentionClass, claimKey, cancellationToken);
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
        Name = "talvora_memory_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryListResult)),
     Description("List active durable Talvora memories with optional project, scope, category and updated-time filters. Results are bounded and ordered by most recently updated.")]
    public static Task<TalvoraMemoryListResult> List(
        string? scope = null,
        string? project = null,
        string? category = null,
        DateTimeOffset? updatedAfterUtc = null,
        DateTimeOffset? updatedBeforeUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ListAsync(
            scope,
            project,
            category,
            updatedAfterUtc,
            updatedBeforeUtc,
            limit,
            cancellationToken);

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
        string? retentionClass = null,
        string? claimKey = null,
        string? supersededBy = null,
        CancellationToken cancellationToken = default)
    {
        var item = await TalvoraMemoryRuntime.Store.UpdateAsync(
            id, category, title, content, importance, confidence,
            source, sourceReference, expiresAtUtc,
            retentionClass, claimKey, supersededBy,
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

    [McpServerTool(
        Name = "talvora_memory_supersede",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemorySupersedeResult)),
     Description("Mark one stale memory as superseded by another existing memory in the same scope/project/session boundary. The stale row remains auditable but normal retrieval excludes it.")]
    public static Task<TalvoraMemorySupersedeResult> Supersede(
        string staleId,
        string replacementId,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.SupersedeAsync(
            staleId,
            replacementId,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_consolidate",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryConsolidateResult)),
     Description("Consolidate exact normalized duplicate active memories inside optional scope/project/session/category filters. The best-authority record wins; duplicates remain as auditable superseded rows.")]
    public static Task<TalvoraMemoryConsolidateResult> Consolidate(
        string? scope = null,
        string? project = null,
        string? session = null,
        string? category = null,
        int maxScan = 5000,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ConsolidateAsync(
            scope,
            project,
            session,
            category,
            maxScan,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_diagnostics",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryDiagnosticsResult)),
     Description("Report Talvora memory database integrity, size, active/expired/superseded counts, and exact normalized duplicate-group count.")]
    public static Task<TalvoraMemoryDiagnosticsResult> Diagnostics(
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.DiagnosticsAsync(cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_embedding_status",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryEmbeddingStatusResult)),
     Description("Report the local semantic embedding provider/model plus active, current, and missing-or-stale embedding counts. Search remains FTS5-only when the provider is unavailable.")]
    public static Task<TalvoraMemoryEmbeddingStatusResult> EmbeddingStatus(
        string? project = null,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.EmbeddingStatusAsync(
            project,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_reembed",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryReembedResult)),
     Description("Re-embed one bounded batch of active memories using the canonical local model. The workflow is idempotent and resumable; repeated calls process remaining missing/model-stale vectors. force=true recomputes the selected batch.")]
    public static Task<TalvoraMemoryReembedResult> Reembed(
        string? project = null,
        int batchSize = 100,
        bool force = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ReembedAsync(
            project,
            batchSize,
            force,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_backup",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryBackupResult)),
     Description("Create a validated online SQLite backup of Talvora durable memory. The live WAL database remains online. Existing destinations require overwrite=true.")]
    public static Task<TalvoraMemoryBackupResult> Backup(
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.BackupAsync(
            destinationPath,
            overwrite,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_restore_stage",
        Destructive = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryRestoreStageResult)),
     Description("Validate a Talvora memory backup, copy it through SQLite Backup API into a canonical pending-restore file, and stage it for atomic application on the next Talvora service start. The live database is never overwritten by this call.")]
    public static Task<TalvoraMemoryRestoreStageResult> RestoreStage(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.StageRestoreAsync(
            sourcePath,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_restore_status",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryRestoreStatusResult)),
     Description("Report whether a validated Talvora memory restore is staged and waiting for the next Talvora service start.")]
    public static Task<TalvoraMemoryRestoreStatusResult> RestoreStatus(
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.RestoreStatusAsync(
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_session_close",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemorySessionCloseResult)),
     Description("Close one logical session using a short summary plus structured memory candidates. Raw chat is not stored. Session id is idempotent. Candidates remain pending by default; policy auto-promotion happens only when autoPromote=true and strict thresholds pass.")]
    public static Task<TalvoraMemorySessionCloseResult> SessionClose(
        string sessionId,
        string summary,
        IReadOnlyList<TalvoraMemoryCandidateInput>? candidates = null,
        string? project = null,
        bool autoPromote = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.CloseSessionAsync(
            sessionId,
            summary,
            project,
            candidates,
            autoPromote,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_candidate_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryCandidateListResult)),
     Description("List pending, promoted, or rejected memory candidates with optional session/project/status filters.")]
    public static Task<TalvoraMemoryCandidateListResult> CandidateList(
        string? sessionId = null,
        string? project = null,
        string? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ListCandidatesAsync(
            sessionId,
            project,
            status,
            limit,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_candidate_promote",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryCandidateResolutionResult)),
     Description("Promote one pending candidate to durable memory. The transition is idempotent; a second call returns the existing promoted memory instead of creating a duplicate.")]
    public static Task<TalvoraMemoryCandidateResolutionResult> CandidatePromote(
        string candidateId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.PromoteCandidateAsync(
            candidateId,
            reason,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_candidate_reject",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryCandidateResolutionResult)),
     Description("Reject one pending memory candidate without deleting its audit record. The transition is idempotent.")]
    public static Task<TalvoraMemoryCandidateResolutionResult> CandidateReject(
        string candidateId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.RejectCandidateAsync(
            candidateId,
            reason,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_session_forget",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemorySessionForgetResult)),
     Description("Permanently delete one stored session summary and all candidate audit rows. Optionally also delete durable memories that were promoted from those candidates.")]
    public static Task<TalvoraMemorySessionForgetResult> SessionForget(
        string sessionId,
        bool deletePromotedMemories = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ForgetSessionAsync(
            sessionId,
            deletePromotedMemories,
            cancellationToken);
}
