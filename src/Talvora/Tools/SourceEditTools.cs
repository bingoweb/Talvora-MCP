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
        [Description("Absolute repository root, for example C:\\Users\\tayla\\Talvora-MCP. Paths inside the patch are relative to it.")]
        string workspaceRoot,
        [Description("A new unique id for every attempt, for example 'fix-login-1'. Reusing an id with a different patch is rejected; retry with a new id.")]
        string transactionId,
        [Description("Talvora patch (inputFormat 'talvora'):\n*** Begin Talvora Patch\n*** Update File: src/App.cs\n*** Revision: sha256:<64 hex from talvora_read_source>\n@@\n unchanged line (leading space)\n-removed line\n+added line\n*** Add File: docs/New.md\n+every line starts with +\n*** Delete File: old.txt\n*** Revision: sha256:<...>\n*** End Talvora Patch\nContext and '-' lines must equal the file exactly (whitespace included) and match one place; add context if ambiguous. '*** Begin Patch'/'*** End Patch' and '*** End of File' are accepted.")]
        string patch,
        [Description("'talvora' (default, recommended: no line numbers) or 'unified-diff' (git diff with exact @@ line numbers; then also pass expectedRevisions).")]
        string inputFormat = "talvora",
        [Description("unified-diff only: {\"<relative path>\": \"sha256:<revision from talvora_read_source>\"} for every existing file the diff changes.")]
        Dictionary<string, string>? expectedRevisions = null,
        bool validateSyntax = true,
        [Description("Optional, for the user's desktop worklog: one Turkish line saying what this change does and why (example: \"git pull artık oturum kullanıcısının SSH anahtarını kullanıyor\"). Not used by the edit itself.")]
        string? purpose = null,
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
