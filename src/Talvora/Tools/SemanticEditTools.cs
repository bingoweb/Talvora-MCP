using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

[McpServerToolType]
public static class SemanticEditTools
{
    [McpServerTool(
        Name = "talvora_semantic_edit",
        Title = "C# Semantic Rename",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SemanticEditTransactionResult)),
     Description(SourceEditRoutingContract.SemanticEditDescription)]
    public static Task<SemanticEditTransactionResult> SemanticEdit(
        string workspaceRoot,
        string transactionId,
        string solutionOrProjectPath,
        string documentPath,
        int line,
        int character,
        string expectedRevision,
        string newName,
        string? projectPath = null,
        string? expectedSymbolName = null,
        bool renameOverloads = false,
        bool renameInStrings = false,
        bool renameInComments = false,
        int maxProjects = 1024,
        int maxDocuments = 100000,
        int maxChangedDocuments = 10000,
        long maxTotalChangedCharacters = 536870912,
        int maxDiagnostics = 500,
        int timeoutSeconds = 120,
        bool validateSyntax = true,
        CancellationToken cancellationToken = default) =>
        RoslynSemanticEditEngine.ApplyRenameAsync(
            workspaceRoot,
            transactionId,
            solutionOrProjectPath,
            documentPath,
            line,
            character,
            expectedRevision,
            newName,
            projectPath,
            expectedSymbolName,
            renameOverloads,
            renameInStrings,
            renameInComments,
            maxProjects,
            maxDocuments,
            maxChangedDocuments,
            maxTotalChangedCharacters,
            maxDiagnostics,
            timeoutSeconds,
            validateSyntax,
            cancellationToken);
}
