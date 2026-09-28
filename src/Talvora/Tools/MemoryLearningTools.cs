using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Memory;

namespace Talvora.Tools;

[McpServerToolType]
public static class MemoryLearningTools
{
    [McpServerTool(
        Name = "talvora_memory_learning_status",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningStatusResult)),
     Description("Inspect automatic-learning observations, pending/promoted/suppressed pattern counts, and active suppressions. No raw tool arguments or outputs are stored by automatic learning.")]
    public static Task<TalvoraLearningStatusResult> Status(
        string? project = null,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.LearningStatusAsync(
            project,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_learning_pattern_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningPatternListResult)),
     Description("List coalesced automatic-learning patterns with optional project/status filters.")]
    public static Task<TalvoraLearningPatternListResult> PatternList(
        string? project = null,
        string? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ListLearningPatternsAsync(
            project,
            status,
            limit,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_learning_pattern_promote",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningPatternResolutionResult)),
     Description("Explicitly promote one pending verified learning pattern into durable project lesson memory. Repeated calls are idempotent.")]
    public static Task<TalvoraLearningPatternResolutionResult> PatternPromote(
        string fingerprint,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.PromoteLearningPatternAsync(
            fingerprint,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_learning_pattern_forget",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningPatternForgetResult)),
     Description("Forget one automatic-learning pattern and optionally delete the durable memory promoted from it.")]
    public static Task<TalvoraLearningPatternForgetResult> PatternForget(
        string fingerprint,
        bool deletePromotedMemory = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.ForgetLearningPatternAsync(
            fingerprint,
            deletePromotedMemory,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_learning_suppress",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningSuppressionResult)),
     Description("Enable or remove automatic-learning suppression globally, for one project, for one allowlisted learning tool, or for a project+tool combination.")]
    public static Task<TalvoraLearningSuppressionResult> Suppress(
        bool suppressed,
        string? project = null,
        string? toolName = null,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.SetLearningSuppressionAsync(
            project,
            toolName,
            suppressed,
            reason,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_learning_decision",
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraLearningDecisionResult)),
     Description("Capture an explicit user/project decision as an auditable memory candidate, optionally promoting it immediately. Secret-like content is rejected.")]
    public static Task<TalvoraLearningDecisionResult> Decision(
        string title,
        string content,
        string? project = null,
        double importance = 0.9,
        double confidence = 1.0,
        string? claimKey = null,
        bool promote = false,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.RecordLearningDecisionAsync(
            title,
            content,
            project,
            importance,
            confidence,
            claimKey,
            promote,
            cancellationToken);
}
