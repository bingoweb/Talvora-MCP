using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;

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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
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

            if (lineCount > 0 && linesRead >= lineCount)
            {
                endReached = false;
                break;
            }

            if (linesRead > 0)
            {
                builder.AppendLine();
            }
            builder.Append(line);
            linesRead++;

            if (lineCount > 0 && linesRead >= lineCount)
            {
                var nextLine = await reader.ReadLineAsync(cancellationToken);
                endReached = nextLine is null;
                break;
            }
        }

        return new TalvoraTextRangeResponse(
            fullPath,
            startLine,
            linesRead,
            endReached,
            builder.ToString());
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

        if (lineCount == 0)
        {
            var all = new List<string>();
            foreach (var line in File.ReadLines(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                all.Add(line);
            }

            return new TalvoraTextTailResponse(
                fullPath,
                all.Count,
                all.Count == 0 ? 0 : 1,
                all.Count,
                string.Join(Environment.NewLine, all));
        }

        var queue = new Queue<string>(lineCount);
        var totalLines = 0;

        foreach (var line in File.ReadLines(fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;

            if (queue.Count == lineCount)
            {
                queue.Dequeue();
            }
            queue.Enqueue(line);
        }

        var startLine = queue.Count == 0
            ? 0
            : totalLines - queue.Count + 1;

        return new TalvoraTextTailResponse(
            fullPath,
            totalLines,
            startLine,
            queue.Count,
            string.Join(Environment.NewLine, queue));
    }

    [McpServerTool(
        Name = "talvora_append_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAppendTextResponse)),
     Description("Append UTF-8 text to any accessible file, creating parent directories and the file when needed. appendNewLine=true appends the platform newline after the supplied content. No path allow-list is applied.")]
    public static async Task<TalvoraAppendTextResponse> AppendText(
        string path,
        string content,
        bool appendNewLine = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
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
