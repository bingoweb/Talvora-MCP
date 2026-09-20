namespace Talvora.SourceEditing;

public sealed record SourceEditRoutingRule(
    string Intent,
    string Tool,
    string Guidance);

public sealed record SourceEditRoutingGuide(
    string PrimaryTool,
    string ReadTool,
    string ExactRangeTool,
    string StructuralTool,
    string SemanticTool,
    string Policy,
    IReadOnlyList<SourceEditRoutingRule> Rules,
    IReadOnlyList<string> AvoidForNormalSourceEditing);

internal static class SourceEditRoutingContract
{
    public const string ServerInstructions =
        "For source editing inside recognized development workspaces, talvora_apply_patch is the PRIMARY/default mutation tool for ordinary code, config, and repository-document changes, including ordinary C# changes and one or many files with add/update/delete/move operations. Use talvora_structural_edit only for large/repetitive syntax-shaped transformations where AST structure matters. Use talvora_semantic_edit only for C# operations that genuinely require Roslyn symbol/semantic identity, currently solution/project-aware symbol rename; it is not a general-purpose C# editor. Read existing files with talvora_read_source before ordinary mutation so edits carry SHA-256 revisions. Use talvora_apply_edits only when edits are already expressed as exact zero-based UTF-16 ranges/revisions, typically generated programmatically; do not select it merely because multiple files change. Generic copy/move, Git working-tree mutation, archive extraction, and HTTP download-to-source destinations are policy-routed to talvora_apply_patch by default; their explicitAdmin option is only for deliberate administration. If tool choice is unclear, call talvora_source_edit_guide instead of trial-calling mutators. Legacy text/config/file mutators are compatibility surfaces. PowerShell and process execution are general administration tools; use the Source Edit tools for normal workspace source changes.";

    public const string ReadSourceDescription =
        "Canonical source-aware streaming read that returns a bounded text window plus whole-file SHA-256 revision, encoding, newline policy, and workspace classification. lineCount=0 still reads toward EOF but the response remains bounded by maxCharacters; continue with nextStartLine/nextStartCharacter when endReached=false. Use it before mutating an existing source/config/repository-document file with talvora_apply_patch, before supplying exact generated ranges to talvora_apply_edits, and before anchoring talvora_semantic_edit to a C# source revision.";

    public const string ApplyPatchDescription =
        "PRIMARY/default source editor for ordinary development-workspace mutations: code (including ordinary C# edits), config, repository documents, one or many files, add/update/delete/move, and external unified diffs. Read existing files with talvora_read_source for revisions; prefer this over other mutators unless the task is a large/repetitive syntax-shaped transformation, where talvora_structural_edit is specialized, a C# operation genuinely requires Roslyn symbol identity, where talvora_semantic_edit is specialized, or edits are already exact zero-based UTF-16 ranges, where talvora_apply_edits is specialized. Both patch input formats use the same exact transaction/WAL core; no fuzzy or partial application is permitted.";

    public const string ApplyEditsDescription =
        "Specialized exact-range source editor for changes already represented as zero-based UTF-16 ranges plus revisions, typically generated or coordinated programmatically. Do not choose it merely because multiple files change; ordinary agent-authored source/config/repository-document changes should use talvora_apply_patch.";

    public const string GuideDescription =
        "Return Talvora's canonical source-edit routing contract. Call this when source-edit tool choice is unclear instead of trial-calling mutation tools.";

    public const string SemanticEditDescription =
        "SPECIALIST C# semantic editor for operations that genuinely require Roslyn symbol identity. The current capability is solution/project-aware symbol rename from an exact revisioned source position. Do NOT use this for ordinary C# edits; talvora_apply_patch remains PRIMARY/default. Do NOT use it for broad repetitive syntax-shaped rewrites; use talvora_structural_edit. Roslyn only produces an in-memory proposal; Talvora revalidates exact source revisions and commits changed documents through the existing Source Edit WAL/idempotency/rollback transaction engine.";

    public const string LegacyMutationRouting =
        "For development-workspace source/config/text changes, talvora_apply_patch is the PRIMARY/default editor. talvora_structural_edit is only for broad repetitive AST-shaped transformations, talvora_semantic_edit is only for C# operations requiring Roslyn symbol identity, and talvora_apply_edits is only for already-known exact ranges. Do not trial-call this compatibility mutator.";

    public const string EscapeHatchRouting =
        "This is a general administration capability and is not the normal source editor. For development-workspace source changes, use talvora_apply_patch as the PRIMARY/default editor; use talvora_apply_edits only when exact ranges are already known or generated.";

    public static SourceEditRoutingGuide CreateGuide() =>
        new(
            PrimaryTool: "talvora_apply_patch",
            ReadTool: "talvora_read_source",
            ExactRangeTool: "talvora_apply_edits",
            StructuralTool: "talvora_structural_edit",
            SemanticTool: "talvora_semantic_edit",
            Policy:
                "Default to talvora_apply_patch for normal source mutation, including ordinary C# edits. Use talvora_structural_edit only for broad repetitive AST-shaped transformations. Use talvora_semantic_edit only for C# operations that require Roslyn symbol/semantic identity. Use talvora_apply_edits only for already-known/generated exact ranges. Never discover the editor by mutator trial-and-error.",
            Rules:
            [
                new(
                    "Read an existing source/config/repository-document file before changing it",
                    "talvora_read_source",
                    "Obtain the SHA-256 revision and preserve encoding/newline metadata before mutation."),
                new(
                    "Ordinary localized or broad source/config/repository-document change, one file or many",
                    "talvora_apply_patch",
                    "This is the primary/default editor. Multi-file scope alone is not a reason to switch tools."),
                new(
                    "Add, update, delete, move, copy, restore, extract, or download development-workspace source files",
                    "talvora_apply_patch",
                    "Keep ordinary source-file lifecycle and content changes inside the same revision/WAL/rollback transaction model; use explicitAdmin on generic tools only for deliberate administration."),
                new(
                    "Apply an external Git/traditional unified diff",
                    "talvora_apply_patch",
                    "Use inputFormat=unified-diff with expectedRevisions for existing files; it still normalizes into the Talvora transaction core."),
                new(
                    "Large or repetitive syntax-shaped transformation across source files where AST structure matters",
                    "talvora_structural_edit",
                    "Use the structural specialist. It generates proposals in an isolated mirror and commits through the same Talvora transaction engine; ast-grep never writes the live workspace directly."),
                new(
                    "C# symbol-aware rename or another operation that genuinely requires solution/project semantic identity",
                    "talvora_semantic_edit",
                    "Use the Roslyn semantic specialist only when syntax/text identity is insufficient. Ordinary C# edits remain on talvora_apply_patch."),
                new(
                    "Apply edits already represented as exact zero-based UTF-16 ranges/revisions",
                    "talvora_apply_edits",
                    "Use the range tool only when those exact coordinates already exist or were generated programmatically."),
                new(
                    "Tool choice is ambiguous",
                    "talvora_source_edit_guide",
                    "Consult this contract rather than probing mutation tools and reacting to failures."),
            ],
            AvoidForNormalSourceEditing:
            [
                "talvora_write_text",
                "talvora_replace_text",
                "talvora_append_text",
                "talvora_write_bytes",
                "typed JSON/YAML/TOML/dotenv/INI/XML mutators",
                "talvora_delete for development-workspace source files",
                "talvora_copy for development-workspace source destinations",
                "talvora_move for development-workspace source removal or destination ingress",
                "talvora_git_run working-tree mutation commands unless explicitAdmin=true",
                "talvora_archive_extract into development-workspace source targets unless explicitAdmin=true",
                "talvora_http_download into development-workspace source targets unless explicitAdmin=true",
                "talvora_run_powershell",
                "talvora_run_process",
            ]);
}
