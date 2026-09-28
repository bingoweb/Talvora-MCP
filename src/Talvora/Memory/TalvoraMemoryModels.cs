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

