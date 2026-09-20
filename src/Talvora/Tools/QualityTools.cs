using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraCoverageMetric(
    long Covered,
    long Total,
    double? Percent);

public sealed record TalvoraCoverageSummaryResponse(
    string Path,
    string Format,
    int Files,
    TalvoraCoverageMetric Lines,
    TalvoraCoverageMetric Branches,
    TalvoraCoverageMetric Functions);

public sealed record TalvoraDiagnosticEntry(
    string Severity,
    string? Code,
    string Message,
    string? File,
    int? Line,
    int? Column,
    string Source);

public sealed record TalvoraDiagnosticsResponse(
    string Source,
    int Count,
    int Errors,
    int Warnings,
    int Infos,
    bool Truncated,
    IReadOnlyList<TalvoraDiagnosticEntry> Diagnostics,
    long DiagnosticOffset = 0,
    long? NextDiagnosticOffset = null);

public sealed record TalvoraArtifactEntry(
    string Path,
    string RelativePath,
    string Extension,
    long Length,
    DateTime LastWriteTimeUtc,
    string HashAlgorithm,
    string Hash,
    string? FileVersion,
    string? ProductVersion);

public sealed record TalvoraArtifactInventoryResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraArtifactEntry> Artifacts,
    IReadOnlyList<string> Errors,
    long ResultOffset = 0,
    long? NextResultOffset = null);

[McpServerToolType]
public static partial class QualityTools
{
    internal const int AbsoluteDiagnosticResults = 20_000;
    internal const int AbsoluteArtifactResults = 10_000;
    internal const int AbsoluteArtifactErrors = 1_000;
}
