namespace Talvora.Memory;

public sealed record TalvoraMemoryHandoffCandidate(
    string Id,
    string Category,
    string Title,
    string Content,
    double Importance,
    double Confidence,
    string? Source,
    DateTimeOffset UpdatedAtUtc,
    string? ClaimKey);

public sealed record TalvoraMemoryHandoffCandidatesResult(
    string Project,
    int Count,
    IReadOnlyList<TalvoraMemoryHandoffCandidate> Candidates,
    string SuggestedMarkdown);

public sealed record TalvoraMemoryHandoffReviewIssue(
    string Kind,
    string Severity,
    string Detail,
    string? MemoryId = null);

public sealed record TalvoraMemoryHandoffReviewResult(
    string Project,
    string HandoffPath,
    bool HandoffExists,
    string? GitBranch,
    string? GitHead,
    string? RuntimeSourceCommit,
    int IssueCount,
    IReadOnlyList<TalvoraMemoryHandoffReviewIssue> Issues,
    string SuggestedMarkdown);
