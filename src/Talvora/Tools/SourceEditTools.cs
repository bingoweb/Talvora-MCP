using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

[McpServerToolType]
public static class SourceEditTools
{
    [McpServerTool(
        Name = "talvora_read_source",
        Title = "Read Source With Revision",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSourceReadResponse)),
     Description(SourceEditRoutingContract.ReadSourceDescription)]
    public static Task<TalvoraSourceReadResponse> ReadSource(
        string path,
        int startLine = 1,
        int lineCount = 0,
        int startCharacter = 0,
        int maxCharacters = SourceTextStreamingReader.DefaultReadMaxCharacters,
        CancellationToken cancellationToken = default) =>
        SourceEditRuntime.Engine.ReadSourceAsync(
            path,
            startLine,
            startCharacter,
            lineCount,
            maxCharacters,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_apply_patch",
        Title = "Primary Source Editor",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SourceEditTransactionResult)),
     Description(SourceEditRoutingContract.ApplyPatchDescription)]
    public static Task<SourceEditTransactionResult> ApplyPatch(
        string workspaceRoot,
        string transactionId,
        string patch,
        string inputFormat = "talvora",
        Dictionary<string, string>? expectedRevisions = null,
        bool validateSyntax = true,
        CancellationToken cancellationToken = default) =>
        SourceEditRuntime.Engine.ApplyPatchAsync(
            workspaceRoot,
            transactionId,
            patch,
            inputFormat,
            expectedRevisions,
            validateSyntax,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_apply_edits",
        Title = "Exact Range Source Editor",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SourceEditTransactionResult)),
     Description(SourceEditRoutingContract.ApplyEditsDescription)]
    public static Task<SourceEditTransactionResult> ApplyEdits(
        string workspaceRoot,
        string transactionId,
        SourceEditChangeInput[] changes,
        bool validateSyntax = true,
        CancellationToken cancellationToken = default) =>
        SourceEditRuntime.Engine.ApplyEditsAsync(
            workspaceRoot,
            transactionId,
            changes,
            validateSyntax,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_source_edit_guide",
        Title = "Source Edit Routing Guide",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SourceEditRoutingGuide)),
     Description(SourceEditRoutingContract.GuideDescription)]
    public static SourceEditRoutingGuide SourceEditGuide() =>
        SourceEditRoutingContract.CreateGuide();
}
