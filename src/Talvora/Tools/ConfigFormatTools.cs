using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public sealed record TalvoraDotenvEntry(
    int LineNumber,
    string Key,
    string Value,
    bool Exported);

public sealed record TalvoraDotenvListResponse(
    string Path,
    int Count,
    IReadOnlyList<TalvoraDotenvEntry> Entries);

public sealed record TalvoraDotenvGetResponse(
    string Path,
    string Key,
    bool Found,
    string? Value,
    bool Exported);

public sealed record TalvoraConfigMutationResponse(
    string Path,
    int Matches,
    bool Changed);

public sealed record TalvoraIniEntry(
    int LineNumber,
    string Section,
    string Key,
    string Value);

public sealed record TalvoraIniListResponse(
    string Path,
    int Count,
    IReadOnlyList<TalvoraIniEntry> Entries);

public sealed record TalvoraIniGetResponse(
    string Path,
    string Section,
    string Key,
    bool Found,
    string? Value);

public sealed record TalvoraXmlNodeResult(
    string NodeType,
    string Name,
    string Value,
    string OuterXml,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record TalvoraXmlQueryResponse(
    string Path,
    string XPath,
    string ResultType,
    string? ScalarValue,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraXmlNodeResult> Nodes,
    long ResultOffset = 0,
    long? NextResultOffset = null);

public sealed record TalvoraTestFailure(
    string Name,
    string Outcome,
    double? DurationSeconds,
    string? Message,
    string? StackTrace);

public sealed record TalvoraTestReportSummary(
    string Path,
    string Format,
    int Total,
    int Passed,
    int Failed,
    int Errors,
    int Skipped,
    int Other,
    double? DurationSeconds,
    int FailureCount,
    bool FailuresTruncated,
    IReadOnlyList<TalvoraTestFailure> Failures,
    long FailureOffset = 0,
    long? NextFailureOffset = null);

[McpServerToolType]
public static partial class ConfigFormatTools
{
    internal const int AbsoluteXmlQueryResults = 10_000;
    internal const int AbsoluteTestReportFailures = 10_000;

private sealed record TextDocument(
        string Text,
        Encoding Encoding,
        string NewLine);

    private static async Task<TextDocument>
        ReadTextDocumentAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Configuration file was not found.",
                path);
        }

        return await ReadTextDocumentCoreAsync(
            path,
            cancellationToken);
    }

    private static async Task<TextDocument>
        ReadTextDocumentOrEmptyAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new TextDocument(
                string.Empty,
                new UTF8Encoding(false),
                Environment.NewLine);
        }

        return await ReadTextDocumentCoreAsync(
            path,
            cancellationToken);
    }

    private static async Task<TextDocument>
        ReadTextDocumentCoreAsync(
            string path,
            CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            useAsync: true);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);

        var text = await reader.ReadToEndAsync(
            cancellationToken);

        var newLine = text.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : text.Contains('\n')
                ? "\n"
                : Environment.NewLine;

        return new TextDocument(
            text,
            reader.CurrentEncoding,
            newLine);
    }

    private static async Task
        WriteTextDocumentAsync(
            string path,
            string text,
            Encoding encoding,
            string toolName,
            CancellationToken cancellationToken)
    {
        SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
            path,
            toolName);
        _ = await AtomicFile.WriteAllTextAsync(
            path,
            text,
            encoding,
            createBackup: false,
            cancellationToken);
    }

    private static List<string>
        SplitLinesPreservingEmpty(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var normalized = text
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = normalized
            .Split('\n')
            .ToList();

        if (lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        return lines;
    }

    private static string JoinLines(
        IReadOnlyList<string> lines,
        string newLine)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
                   newLine,
                   lines) +
               newLine;
    }
}
