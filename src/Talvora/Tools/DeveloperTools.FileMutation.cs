using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_replace_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraReplaceTextResponse)),
     Description("Compatibility literal/regex text replacement for ordinary/non-workspace files. Existing UTF-8/UTF-8 BOM/UTF-16 BOM/UTF-32 BOM encoding and BOM semantics are preserved. Inside recognized development workspaces, source/text mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Non-workspace compatibility is preserved.")]
    public static async Task<TalvoraReplaceTextResponse> ReplaceText(
        string path,
        string search,
        string replacement,
        bool regex = false,
        bool caseSensitive = true,
        bool replaceAll = true,
        int expectedMatches = -1,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(search))
        {
            throw new ArgumentException("Search value cannot be empty.", nameof(search));
        }
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
            fullPath,
            "talvora_replace_text");
        var snapshot =
            await SourceTextCodec.ReadSnapshotAsync(
                fullPath,
                Path.GetFileName(fullPath),
                requireText: true,
                cancellationToken);
        if (!snapshot.Exists ||
            snapshot.Document is null)
        {
            throw new FileNotFoundException(
                "Text replacement target was not found.",
                fullPath);
        }

        var document = snapshot.Document;
        var original = document.Text;
        string updated;
        int matches;

        if (regex)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            var expression = new Regex(search, options, TimeSpan.FromSeconds(2));
            matches = expression.Count(original);
            updated = replaceAll
                ? expression.Replace(original, replacement)
                : expression.Replace(original, replacement, 1);
        }
        else
        {
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            matches = CountOccurrences(original, search, comparison);

            if (replaceAll)
            {
                updated = original.Replace(search, replacement, comparison);
            }
            else
            {
                var index = original.IndexOf(search, comparison);
                updated = index < 0
                    ? original
                    : string.Concat(
                        original.AsSpan(0, index),
                        replacement,
                        original.AsSpan(index + search.Length));
            }
        }

        if (expectedMatches >= 0 && matches != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} match(es), found {matches}. File was not modified.");
        }

        var replacements = replaceAll ? matches : Math.Min(matches, 1);
        var changed = !string.Equals(original, updated, StringComparison.Ordinal);
        string? backupPath = null;

        if (changed)
        {
            backupPath = await AtomicFile.WriteAllTextAsync(
                fullPath,
                updated,
                CreateReplacementWriterEncoding(
                    document.Encoding),
                createBackup,
                cancellationToken);
        }

        return new TalvoraReplaceTextResponse(
            fullPath,
            matches,
            replacements,
            changed,
            backupPath);
    }

    private static Encoding CreateReplacementWriterEncoding(
        SourceTextEncodingDescriptor descriptor) =>
        SourceTextCodec.CreateWriterEncoding(
            descriptor);
}
