using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class ConfigAssetTools
{
[McpServerTool(
        Name = "talvora_read_text_range",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextRangeResponse)),
     Description("Read a line range from any accessible text file with bounded streaming. Original line terminators in the selected slice are preserved, including the terminator after the last selected logical line when that line is not EOF. startLine is 1-based; lineCount=0 requests the finite server maximum page. Use nextStartLine/nextStartCharacter when responseLimited=true. No path allow-list is applied.")]
    public static async Task<TalvoraTextRangeResponse> ReadTextRange(
        string path,
        int startLine = 1,
        int lineCount = 200,
        int startCharacter = 0,
        CancellationToken cancellationToken = default)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }
        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }
        if (startCharacter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startCharacter));
        }

        var effectiveLineCount =
            lineCount == 0
                ? AbsoluteTextResponseLines
                : Math.Min(
                    lineCount,
                    AbsoluteTextResponseLines);
        var fullPath = Path.GetFullPath(path);
        var snapshot =
            await SourceTextStreamingReader.ReadSliceAsync(
                fullPath,
                startLine,
                startCharacter,
                effectiveLineCount,
                AbsoluteTextResponseCharacters,
                cancellationToken);
        return new TalvoraTextRangeResponse(
            fullPath,
            startLine,
            startCharacter,
            snapshot.LinesRead,
            snapshot.EndReached,
            snapshot.Text,
            snapshot.ResponseLimited,
            snapshot.ResponseLimited
                ? snapshot.NextStartLine
                : null,
            snapshot.ResponseLimited
                ? snapshot.NextStartCharacter
                : null);
    }

    [McpServerTool(
        Name = "talvora_tail_text",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextTailResponse)),
     Description("Return the last lines of any accessible text file using bounded streaming. lineCount=0 requests the finite server maximum tail window. beforeLine is an exclusive 1-based upper bound; use nextBeforeLine to page backward. Useful for logs and build output. No path allow-list is applied.")]
    public static TalvoraTextTailResponse TailText(
        string path,
        int lineCount = 200,
        long beforeLine = 0,
        CancellationToken cancellationToken = default)
    {
        if (lineCount < 0 || beforeLine < 0)
        {
            throw new ArgumentOutOfRangeException(
                "lineCount and beforeLine cannot be negative.");
        }

        var fullPath = Path.GetFullPath(path);
        var effectiveLineCount =
            lineCount == 0
                ? AbsoluteTextResponseLines
                : Math.Min(
                    lineCount,
                    AbsoluteTextResponseLines);
        return BoundedTextTailReader.Read(
            fullPath,
            effectiveLineCount,
            beforeLine,
            AbsoluteTextResponseCharacters,
            lineCount == 0 ||
            lineCount > AbsoluteTextResponseLines,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_append_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAppendTextResponse)),
     Description("Compatibility text append for ordinary/non-workspace files. New or empty files use BOM-less UTF-8; existing supported UTF-8/BOM, UTF-16 BOM, and UTF-32 BOM files preserve their text encoding without inserting another BOM. appendNewLine=true preserves the first detected CRLF/LF/CR style in an existing file and uses the platform newline when no style exists. Inside recognized development workspaces, source/text mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Ordinary non-workspace append remains supported.")]
    public static async Task<TalvoraAppendTextResponse> AppendText(
        string path,
        string content,
        bool appendNewLine = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
            fullPath,
            "talvora_append_text");
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var existingLength =
            File.Exists(fullPath)
                ? new FileInfo(fullPath).Length
                : 0;
        var existingEncoding =
            existingLength > 0
                ? SourceTextCodec.ReadEncodingDescriptor(fullPath)
                : null;
        var appendEncoding =
            existingEncoding?.Encoding
                ?? new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);
        var newline =
            appendNewLine &&
            existingEncoding is not null
                ? await DetectAppendNewlineAsync(
                        fullPath,
                        existingEncoding,
                        cancellationToken)
                    .ConfigureAwait(false)
                : Environment.NewLine;

        await using var stream = new FileStream(
            fullPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(
            stream,
            appendEncoding,
            bufferSize: 64 * 1024,
            leaveOpen: true);

        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        if (appendNewLine)
        {
            await writer.WriteAsync(newline.AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new TalvoraAppendTextResponse(
            fullPath,
            content.Length,
            stream.Length,
            appendNewLine);
    }

    private static async Task<string> DetectAppendNewlineAsync(
        string fullPath,
        SourceTextEncodingDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        const int probeCharacters = 64 * 1024;
        var buffer = new char[4096];
        var charactersRead = 0;
        var pendingCarriageReturn = false;

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            options:
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        stream.Position =
            Math.Min(
                descriptor.Preamble.Length,
                stream.Length);
        using var reader = new StreamReader(
            stream,
            descriptor.Encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024,
            leaveOpen: true);

        while (charactersRead < probeCharacters)
        {
            var requested =
                Math.Min(
                    buffer.Length,
                    probeCharacters - charactersRead);
            var read =
                await reader.ReadAsync(
                        buffer.AsMemory(0, requested),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (pendingCarriageReturn)
                {
                    return character == '\n'
                        ? "\r\n"
                        : "\r";
                }

                if (character == '\r')
                {
                    pendingCarriageReturn = true;
                }
                else if (character == '\n')
                {
                    return "\n";
                }
            }

            charactersRead += read;
        }

        return pendingCarriageReturn
            ? "\r"
            : Environment.NewLine;
    }
}
