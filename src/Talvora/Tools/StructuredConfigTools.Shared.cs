using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Talvora.Tools;

public static partial class StructuredConfigTools
{
private static TalvoraStructuredConfigGetResponse BuildGetResponse(
        string path,
        string format,
        string pointer,
        JsonNode? root,
        bool indented)
    {
        var tokens = ConfigAssetTools.ParsePointer(pointer);

        if (!ConfigAssetTools.TryResolve(
                root,
                tokens,
                out var node))
        {
            return new TalvoraStructuredConfigGetResponse(
                path,
                format,
                pointer,
                false,
                "Missing",
                null);
        }

        return new TalvoraStructuredConfigGetResponse(
            path,
            format,
            pointer,
            true,
            ConfigAssetTools.GetJsonKind(node),
            ConfigAssetTools.ToJson(node, indented));
    }

    private static async Task<TalvoraStructuredConfigMutationResponse>
        WriteMutationAsync(
            string path,
            string format,
            string pointer,
            string originalText,
            string updatedText,
            bool createBackup,
            string toolName,
            CancellationToken cancellationToken)
    {
        var changed =
            !string.Equals(
                TextLines.NormalizeTrailingNewline(originalText),
                TextLines.NormalizeTrailingNewline(updatedText),
                StringComparison.Ordinal);

        string? backupPath = null;

        if (changed)
        {
            SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
                path,
                toolName);
            backupPath = await AtomicFile.WriteAllTextAsync(
                path,
                updatedText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                createBackup,
                cancellationToken);
        }

        return new TalvoraStructuredConfigMutationResponse(
            path,
            format,
            pointer,
            changed,
            backupPath);
    }
}
