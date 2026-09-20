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
     Description("Compatibility dotenv mutator for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Preserves unrelated lines and supports replaceAll/export semantics.")]
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
                "talvora_dotenv_set/delete",
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
     Description("Compatibility dotenv delete tool for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Preserves unrelated lines.")]
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
                "talvora_dotenv_set/delete",
                cancellationToken);
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            matches,
            changed);
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
}
