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
     Description("Read a line range from any accessible text file without returning the entire file. startLine is 1-based; lineCount=0 reads from startLine to EOF. No path allow-list is applied.")]
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
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(
            stream,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var builder = new StringBuilder();
        var currentLine = 0;
        var linesRead = 0;
        var endReached = false;
        var responseLimited = false;
        int? nextStartLine = null;
        int? nextStartCharacter = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line =
                await reader.ReadLineAsync(
                    cancellationToken);
            if (line is null)
            {
                endReached = true;
                break;
            }

            currentLine++;
            if (currentLine < startLine)
            {
                continue;
            }

            if (linesRead >= effectiveLineCount)
            {
                responseLimited = true;
                nextStartLine = currentLine;
                nextStartCharacter = 0;
                break;
            }

            var sourceCharacter =
                currentLine == startLine
                    ? startCharacter
                    : 0;
            if (sourceCharacter > line.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startCharacter),
                    "startCharacter is beyond the requested source line.");
            }

            var separatorLength =
                linesRead > 0
                    ? Environment.NewLine.Length
                    : 0;
            var remainingCharacters =
                AbsoluteTextResponseCharacters -
                builder.Length;
            if (remainingCharacters <= separatorLength)
            {
                responseLimited = true;
                nextStartLine = currentLine;
                nextStartCharacter = sourceCharacter;
                break;
            }

            if (separatorLength > 0)
            {
                builder.AppendLine();
                remainingCharacters -= separatorLength;
            }

            var sourceRemaining =
                line.Length -
                sourceCharacter;
            var charactersToAppend =
                Math.Min(
                    sourceRemaining,
                    remainingCharacters);
            if (charactersToAppend > 0)
            {
                builder.Append(
                    line,
                    sourceCharacter,
                    charactersToAppend);
            }
            linesRead++;

            if (charactersToAppend < sourceRemaining)
            {
                responseLimited = true;
                nextStartLine = currentLine;
                nextStartCharacter =
                    sourceCharacter +
                    charactersToAppend;
                break;
            }

            if (linesRead >= effectiveLineCount)
            {
                var nextLine =
                    await reader.ReadLineAsync(
                        cancellationToken);
                endReached =
                    nextLine is null;
                responseLimited =
                    !endReached;
                if (responseLimited)
                {
                    nextStartLine =
                        currentLine + 1;
                    nextStartCharacter = 0;
                }
                break;
            }
        }

        return new TalvoraTextRangeResponse(
            fullPath,
            startLine,
            startCharacter,
            linesRead,
            endReached,
            builder.ToString(),
            responseLimited,
            nextStartLine,
            nextStartCharacter);
    }

    [McpServerTool(
        Name = "talvora_tail_text",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextTailResponse)),
     Description("Return the last lines of any accessible text file. lineCount=0 returns the complete file. Useful for logs and build output. No path allow-list is applied.")]
    public static TalvoraTextTailResponse TailText(
        string path,
        int lineCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        var fullPath = Path.GetFullPath(path);
        var effectiveLineCount =
            lineCount == 0
                ? AbsoluteTextResponseLines
                : Math.Min(
                    lineCount,
                    AbsoluteTextResponseLines);
        var queue =
            new Queue<string>(
                effectiveLineCount);
        var totalLines = 0;
        long queuedLineCharacters = 0;
        var responseLimited = false;

        foreach (var line in File.ReadLines(fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;

            var retainedLine =
                line.Length >
                    AbsoluteTextResponseCharacters
                    ? line[^AbsoluteTextResponseCharacters..]
                    : line;
            if (retainedLine.Length != line.Length)
            {
                responseLimited = true;
            }

            queue.Enqueue(retainedLine);
            queuedLineCharacters +=
                retainedLine.Length;

            while (queue.Count >
                       effectiveLineCount ||
                   queuedLineCharacters +
                       Math.Max(
                           0,
                           queue.Count - 1) *
                       Environment.NewLine.Length >
                       AbsoluteTextResponseCharacters)
            {
                var removed =
                    queue.Dequeue();
                queuedLineCharacters -=
                    removed.Length;
                responseLimited = true;
            }
        }

        var startLine = queue.Count == 0
            ? 0
            : totalLines - queue.Count + 1;
        if ((lineCount == 0 &&
             totalLines >
                 AbsoluteTextResponseLines) ||
            (lineCount >
                 AbsoluteTextResponseLines &&
             totalLines >
                 AbsoluteTextResponseLines))
        {
            responseLimited = true;
        }

        return new TalvoraTextTailResponse(
            fullPath,
            totalLines,
            startLine,
            queue.Count,
            string.Join(
                Environment.NewLine,
                queue),
            responseLimited);
    }

    [McpServerTool(
        Name = "talvora_append_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAppendTextResponse)),
     Description("Compatibility UTF-8 append for ordinary/non-workspace files. Inside recognized development workspaces, source/text mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Ordinary non-workspace append remains supported.")]
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

        await using var stream = new FileStream(
            fullPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: true);

        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        if (appendNewLine)
        {
            await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new TalvoraAppendTextResponse(
            fullPath,
            content.Length,
            stream.Length,
            appendNewLine);
    }
}
