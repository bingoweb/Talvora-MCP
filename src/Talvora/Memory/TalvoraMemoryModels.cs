namespace Talvora.Memory;

public sealed record TalvoraMemoryItem(
    string Id,
    string Scope,
    string? Project,
    string? Session,
    string Category,
    string Title,
    string Content,
    double Importance,
    double Confidence,
    string? Source,
    string? SourceReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? SupersededBy,
    string RetentionClass,
    string? ClaimKey);

public sealed record TalvoraMemorySearchHit(
    TalvoraMemoryItem Item,
    double Rank,
    double SourceAuthority);

public sealed record TalvoraMemorySearchResult(
    string Query,
    int Count,
    IReadOnlyList<TalvoraMemorySearchHit> Items);

public sealed record TalvoraMemoryContextResult(
    string Query,
    int Count,
    int CharacterBudget,
    string Context,
    IReadOnlyList<TalvoraMemorySearchHit> Items);

public sealed record TalvoraMemoryMutationResult(
    bool Success,
    string Id,
    TalvoraMemoryItem? Item);

public sealed record TalvoraMemoryForgetResult(bool Found, bool Deleted, string Id);

public sealed record TalvoraMemorySupersedeResult(
    bool Success,
    TalvoraMemoryItem? Stale,
    TalvoraMemoryItem? Replacement);

public sealed record TalvoraMemoryConsolidationGroup(
    string WinnerId,
    IReadOnlyList<string> SupersededIds);

public sealed record TalvoraMemoryConsolidateResult(
    int Scanned,
    int DuplicateGroups,
    int SupersededCount,
    IReadOnlyList<TalvoraMemoryConsolidationGroup> Groups);

public sealed record TalvoraMemoryDiagnosticsResult(
    string DatabasePath,
    long DatabaseBytes,
    string Integrity,
    int TotalCount,
    int ActiveCount,
    int ExpiredCount,
    int SupersededCount,
    int DuplicateGroups);

public sealed record TalvoraMemoryCandidateInput(
    string Category,
    string Title,
    string Content,
    double Importance = 0.5,
    double Confidence = 1.0,
    string? Source = null,
    string? SourceReference = null,
    string? ClaimKey = null,
    string? RetentionClass = null,
    string? TargetScope = null);

public sealed record TalvoraMemoryCandidate(
    string Id,
    string SessionId,
    string? Project,
    string TargetScope,
    string Category,
    string Title,
    string Content,
    double Importance,
    double Confidence,
    string? Source,
    string? SourceReference,
    string? ClaimKey,
    string RetentionClass,
    double PromotionScore,
    string Recommendation,
    string Status,
    string? PromotedMemoryId,
    string? ResolutionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record TalvoraMemorySessionCloseResult(
    string SessionId,
    string? Project,
    string Summary,
    bool Replayed,
    int CandidateCount,
    IReadOnlyList<TalvoraMemoryCandidate> Candidates);

public sealed record TalvoraMemoryCandidateListResult(
    int Count,
    IReadOnlyList<TalvoraMemoryCandidate> Items);

public sealed record TalvoraMemoryCandidateResolutionResult(
    bool Success,
    bool Replayed,
    TalvoraMemoryCandidate? Candidate,
    TalvoraMemoryItem? Memory);

public sealed record TalvoraMemorySessionForgetResult(
    bool Found,
    string SessionId,
    int CandidateCount,
    int DeletedPromotedMemories);

public sealed record TalvoraLearningPattern(
    string Fingerprint,
    string? Project,
    string PatternKind,
    string Title,
    string Content,
    string SourceTools,
    int OccurrenceCount,
    double Confidence,
    double Importance,
    string Status,
    string? PromotedMemoryId,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc);

public sealed record TalvoraLearningPatternListResult(
    int Count,
    IReadOnlyList<TalvoraLearningPattern> Items);

public sealed record TalvoraLearningStatusResult(
    string? Project,
    int ObservationCount7Days,
    int PendingPatterns,
    int PromotedPatterns,
    int SuppressedPatterns,
    int ActiveSuppressions);

public sealed record TalvoraLearningSuppressionResult(
    bool Suppressed,
    string? Project,
    string? ToolName,
    string? Reason);

public sealed record TalvoraLearningPatternResolutionResult(
    bool Success,
    bool Replayed,
    TalvoraLearningPattern? Pattern,
    TalvoraMemoryItem? Memory);

public sealed record TalvoraLearningDecisionResult(
    TalvoraMemoryCandidate Candidate,
    TalvoraMemoryItem? Memory);

public sealed record TalvoraLearningPatternForgetResult(
    bool Found,
    string Fingerprint,
    bool DeletedPromotedMemory);

