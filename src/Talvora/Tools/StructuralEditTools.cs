using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

[McpServerToolType]
public static class StructuralEditTools
{
    [McpServerTool(
        Name = "talvora_structural_edit",
        Title = "Structural Source Editor",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SourceEditTransactionResult)),
     Description("SPECIALIST structural editor for large/repetitive syntax-shaped transformations across source files. Use this only when AST structure matters or the same transformation repeats broadly; ordinary source/config/repository-document edits still use talvora_apply_patch as the PRIMARY/default editor. Supports either pattern/kind + rewrite or an advanced workspace-relative ast-grep YAML ruleFile with fix. The active ruleFile is a control input and is excluded from transformation targets. Talvora runs verified ast-grep in proposal-only mode against an isolated UTF-8 mirror, cross-checks scalar positions and UTF-8 byte offsets, converts matches to exact UTF-16 revision/range edits, and commits through the same Source Edit WAL/rollback/idempotency engine. ast-grep never writes the live workspace directly.")]
    public static Task<SourceEditTransactionResult> StructuralEdit(
        string workspaceRoot,
        string transactionId,
        string? rewrite = null,
        string? ruleFile = null,
        string? pattern = null,
        string? kind = null,
        string? language = null,
        string? selector = null,
        string? strictness = null,
        string[]? paths = null,
        string[]? globs = null,
        bool includeGenerated = false,
        bool includeHidden = false,
        int threads = 0,
        int maxMatches = 5000,
        int maxFiles = 20000,
        long maxSingleFileBytes = 33554432,
        long maxTotalBytes = 536870912,
        int maxOutputCharacters = 67108864,
        int timeoutSeconds = 120,
        bool validateSyntax = true,
        CancellationToken cancellationToken = default) =>
        AstGrepStructuralEditEngine.ApplyAsync(
            workspaceRoot,
            transactionId,
            rewrite,
            ruleFile,
            pattern,
            kind,
            language,
            selector,
            strictness,
            paths,
            globs,
            includeGenerated,
            includeHidden,
            threads,
            maxMatches,
            maxFiles,
            maxSingleFileBytes,
            maxTotalBytes,
            maxOutputCharacters,
            timeoutSeconds,
            validateSyntax,
            cancellationToken);
}
