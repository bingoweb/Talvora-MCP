using System.Text;

namespace Talvora.Shared;

/// <summary>
/// Updates only Talvora's Codex MCP registration, retaining unrelated
/// user-owned TOML tables even when their headers contain inline comments.
/// </summary>
public static class CodexMcpConfiguration
{
    public static string UpsertTalvoraLocal(string original, string mcpUrl)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpUrl);
        var output = new List<string>();
        var skipping = false;
        string? multilineDelimiter = null;
        var lines = original.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        foreach (var line in lines)
        {
            // Table-like text inside a TOML multiline string is data.
            var tableParts = multilineDelimiter is null
                ? ReadTableParts(line)
                : null;
            if (tableParts is not null)
            {
                // Quoted keys containing a dot are a single TOML key, not
                // two dotted segments (e.g. "talvora_local.env").
                skipping = tableParts.Count >= 2 &&
                    string.Equals(tableParts[0], "mcp_servers",
                        StringComparison.Ordinal) &&
                    string.Equals(tableParts[1], "talvora_local",
                        StringComparison.Ordinal);
            }
            if (!skipping)
            {
                output.Add(line);
            }
            UpdateMultilineState(line, ref multilineDelimiter);
        }

        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1]))
        {
            output.RemoveAt(output.Count - 1);
        }
        if (output.Count > 0)
        {
            output.Add(string.Empty);
        }
        output.Add("[mcp_servers.talvora_local]");
        output.Add($"url = \"{mcpUrl}\"");
        output.Add(string.Empty);
        return string.Join(Environment.NewLine, output);
    }

    private static List<string>? ReadTableParts(string line)
    {
        var offset = 0;
        SkipWhitespace(line, ref offset);
        if (offset >= line.Length || line[offset] != '[')
        {
            return null;
        }

        var bracketCount = offset + 1 < line.Length &&
            line[offset + 1] == '[' ? 2 : 1;
        offset += bracketCount;
        var parts = new List<string>();
        while (offset < line.Length)
        {
            SkipWhitespace(line, ref offset);
            if (offset >= line.Length)
            {
                return null;
            }
            var key = new StringBuilder();
            var quote = line[offset] is '"' or '\'' ? line[offset++] : '\0';
            if (quote != '\0')
            {
                var closed = false;
                while (offset < line.Length)
                {
                    var current = line[offset++];
                    if (quote == '"' && current == '\\')
                    {
                        if (offset >= line.Length) { return null; }
                        key.Append(line[offset++]);
                    }
                    else if (current == quote)
                    {
                        closed = true;
                        break;
                    }
                    else
                    {
                        key.Append(current);
                    }
                }
                if (!closed) { return null; }
            }
            else
            {
                while (offset < line.Length &&
                       (char.IsAsciiLetterOrDigit(line[offset]) ||
                        line[offset] is '_' or '-'))
                {
                    key.Append(line[offset++]);
                }
                if (key.Length == 0) { return null; }
            }

            parts.Add(key.ToString());
            SkipWhitespace(line, ref offset);
            if (offset < line.Length && line[offset] == '.')
            {
                offset++;
                continue;
            }

            for (var close = 0; close < bracketCount; close++)
            {
                if (offset >= line.Length || line[offset++] != ']')
                {
                    return null;
                }
            }
            SkipWhitespace(line, ref offset);
            return offset == line.Length || line[offset] == '#'
                ? parts
                : null;
        }
        return null;
    }

    private static void SkipWhitespace(string line, ref int offset)
    {
        while (offset < line.Length && line[offset] is ' ' or '\t')
        {
            offset++;
        }
    }

    private static void UpdateMultilineState(
        string line, ref string? delimiter)
    {
        var inBasicString = false;
        var inLiteralString = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (delimiter is not null)
            {
                if (index + 3 <= line.Length &&
                    line.AsSpan(index, 3).SequenceEqual(delimiter) &&
                    (delimiter == "'''" || !IsEscaped(line, index)))
                {
                    delimiter = null;
                    index += 2;
                }
                continue;
            }
            if (inBasicString)
            {
                if (line[index] == '"' && !IsEscaped(line, index))
                {
                    inBasicString = false;
                }
                continue;
            }
            if (inLiteralString)
            {
                if (line[index] == '\'')
                {
                    inLiteralString = false;
                }
                continue;
            }
            if (line[index] == '#') { break; }
            if (index + 3 <= line.Length)
            {
                var triple = line.AsSpan(index, 3);
                if (triple.SequenceEqual("\"\"\"") ||
                    triple.SequenceEqual("'''"))
                {
                    delimiter = triple.ToString();
                    index += 2;
                    continue;
                }
            }
            if (line[index] == '"') { inBasicString = true; }
            else if (line[index] == '\'') { inLiteralString = true; }
        }
    }

    private static bool IsEscaped(string line, int position)
    {
        var count = 0;
        for (var cursor = position - 1; cursor >= 0 &&
             line[cursor] == '\\'; cursor--)
        {
            count++;
        }
        return count % 2 != 0;
    }
}
