using ModelContextProtocol;
using System.Text;

namespace Talvora.SourceEditing;

public static class SourceEditCodes
{
    public const string PolicyViolation = "SOURCE_EDIT_POLICY_VIOLATION";
    public const string WorkspaceNotFound = "SOURCE_EDIT_WORKSPACE_NOT_FOUND";
    public const string PathOutsideWorkspace = "SOURCE_EDIT_PATH_OUTSIDE_WORKSPACE";
    public const string ReparsePointUnsupported = "SOURCE_EDIT_REPARSE_POINT_UNSUPPORTED";
    public const string UnsupportedEncoding = "SOURCE_EDIT_UNSUPPORTED_ENCODING";
    public const string ReadOnly = "SOURCE_EDIT_READ_ONLY";
    public const string CrossVolumeMoveUnsupported = "SOURCE_EDIT_CROSS_VOLUME_MOVE_UNSUPPORTED";
    public const string PatchParseError = "PATCH_PARSE_ERROR";
    public const string PatchContextNotFound = "PATCH_CONTEXT_NOT_FOUND";
    public const string PatchContextAmbiguous = "PATCH_CONTEXT_AMBIGUOUS";
    public const string PatchOverlappingEdits = "PATCH_OVERLAPPING_EDITS";
    public const string ExpectedRevisionMismatch = "EXPECTED_REVISION_MISMATCH";
    public const string Conflict = "SOURCE_EDIT_CONFLICT";
    public const string ValidationFailed = "SOURCE_VALIDATION_FAILED";
    public const string ResourceLimit = "SOURCE_EDIT_RESOURCE_LIMIT";
    public const string TransactionIdInvalid = "TRANSACTION_ID_INVALID";
    public const string TransactionIdReuseMismatch = "TRANSACTION_ID_REUSE_MISMATCH";
    public const string TransactionIdExpired = "TRANSACTION_ID_EXPIRED";
    public const string TransactionRolledBack = "TRANSACTION_ROLLED_BACK";
    public const string TransactionRecoveryRequired = "TRANSACTION_RECOVERY_REQUIRED";
    public const string IoFailure = "SOURCE_EDIT_IO_FAILURE";
    public const string StructuralNoMatches = "STRUCTURAL_NO_MATCHES";
    public const string StructuralProposalInvalid = "STRUCTURAL_PROPOSAL_INVALID";
    public const string StructuralToolchainUnavailable = "STRUCTURAL_TOOLCHAIN_UNAVAILABLE";
    public const string StructuralToolFailed = "STRUCTURAL_TOOL_FAILED";
    public const string SemanticInputInvalid = "SEMANTIC_INPUT_INVALID";
    public const string SemanticMsBuildUnavailable = "SEMANTIC_MSBUILD_UNAVAILABLE";
    public const string SemanticWorkspaceLoadFailed = "SEMANTIC_WORKSPACE_LOAD_FAILED";
    public const string SemanticScopeIncomplete = "SEMANTIC_SCOPE_INCOMPLETE";
    public const string SemanticScopeAmbiguous = "SEMANTIC_SCOPE_AMBIGUOUS";
    public const string SemanticCompilationInvalid = "SEMANTIC_COMPILATION_INVALID";
    public const string SemanticDocumentNotFound = "SEMANTIC_DOCUMENT_NOT_FOUND";
    public const string SemanticDocumentAmbiguous = "SEMANTIC_DOCUMENT_AMBIGUOUS";
    public const string SemanticSnapshotMismatch = "SEMANTIC_SOURCE_SNAPSHOT_MISMATCH";
    public const string SemanticSymbolNotFound = "SEMANTIC_SYMBOL_NOT_FOUND";
    public const string SemanticSymbolAmbiguous = "SEMANTIC_SYMBOL_AMBIGUOUS";
    public const string SemanticConflict = "SEMANTIC_CONFLICT";
    public const string SemanticProposalInvalid = "SEMANTIC_PROPOSAL_INVALID";
    public const string SemanticNoChanges = "SEMANTIC_NO_CHANGES";
    public const string SemanticTimeout = "SEMANTIC_TIMEOUT";
}

public sealed record TalvoraSourceReadResponse(
    string Path,
    string? WorkspaceRoot,
    string Classification,
    long Length,
    string Revision,
    string Encoding,
    string Newline,
    bool HasFinalNewline,
    int StartLine,
    int LinesRead,
    bool EndReached,
    string Text,
    int StartCharacter,
    int NextStartLine,
    int NextStartCharacter,
    bool ResponseLimited,
    int ResponseLimitCharacters);

public sealed class SourceEditTextRangeInput
{
    public int StartLine { get; init; }
    public int StartCharacter { get; init; }
    public int EndLine { get; init; }
    public int EndCharacter { get; init; }
    public required string NewText { get; init; }
    public string? ExpectedText { get; init; }
}

public sealed class SourceEditChangeInput
{
    public required string Operation { get; init; }
    public required string Path { get; init; }
    public string? DestinationPath { get; init; }
    public string? ExpectedRevision { get; init; }
    public string? Content { get; init; }
    public string? Encoding { get; init; }
    public string? Newline { get; init; }
    public IReadOnlyList<SourceEditTextRangeInput>? Edits { get; init; }
}

public sealed record SourceEditValidationResult(
    string Path,
    string Validator,
    string Status,
    string? Message);

public sealed record SourceEditWarning(
    string Code,
    string Message,
    string? Path);

public sealed record SourceEditError(
    string Code,
    string Message,
    string? Path,
    IReadOnlyDictionary<string, string>? Details);

public sealed record SourceEditFileReceipt(
    string Operation,
    string? OldPath,
    string? NewPath,
    string? BeforeRevision,
    string? AfterRevision,
    long BeforeBytes,
    long AfterBytes);

public sealed record SourceEditTransactionResult(
    bool Success,
    string TransactionId,
    string Status,
    bool Replayed,
    string? WorkspaceRoot,
    string? RequestHash,
    IReadOnlyList<SourceEditFileReceipt> Files,
    IReadOnlyList<SourceEditValidationResult> Validation,
    IReadOnlyList<SourceEditWarning> Warnings,
    SourceEditError? Error)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? AdapterReceiptJson { get; init; }

    public static SourceEditTransactionResult Rejected(
        string transactionId,
        string? workspaceRoot,
        string? requestHash,
        SourceEditDomainException exception) =>
        new(
            false,
            transactionId,
            "rejected",
            false,
            workspaceRoot,
            requestHash,
            [],
            [],
            [],
            new SourceEditError(
                exception.Code,
                exception.Message,
                exception.Path,
                exception.Details));
}

public sealed record SemanticEditDiagnostic(
    string Source,
    string Severity,
    string Code,
    string Message,
    string? ProjectPath,
    string? DocumentPath);

public sealed record SemanticWorkspaceReceipt(
    string InputPath,
    string InputKind,
    string MsBuildVersion,
    string MsBuildPath,
    int ProjectCount,
    int DocumentCount);

public sealed record SemanticSymbolReceipt(
    string Name,
    string NewName,
    string Kind,
    string Display,
    string IdentityHash,
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<string> DeclarationPaths);

public sealed record SemanticEditTransactionResult(
    bool Success,
    string Operation,
    bool Replayed,
    SemanticWorkspaceReceipt? Workspace,
    SemanticSymbolReceipt? Symbol,
    IReadOnlyList<SemanticEditDiagnostic> Diagnostics,
    SourceEditTransactionResult Transaction);

public sealed class SourceEditDomainException : McpException
{
    public SourceEditDomainException(
        string code,
        string message,
        string? path = null,
        IReadOnlyDictionary<string, string>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
        Details = details;
    }

    public string Code { get; }
    public string? Path { get; }
    public IReadOnlyDictionary<string, string>? Details { get; }
}

internal enum SourceEditOperationKind
{
    Update,
    Add,
    Delete,
    Move,
}

internal sealed record SourceTextEncodingDescriptor(
    string Name,
    Encoding Encoding,
    byte[] Preamble);

internal sealed record SourceTextDocument(
    string Text,
    SourceTextEncodingDescriptor Encoding,
    string Newline,
    bool HasFinalNewline,
    long OriginalByteLength);

internal sealed record SourceFileSnapshot(
    string FullPath,
    string RelativePath,
    bool Exists,
    string? Revision,
    long Length,
    FileAttributes Attributes,
    DateTime? LastWriteTimeUtc,
    SourceTextDocument? Document);

internal sealed record NormalizedSourceEditChange(
    SourceEditOperationKind Operation,
    string RelativePath,
    string FullPath,
    string? DestinationRelativePath,
    string? DestinationFullPath,
    string? ExpectedRevision,
    SourceFileSnapshot? Before,
    SourceFileSnapshot? DestinationBefore,
    SourceTextDocument? ProposedDocument);

internal sealed record NormalizedSourceEditChangeSet(
    int SchemaVersion,
    string TransactionId,
    string WorkspaceRoot,
    string InputKind,
    bool ValidateSyntax,
    string RequestHash,
    IReadOnlyList<NormalizedSourceEditChange> Changes);

internal sealed record SourceEditPreparedFile(
    int Index,
    NormalizedSourceEditChange Change,
    string? StagePath,
    string? BackupPath,
    string? AfterRevision,
    long AfterBytes,
    IReadOnlyList<string> CreatedDirectories);

internal sealed record SourceEditDirectoryIdentity(
    string Path,
    uint VolumeSerialNumber,
    ulong FileId);

internal sealed record SourceEditPreparedTransaction(
    NormalizedSourceEditChangeSet ChangeSet,
    IReadOnlyList<SourceEditPreparedFile> Files,
    IReadOnlyList<SourceEditValidationResult> Validation,
    IReadOnlyList<SourceEditDirectoryIdentity> DirectoryIdentities);

internal static class SourceEditRevision
{
    public static string Format(ReadOnlySpan<byte> hash) =>
        "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
        {
            normalized = normalized[7..];
        }

        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ExpectedRevisionMismatch,
                "Revision must be a SHA-256 value in the form sha256:<64 hex characters>.");
        }

        return "sha256:" + normalized;
    }
}
