using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using ModelContextProtocol.Server;

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
    IReadOnlyList<TalvoraXmlNodeResult> Nodes);

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
    IReadOnlyList<TalvoraTestFailure> Failures);

[McpServerToolType]
public static class ConfigFormatTools
{
    [McpServerTool(
        Name = "talvora_dotenv_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDotenvListResponse)),
     Description("Parse any accessible .env-style text file into key/value/exported entries. Comments and blank lines are ignored; no path/key allowlist is applied.")]
    public static async Task<TalvoraDotenvListResponse> DotenvList(
        string path,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);

        IEnumerable<TalvoraDotenvEntry> entries =
            ParseDotenv(document.Text);

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries.Where(entry =>
                entry.Key.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase));
        }

        var result = entries.ToArray();
        return new TalvoraDotenvListResponse(
            fullPath,
            result.Length,
            result);
    }

    [McpServerTool(
        Name = "talvora_dotenv_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDotenvGetResponse)),
     Description("Get one key from any accessible .env-style file. The last matching definition wins, matching common dotenv override behavior.")]
    public static async Task<TalvoraDotenvGetResponse> DotenvGet(
        string path,
        string key,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);

        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var match = ParseDotenv(document.Text)
            .LastOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    key,
                    comparison));

        return new TalvoraDotenvGetResponse(
            fullPath,
            key,
            match is not null,
            match?.Value,
            match?.Exported ?? false);
    }

    [McpServerTool(
        Name = "talvora_dotenv_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Set or append one key in any accessible .env-style file while preserving unrelated lines. replaceAll=true updates every matching definition. exported=null preserves an existing export prefix or defaults to false for new keys.")]
    public static async Task<TalvoraConfigMutationResponse> DotenvSet(
        string path,
        string key,
        string value,
        bool caseSensitive = true,
        bool replaceAll = true,
        bool? exported = null,
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException(
                "Dotenv value cannot contain raw line breaks.",
                nameof(value));
        }

        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentOrEmptyAsync(
            fullPath,
            cancellationToken);

        var lines = SplitLinesPreservingEmpty(
            document.Text);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var matches = 0;
        var changed = false;
        var firstExported = false;
        var sawFirst = false;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (!TryParseDotenvLine(
                    lines[index],
                    out var parsedKey,
                    out _,
                    out var wasExported))
            {
                continue;
            }

            if (!string.Equals(
                    parsedKey,
                    key,
                    comparison))
            {
                continue;
            }

            matches++;
            if (!sawFirst)
            {
                firstExported = wasExported;
                sawFirst = true;
            }

            if (!replaceAll && matches > 1)
            {
                continue;
            }

            var effectiveExported =
                exported ?? wasExported;
            var replacement =
                FormatDotenvLine(
                    key,
                    value,
                    effectiveExported);

            if (!string.Equals(
                    lines[index],
                    replacement,
                    StringComparison.Ordinal))
            {
                lines[index] = replacement;
                changed = true;
            }

            if (!replaceAll)
            {
                break;
            }
        }

        if (matches == 0)
        {
            lines.Add(
                FormatDotenvLine(
                    key,
                    value,
                    exported ?? false));
            changed = true;
        }
        else if (!replaceAll &&
                 matches == 1 &&
                 !changed)
        {
            _ = firstExported;
        }

        if (changed)
        {
            await WriteTextDocumentAsync(
                fullPath,
                JoinLines(
                    lines,
                    document.NewLine),
                document.Encoding,
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
    }

    [McpServerTool(
        Name = "talvora_dotenv_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Delete every matching key definition from any accessible .env-style file while preserving unrelated lines.")]
    public static async Task<TalvoraConfigMutationResponse> DotenvDelete(
        string path,
        string key,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);

        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var lines = SplitLinesPreservingEmpty(
            document.Text);
        var output = new List<string>(
            lines.Count);
        var matches = 0;

        foreach (var line in lines)
        {
            if (TryParseDotenvLine(
                    line,
                    out var parsedKey,
                    out _,
                    out _) &&
                string.Equals(
                    parsedKey,
                    key,
                    comparison))
            {
                matches++;
                continue;
            }

            output.Add(line);
        }

        var changed = matches > 0;
        if (changed)
        {
            await WriteTextDocumentAsync(
                fullPath,
                JoinLines(
                    output,
                    document.NewLine),
                document.Encoding,
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
    }

    [McpServerTool(
        Name = "talvora_ini_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraIniListResponse)),
     Description("Parse any accessible INI-style file into section/key/value entries. Supports global keys before any section and both '=' and ':' separators.")]
    public static async Task<TalvoraIniListResponse> IniList(
        string path,
        string? section = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);

        IEnumerable<TalvoraIniEntry> entries =
            ParseIni(document.Text);

        if (section is not null)
        {
            entries = entries.Where(entry =>
                string.Equals(
                    entry.Section,
                    section,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries.Where(entry =>
                entry.Key.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                entry.Value.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase));
        }

        var result = entries.ToArray();
        return new TalvoraIniListResponse(
            fullPath,
            result.Length,
            result);
    }

    [McpServerTool(
        Name = "talvora_ini_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraIniGetResponse)),
     Description("Get one INI key by section. The last matching key wins. Use an empty section string for global keys.")]
    public static async Task<TalvoraIniGetResponse> IniGet(
        string path,
        string section,
        string key,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);

        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var match = ParseIni(document.Text)
            .LastOrDefault(entry =>
                string.Equals(
                    entry.Section,
                    section ?? string.Empty,
                    comparison) &&
                string.Equals(
                    entry.Key,
                    key,
                    comparison));

        return new TalvoraIniGetResponse(
            fullPath,
            section ?? string.Empty,
            key,
            match is not null,
            match?.Value);
    }

    [McpServerTool(
        Name = "talvora_ini_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Set or append an INI key in any section of any accessible file. Missing sections are created. replaceAll=true updates every matching key definition.")]
    public static async Task<TalvoraConfigMutationResponse> IniSet(
        string path,
        string section,
        string key,
        string value,
        bool caseSensitive = false,
        bool replaceAll = true,
        string delimiter = "=",
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);
        if (delimiter is not ("=" or ":"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delimiter),
                "delimiter must be '=' or ':'.");
        }
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException(
                "INI value cannot contain raw line breaks.",
                nameof(value));
        }

        var targetSection = section ?? string.Empty;
        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentOrEmptyAsync(
            fullPath,
            cancellationToken);
        var lines = SplitLinesPreservingEmpty(
            document.Text);

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var currentSection = string.Empty;
        var matches = 0;
        var sectionFound =
            targetSection.Length == 0;
        var insertionIndex =
            targetSection.Length == 0
                ? FindGlobalInsertionIndex(lines)
                : -1;
        var changed = false;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var line = lines[index];

            if (TryParseIniSection(
                    line,
                    out var parsedSection))
            {
                if (string.Equals(
                        currentSection,
                        targetSection,
                        comparison) &&
                    targetSection.Length > 0 &&
                    insertionIndex < 0)
                {
                    insertionIndex = index;
                }

                currentSection = parsedSection;

                if (string.Equals(
                        currentSection,
                        targetSection,
                        comparison))
                {
                    sectionFound = true;
                }

                continue;
            }

            if (!string.Equals(
                    currentSection,
                    targetSection,
                    comparison) ||
                !TryParseIniKeyLine(
                    line,
                    out var parsedKey,
                    out _,
                    out _))
            {
                continue;
            }

            if (!string.Equals(
                    parsedKey,
                    key,
                    comparison))
            {
                continue;
            }

            matches++;
            var replacement =
                $"{key}{delimiter}{value}";

            if (!string.Equals(
                    line,
                    replacement,
                    StringComparison.Ordinal))
            {
                lines[index] = replacement;
                changed = true;
            }

            if (!replaceAll)
            {
                break;
            }
        }

        if (matches == 0)
        {
            if (!sectionFound &&
                targetSection.Length > 0)
            {
                if (lines.Count > 0 &&
                    lines[^1].Length != 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add(
                    $"[{targetSection}]");
                lines.Add(
                    $"{key}{delimiter}{value}");
            }
            else
            {
                if (insertionIndex < 0)
                {
                    insertionIndex = lines.Count;
                }

                lines.Insert(
                    insertionIndex,
                    $"{key}{delimiter}{value}");
            }

            changed = true;
        }

        if (changed)
        {
            await WriteTextDocumentAsync(
                fullPath,
                JoinLines(
                    lines,
                    document.NewLine),
                document.Encoding,
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
    }

    [McpServerTool(
        Name = "talvora_ini_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Delete every matching INI key in the requested section while preserving unrelated sections, keys, and comments.")]
    public static async Task<TalvoraConfigMutationResponse> IniDelete(
        string path,
        string section,
        string key,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        ValidateConfigKey(key);

        var targetSection = section ?? string.Empty;
        var fullPath = Path.GetFullPath(path);
        var document = await ReadTextDocumentAsync(
            fullPath,
            cancellationToken);
        var lines = SplitLinesPreservingEmpty(
            document.Text);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var currentSection = string.Empty;
        var output = new List<string>(
            lines.Count);
        var matches = 0;

        foreach (var line in lines)
        {
            if (TryParseIniSection(
                    line,
                    out var parsedSection))
            {
                currentSection = parsedSection;
                output.Add(line);
                continue;
            }

            if (string.Equals(
                    currentSection,
                    targetSection,
                    comparison) &&
                TryParseIniKeyLine(
                    line,
                    out var parsedKey,
                    out _,
                    out _) &&
                string.Equals(
                    parsedKey,
                    key,
                    comparison))
            {
                matches++;
                continue;
            }

            output.Add(line);
        }

        var changed = matches > 0;
        if (changed)
        {
            await WriteTextDocumentAsync(
                fullPath,
                JoinLines(
                    output,
                    document.NewLine),
                document.Encoding,
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
    }

    [McpServerTool(
        Name = "talvora_xml_query",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraXmlQueryResponse)),
     Description("Evaluate an arbitrary XPath expression against any accessible XML file. Supports namespace prefix mappings, scalar XPath results, node sets, and optional result limits.")]
    public static TalvoraXmlQueryResponse XmlQuery(
        string path,
        string xpath,
        Dictionary<string, string>? namespaces = null,
        int maxResults = 200)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            throw new ArgumentException(
                "XPath expression is required.",
                nameof(xpath));
        }
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var result = navigator.Evaluate(
            xpath,
            manager);

        if (result is XPathNodeIterator iterator)
        {
            var nodes =
                new List<TalvoraXmlNodeResult>();
            var truncated = false;

            while (iterator.MoveNext())
            {
                if (iterator.Current is null)
                {
                    continue;
                }

                if (maxResults > 0 &&
                    nodes.Count >= maxResults)
                {
                    truncated = true;
                    break;
                }

                nodes.Add(
                    ToXmlNodeResult(
                        iterator.Current));
            }

            return new TalvoraXmlQueryResponse(
                fullPath,
                xpath,
                "nodes",
                null,
                nodes.Count,
                truncated,
                nodes);
        }

        return new TalvoraXmlQueryResponse(
            fullPath,
            xpath,
            result?.GetType().Name ?? "null",
            Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture),
            0,
            false,
            []);
    }

    [McpServerTool(
        Name = "talvora_xml_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Set the value of every XML node selected by an arbitrary XPath expression. Works with elements, attributes, and text nodes. expectedMatches=-1 disables match-count assertion.")]
    public static TalvoraConfigMutationResponse XmlSet(
        string path,
        string xpath,
        string value,
        Dictionary<string, string>? namespaces = null,
        int expectedMatches = -1)
    {
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var iterator = navigator.Select(
            xpath,
            manager);
        var nodes = new List<XPathNavigator>();

        while (iterator.MoveNext())
        {
            if (iterator.Current is not null)
            {
                nodes.Add(
                    iterator.Current.Clone());
            }
        }

        if (expectedMatches >= 0 &&
            nodes.Count != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} XPath match(es), found {nodes.Count}. XML was not modified.");
        }

        var changed = false;

        foreach (var node in nodes)
        {
            if (!string.Equals(
                    node.Value,
                    value,
                    StringComparison.Ordinal))
            {
                node.SetValue(value);
                changed = true;
            }
        }

        if (changed)
        {
            SaveXmlDocument(
                document,
                fullPath);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            nodes.Count,
            changed);
    }

    [McpServerTool(
        Name = "talvora_xml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Delete every XML node selected by an arbitrary XPath expression. expectedMatches=-1 disables match-count assertion.")]
    public static TalvoraConfigMutationResponse XmlDelete(
        string path,
        string xpath,
        Dictionary<string, string>? namespaces = null,
        int expectedMatches = -1)
    {
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var iterator = navigator.Select(
            xpath,
            manager);
        var nodes = new List<XPathNavigator>();

        while (iterator.MoveNext())
        {
            if (iterator.Current is not null)
            {
                nodes.Add(
                    iterator.Current.Clone());
            }
        }

        if (expectedMatches >= 0 &&
            nodes.Count != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} XPath match(es), found {nodes.Count}. XML was not modified.");
        }

        foreach (var node in nodes
                     .OrderByDescending(
                         GetNavigatorDepth))
        {
            node.DeleteSelf();
        }

        if (nodes.Count > 0)
        {
            SaveXmlDocument(
                document,
                fullPath);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            nodes.Count,
            nodes.Count > 0);
    }

    [McpServerTool(
        Name = "talvora_test_report_summary",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTestReportSummary)),
     Description("Parse TRX, JUnit/xUnit-style XML, or NUnit3 test-result XML into a common summary with failed/error test details. format=auto detects by XML root element. maxFailures=0 means unlimited.")]
    public static TalvoraTestReportSummary TestReportSummary(
        string path,
        string format = "auto",
        int maxFailures = 200)
    {
        if (maxFailures < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFailures));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var detected = DetectTestReportFormat(
            document,
            format);

        return detected switch
        {
            "trx" => ParseTrx(
                fullPath,
                document,
                maxFailures),
            "junit" => ParseJunit(
                fullPath,
                document,
                maxFailures),
            "nunit3" => ParseNunit3(
                fullPath,
                document,
                maxFailures),
            _ => throw new InvalidOperationException(
                $"Unsupported test report format: {detected}"),
        };
    }

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
            CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temp =
            path +
            ".talvora-" +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                temp,
                text,
                encoding,
                cancellationToken);

            File.Move(
                temp,
                path,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
            }
        }
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

    private static IEnumerable<TalvoraDotenvEntry>
        ParseDotenv(string text)
    {
        var lines =
            SplitLinesPreservingEmpty(text);

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (!TryParseDotenvLine(
                    lines[index],
                    out var key,
                    out var value,
                    out var exported))
            {
                continue;
            }

            yield return new TalvoraDotenvEntry(
                index + 1,
                key,
                value,
                exported);
        }
    }

    private static bool TryParseDotenvLine(
        string line,
        out string key,
        out string value,
        out bool exported)
    {
        key = string.Empty;
        value = string.Empty;
        exported = false;

        var trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith('#'))
        {
            return false;
        }

        if (trimmed.StartsWith(
                "export ",
                StringComparison.Ordinal))
        {
            exported = true;
            trimmed = trimmed[7..].TrimStart();
        }

        var separator =
            trimmed.IndexOf('=');

        if (separator <= 0)
        {
            return false;
        }

        key = trimmed[..separator].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        value = DecodeDotenvValue(
            trimmed[(separator + 1)..].Trim());

        return true;
    }

    private static string DecodeDotenvValue(
        string value)
    {
        if (value.Length >= 2 &&
            value[0] == '"' &&
            value[^1] == '"')
        {
            var inner =
                value[1..^1];

            return inner
                .Replace(
                    "\\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    "\\r",
                    "\r",
                    StringComparison.Ordinal)
                .Replace(
                    "\\t",
                    "\t",
                    StringComparison.Ordinal)
                .Replace(
                    "\\\"",
                    "\"",
                    StringComparison.Ordinal)
                .Replace(
                    "\\\\",
                    "\\",
                    StringComparison.Ordinal);
        }

        if (value.Length >= 2 &&
            value[0] == '\'' &&
            value[^1] == '\'')
        {
            return value[1..^1];
        }

        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0
            ? value[..hash].TrimEnd()
            : value;
    }

    private static string FormatDotenvLine(
        string key,
        string value,
        bool exported)
    {
        var encoded =
            EncodeDotenvValue(value);

        return
            (exported ? "export " : string.Empty) +
            key +
            "=" +
            encoded;
    }

    private static string EncodeDotenvValue(
        string value)
    {
        var mustQuote =
            value.Length == 0 ||
            char.IsWhiteSpace(value[0]) ||
            char.IsWhiteSpace(value[^1]) ||
            value.Contains('#') ||
            value.Contains('"') ||
            value.Contains('\\');

        if (!mustQuote)
        {
            return value;
        }

        return "\"" +
               value
                   .Replace(
                       "\\",
                       "\\\\",
                       StringComparison.Ordinal)
                   .Replace(
                       "\"",
                       "\\\"",
                       StringComparison.Ordinal) +
               "\"";
    }

    private static IEnumerable<TalvoraIniEntry>
        ParseIni(string text)
    {
        var lines =
            SplitLinesPreservingEmpty(text);
        var section = string.Empty;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var line = lines[index];

            if (TryParseIniSection(
                    line,
                    out var parsedSection))
            {
                section = parsedSection;
                continue;
            }

            if (!TryParseIniKeyLine(
                    line,
                    out var key,
                    out var value,
                    out _))
            {
                continue;
            }

            yield return new TalvoraIniEntry(
                index + 1,
                section,
                key,
                value);
        }
    }

    private static bool TryParseIniSection(
        string line,
        out string section)
    {
        section = string.Empty;
        var trimmed = line.Trim();

        if (trimmed.Length < 2 ||
            trimmed[0] != '[' ||
            trimmed[^1] != ']')
        {
            return false;
        }

        section =
            trimmed[1..^1].Trim();

        return true;
    }

    private static bool TryParseIniKeyLine(
        string line,
        out string key,
        out string value,
        out char delimiter)
    {
        key = string.Empty;
        value = string.Empty;
        delimiter = '=';

        var trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith(';') ||
            trimmed.StartsWith('#'))
        {
            return false;
        }

        var equals =
            trimmed.IndexOf('=');
        var colon =
            trimmed.IndexOf(':');

        var separator =
            equals < 0
                ? colon
                : colon < 0
                    ? equals
                    : Math.Min(
                        equals,
                        colon);

        if (separator <= 0)
        {
            return false;
        }

        key =
            trimmed[..separator].Trim();
        value =
            trimmed[(separator + 1)..].Trim();
        delimiter =
            trimmed[separator];

        return key.Length > 0;
    }

    private static int FindGlobalInsertionIndex(
        IReadOnlyList<string> lines)
    {
        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (TryParseIniSection(
                    lines[index],
                    out _))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static void ValidateConfigKey(
        string key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            key.Any(char.IsWhiteSpace) ||
            key.Contains('=') ||
            key.Contains('\r') ||
            key.Contains('\n'))
        {
            throw new ArgumentException(
                "Configuration key must be one non-empty token without whitespace or '='.",
                nameof(key));
        }
    }

    private static XmlDocument LoadXmlDocument(
        string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
        };

        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null,
        };

        using var reader =
            XmlReader.Create(
                path,
                settings);

        document.Load(reader);
        return document;
    }

    private static void SaveXmlDocument(
        XmlDocument document,
        string path)
    {
        using var writer = XmlWriter.Create(
            path,
            new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None,
            });

        document.Save(writer);
    }

    private static XmlNamespaceManager
        CreateNamespaceManager(
            XmlNameTable nameTable,
            Dictionary<string, string>? namespaces)
    {
        var manager =
            new XmlNamespaceManager(nameTable);

        foreach (var pair in namespaces
                     ?? new Dictionary<string, string>())
        {
            manager.AddNamespace(
                pair.Key,
                pair.Value);
        }

        return manager;
    }

    private static TalvoraXmlNodeResult
        ToXmlNodeResult(
            XPathNavigator navigator)
    {
        var attributes =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        if (navigator.NodeType ==
                XPathNodeType.Element &&
            navigator.HasAttributes)
        {
            var clone =
                navigator.Clone();

            if (clone.MoveToFirstAttribute())
            {
                do
                {
                    attributes[clone.Name] =
                        clone.Value;
                }
                while (clone.MoveToNextAttribute());
            }
        }

        return new TalvoraXmlNodeResult(
            navigator.NodeType.ToString(),
            navigator.Name,
            navigator.Value,
            navigator.OuterXml,
            attributes);
    }

    private static int GetNavigatorDepth(
        XPathNavigator navigator)
    {
        var clone =
            navigator.Clone();
        var depth = 0;

        while (clone.MoveToParent())
        {
            depth++;
        }

        return depth;
    }

    private static string DetectTestReportFormat(
        XmlDocument document,
        string requested)
    {
        var normalized =
            requested.Trim().ToLowerInvariant();

        if (normalized != "auto")
        {
            return normalized switch
            {
                "trx" => "trx",
                "junit" => "junit",
                "xunit" => "junit",
                "nunit" => "nunit3",
                "nunit3" => "nunit3",
                _ => normalized,
            };
        }

        var root =
            document.DocumentElement
            ?? throw new InvalidDataException(
                "Test report XML has no root element.");

        return root.LocalName switch
        {
            "TestRun" => "trx",
            "testsuite" => "junit",
            "testsuites" => "junit",
            "test-run" => "nunit3",
            _ => throw new InvalidDataException(
                $"Unable to detect test report format from root element '{root.LocalName}'."),
        };
    }

    private static TalvoraTestReportSummary
        ParseTrx(
            string path,
            XmlDocument document,
            int maxFailures)
    {
        var counters =
            document.SelectSingleNode(
                "//*[local-name()='Counters']");

        var total =
            ParseIntAttribute(counters, "total");
        var passed =
            ParseIntAttribute(counters, "passed");
        var failed =
            ParseIntAttribute(counters, "failed");
        var errors =
            ParseIntAttribute(counters, "error") +
            ParseIntAttribute(counters, "timeout") +
            ParseIntAttribute(counters, "aborted");
        var skipped =
            ParseIntAttribute(counters, "notExecuted") +
            ParseIntAttribute(counters, "inconclusive");

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;

        var nodes =
            document.SelectNodes(
                "//*[local-name()='UnitTestResult']");

        if (nodes is not null)
        {
            foreach (XmlNode node in nodes)
            {
                var outcome =
                    node.Attributes?["outcome"]?.Value
                    ?? string.Empty;

                if (string.Equals(
                        outcome,
                        "Passed",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        outcome,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (maxFailures > 0 &&
                    failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var errorInfo =
                    node.SelectSingleNode(
                        ".//*[local-name()='ErrorInfo']");

                failures.Add(
                    new TalvoraTestFailure(
                        node.Attributes?["testName"]?.Value
                        ?? string.Empty,
                        outcome,
                        ParseDuration(
                            node.Attributes?["duration"]?.Value),
                        errorInfo?.SelectSingleNode(
                            "./*[local-name()='Message']")?.InnerText,
                        errorInfo?.SelectSingleNode(
                            "./*[local-name()='StackTrace']")?.InnerText));
            }
        }

        var other =
            Math.Max(
                0,
                total -
                passed -
                failed -
                errors -
                skipped);

        return new TalvoraTestReportSummary(
            path,
            "trx",
            total,
            passed,
            failed,
            errors,
            skipped,
            other,
            null,
            failures.Count,
            truncated,
            failures);
    }

    private static TalvoraTestReportSummary
        ParseJunit(
            string path,
            XmlDocument document,
            int maxFailures)
    {
        var suites =
            document.SelectNodes(
                "//*[local-name()='testsuite']");

        var total = 0;
        var failed = 0;
        var errors = 0;
        var skipped = 0;
        double? duration = 0;

        if (suites is not null)
        {
            foreach (XmlNode suite in suites)
            {
                total +=
                    ParseIntAttribute(
                        suite,
                        "tests");
                failed +=
                    ParseIntAttribute(
                        suite,
                        "failures");
                errors +=
                    ParseIntAttribute(
                        suite,
                        "errors");
                skipped +=
                    ParseIntAttribute(
                        suite,
                        "skipped") +
                    ParseIntAttribute(
                        suite,
                        "disabled");

                var suiteDuration =
                    ParseDoubleAttribute(
                        suite,
                        "time");

                if (suiteDuration is null)
                {
                    duration = null;
                }
                else if (duration is not null)
                {
                    duration += suiteDuration;
                }
            }
        }

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;

        var cases =
            document.SelectNodes(
                "//*[local-name()='testcase']");

        if (cases is not null)
        {
            foreach (XmlNode testCase in cases)
            {
                var failure =
                    testCase.SelectSingleNode(
                        "./*[local-name()='failure']");
                var error =
                    testCase.SelectSingleNode(
                        "./*[local-name()='error']");

                if (failure is null &&
                    error is null)
                {
                    continue;
                }

                if (maxFailures > 0 &&
                    failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var detail =
                    failure ?? error!;
                var className =
                    testCase.Attributes?["classname"]?.Value;
                var name =
                    testCase.Attributes?["name"]?.Value
                    ?? string.Empty;

                failures.Add(
                    new TalvoraTestFailure(
                        string.IsNullOrWhiteSpace(className)
                            ? name
                            : className + "." + name,
                        failure is not null
                            ? "Failed"
                            : "Error",
                        ParseDoubleAttribute(
                            testCase,
                            "time"),
                        detail.Attributes?["message"]?.Value
                        ?? detail.InnerText,
                        detail.InnerText));
            }
        }

        var passed =
            Math.Max(
                0,
                total -
                failed -
                errors -
                skipped);

        return new TalvoraTestReportSummary(
            path,
            "junit",
            total,
            passed,
            failed,
            errors,
            skipped,
            0,
            duration,
            failures.Count,
            truncated,
            failures);
    }

    private static TalvoraTestReportSummary
        ParseNunit3(
            string path,
            XmlDocument document,
            int maxFailures)
    {
        var root =
            document.DocumentElement
            ?? throw new InvalidDataException(
                "NUnit report XML has no root element.");

        var total =
            ParseIntAttribute(root, "total");
        var passed =
            ParseIntAttribute(root, "passed");
        var failed =
            ParseIntAttribute(root, "failed");
        var skipped =
            ParseIntAttribute(root, "skipped");
        var inconclusive =
            ParseIntAttribute(
                root,
                "inconclusive");
        var duration =
            ParseDoubleAttribute(
                root,
                "duration");

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;

        var cases =
            document.SelectNodes(
                "//*[local-name()='test-case' and @result='Failed']");

        if (cases is not null)
        {
            foreach (XmlNode testCase in cases)
            {
                if (maxFailures > 0 &&
                    failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var failure =
                    testCase.SelectSingleNode(
                        "./*[local-name()='failure']");

                failures.Add(
                    new TalvoraTestFailure(
                        testCase.Attributes?["fullname"]?.Value
                        ?? testCase.Attributes?["name"]?.Value
                        ?? string.Empty,
                        "Failed",
                        ParseDoubleAttribute(
                            testCase,
                            "duration"),
                        failure?.SelectSingleNode(
                            "./*[local-name()='message']")?.InnerText,
                        failure?.SelectSingleNode(
                            "./*[local-name()='stack-trace']")?.InnerText));
            }
        }

        return new TalvoraTestReportSummary(
            path,
            "nunit3",
            total,
            passed,
            failed,
            0,
            skipped,
            inconclusive,
            duration,
            failures.Count,
            truncated,
            failures);
    }

    private static int ParseIntAttribute(
        XmlNode? node,
        string name)
    {
        return int.TryParse(
            node?.Attributes?[name]?.Value,
            out var value)
            ? value
            : 0;
    }

    private static double? ParseDoubleAttribute(
        XmlNode? node,
        string name)
    {
        return double.TryParse(
            node?.Attributes?[name]?.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static double? ParseDuration(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeSpan.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                out var timeSpan))
        {
            return timeSpan.TotalSeconds;
        }

        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds)
            ? seconds
            : null;
    }
}
