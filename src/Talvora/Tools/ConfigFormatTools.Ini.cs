using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class ConfigFormatTools
{
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
     Description("Compatibility INI mutator for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Missing sections may be created and replaceAll is supported.")]
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
                "talvora_ini_set/delete",
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
     Description("Compatibility INI delete tool for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Preserves unrelated sections, keys, and comments.")]
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
                "talvora_ini_set/delete",
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
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
}
