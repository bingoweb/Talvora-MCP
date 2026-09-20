using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Talvora.SourceEditing;

internal enum UnifiedDiffLineKind
{
    Context,
    Delete,
    Add,
}

internal sealed record UnifiedDiffLine(
    UnifiedDiffLineKind Kind,
    string Text,
    bool NoNewlineAtEnd);

internal sealed record UnifiedDiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    string? Label,
    IReadOnlyList<UnifiedDiffLine> Lines);

internal sealed record UnifiedDiffFile(
    string? OldPath,
    string? NewPath,
    string? RenameFrom,
    string? RenameTo,
    bool IsNewFile,
    bool IsDeletedFile,
    IReadOnlyList<UnifiedDiffHunk> Hunks);

internal sealed record UnifiedDiffDocument(
    IReadOnlyList<UnifiedDiffFile> Files);

internal static partial class UnifiedDiffParser
{
    public static UnifiedDiffDocument Parse(string patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            throw ParseError("Unified diff input is empty.");
        }

        var normalized = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var files = new List<UnifiedDiffFile>();
        MutableFile? current = null;
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.StartsWith("@@@", StringComparison.Ordinal))
            {
                throw ParseError(
                    "Combined merge diffs (@@@) are review formats and are not accepted as mutation input.",
                    index);
            }

            if (line.StartsWith("GIT binary patch", StringComparison.Ordinal) ||
                line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                throw ParseError(
                    "Binary patches are not supported by Source Edit compatibility input.",
                    index);
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushCurrent(files, ref current);
                current = new MutableFile();
                index++;
                continue;
            }

            if (line.StartsWith("copy from ", StringComparison.Ordinal) ||
                line.StartsWith("copy to ", StringComparison.Ordinal))
            {
                throw ParseError(
                    "Git copy metadata is not supported by unified-diff compatibility v1. Keep the operation on talvora_apply_patch by using inputFormat=talvora with an explicit Add File block.",
                    index);
            }

            if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                current ??= new MutableFile();
                current.RenameFrom =
                    ParseExtendedPath(
                        line["rename from ".Length..],
                        index);
                index++;
                continue;
            }

            if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                current ??= new MutableFile();
                current.RenameTo =
                    ParseExtendedPath(
                        line["rename to ".Length..],
                        index);
                index++;
                continue;
            }

            if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                current ??= new MutableFile();
                EnsureRegularTextMode(
                    line["new file mode ".Length..],
                    index);
                current.NewFileMode = true;
                index++;
                continue;
            }

            if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                current ??= new MutableFile();
                EnsureRegularTextMode(
                    line["deleted file mode ".Length..],
                    index);
                current.DeletedFileMode = true;
                index++;
                continue;
            }

            if (line.StartsWith("old mode ", StringComparison.Ordinal) ||
                line.StartsWith("new mode ", StringComparison.Ordinal))
            {
                throw ParseError(
                    "Mode-only changes are not supported by Source Edit text transactions.",
                    index);
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                if (current is not null &&
                    current.HasFileHeaders)
                {
                    FlushCurrent(files, ref current);
                }

                current ??= new MutableFile();
                current.OldPath =
                    ParseHeaderPath(
                        line[4..],
                        oldSide: true,
                        index);
                current.HasFileHeaders = true;

                if (index + 1 >= lines.Length ||
                    !lines[index + 1].StartsWith(
                        "+++ ",
                        StringComparison.Ordinal))
                {
                    throw ParseError(
                        "Unified diff '---' header must be followed by a '+++' header.",
                        index);
                }

                index++;
                current.NewPath =
                    ParseHeaderPath(
                        lines[index][4..],
                        oldSide: false,
                        index);
                current.HasFileHeaders = true;
                index++;
                continue;
            }

            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                current ??= new MutableFile();
                var hunk =
                    ParseHunk(
                        lines,
                        ref index);
                current.Hunks.Add(hunk);
                continue;
            }

            index++;
        }

        FlushCurrent(files, ref current);

        if (files.Count == 0)
        {
            throw ParseError(
                "Unified diff contains no file changes.");
        }

        return new UnifiedDiffDocument(files);
    }

    private static UnifiedDiffHunk ParseHunk(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var header = lines[index];
        var match = HunkHeaderPattern().Match(header);
        if (!match.Success)
        {
            throw ParseError(
                $"Invalid unified diff hunk header: {header}",
                index);
        }

        var oldStart =
            ParseNumber(
                match.Groups["oldStart"].Value,
                index);
        var oldCount =
            match.Groups["oldCount"].Success
                ? ParseNumber(
                    match.Groups["oldCount"].Value,
                    index)
                : 1;
        var newStart =
            ParseNumber(
                match.Groups["newStart"].Value,
                index);
        var newCount =
            match.Groups["newCount"].Success
                ? ParseNumber(
                    match.Groups["newCount"].Value,
                    index)
                : 1;
        var label =
            match.Groups["label"].Success
                ? match.Groups["label"].Value.Trim()
                : null;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = null;
        }

        index++;
        var parsed = new List<UnifiedDiffLine>();
        var oldSeen = 0;
        var newSeen = 0;

        while (index < lines.Count &&
               (oldSeen < oldCount ||
                newSeen < newCount))
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                throw ParseError(
                    "Unified diff hunk line is missing a context/add/delete prefix.",
                    index);
            }

            UnifiedDiffLineKind kind;
            switch (line[0])
            {
                case ' ':
                    kind = UnifiedDiffLineKind.Context;
                    oldSeen++;
                    newSeen++;
                    break;
                case '-':
                    kind = UnifiedDiffLineKind.Delete;
                    oldSeen++;
                    break;
                case '+':
                    kind = UnifiedDiffLineKind.Add;
                    newSeen++;
                    break;
                default:
                    throw ParseError(
                        "Unified diff hunk lines must start with space, '-', or '+'.",
                        index);
            }

            if (oldSeen > oldCount ||
                newSeen > newCount)
            {
                throw ParseError(
                    "Unified diff hunk body exceeds the line counts declared by its header.",
                    index);
            }

            parsed.Add(
                new UnifiedDiffLine(
                    kind,
                    line[1..],
                    NoNewlineAtEnd: false));
            index++;

            if (index < lines.Count &&
                string.Equals(
                    lines[index],
                    "\\ No newline at end of file",
                    StringComparison.Ordinal))
            {
                if (parsed.Count == 0)
                {
                    throw ParseError(
                        "No-newline marker has no preceding hunk line.",
                        index);
                }

                parsed[^1] =
                    parsed[^1] with
                    {
                        NoNewlineAtEnd = true,
                    };
                index++;
            }
        }

        if (oldSeen != oldCount ||
            newSeen != newCount)
        {
            throw ParseError(
                "Unified diff hunk ended before its declared old/new line counts were satisfied.",
                Math.Max(0, index - 1));
        }

        return new UnifiedDiffHunk(
            oldStart,
            oldCount,
            newStart,
            newCount,
            label,
            parsed);
    }

    private static void FlushCurrent(
        List<UnifiedDiffFile> files,
        ref MutableFile? current)
    {
        if (current is null)
        {
            return;
        }

        var oldPath =
            current.RenameFrom ??
            current.OldPath;
        var newPath =
            current.RenameTo ??
            current.NewPath;
        var isNew =
            current.NewFileMode ||
            string.Equals(
                current.OldPath,
                "/dev/null",
                StringComparison.Ordinal);
        var isDeleted =
            current.DeletedFileMode ||
            string.Equals(
                current.NewPath,
                "/dev/null",
                StringComparison.Ordinal);

        if (isNew && isDeleted)
        {
            throw ParseError(
                "A unified diff file cannot be both new and deleted.");
        }

        if (current.RenameFrom is not null ^
            current.RenameTo is not null)
        {
            throw ParseError(
                "Git rename metadata requires both 'rename from' and 'rename to'.");
        }

        if (current.Hunks.Count == 0 &&
            current.RenameFrom is null)
        {
            if (current.HasAnyMetadata)
            {
                throw ParseError(
                    "Unified diff contains a non-text or mode-only file change that Source Edit compatibility v1 cannot apply.");
            }

            current = null;
            return;
        }

        if (isNew)
        {
            oldPath = null;
        }

        if (isDeleted)
        {
            newPath = null;
        }

        if (!isNew &&
            string.IsNullOrWhiteSpace(oldPath))
        {
            throw ParseError(
                "Unified diff existing-file change is missing an old path.");
        }

        if (!isDeleted &&
            string.IsNullOrWhiteSpace(newPath))
        {
            throw ParseError(
                "Unified diff resulting file is missing a new path.");
        }

        files.Add(
            new UnifiedDiffFile(
                oldPath,
                newPath,
                current.RenameFrom,
                current.RenameTo,
                isNew,
                isDeleted,
                current.Hunks.ToArray()));
        current = null;
    }

    private static string ParseHeaderPath(
        string value,
        bool oldSide,
        int line)
    {
        var raw =
            StripTimestamp(value);
        if (string.Equals(
                raw,
                "/dev/null",
                StringComparison.Ordinal))
        {
            return raw;
        }

        var path =
            ParseQuotedOrRawPath(
                raw,
                line);
        var prefix =
            oldSide ? "a/" : "b/";
        if (path.StartsWith(
                prefix,
                StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return RequireSafeRelativePath(
            path,
            line);
    }

    private static string ParseExtendedPath(
        string value,
        int line) =>
        RequireSafeRelativePath(
            ParseQuotedOrRawPath(
                value.Trim(),
                line),
            line);

    private static string ParseQuotedOrRawPath(
        string value,
        int line)
    {
        if (!value.StartsWith(
                '"'))
        {
            return value;
        }

        if (value.Length < 2 ||
            value[^1] != '"')
        {
            throw ParseError(
                "Quoted Git path is missing a closing quote.",
                line);
        }

        var body = value[1..^1];
        var builder = new StringBuilder();

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (++index >= body.Length)
            {
                throw ParseError(
                    "Quoted Git path ends with an incomplete escape.",
                    line);
            }

            var escaped = body[index];
            switch (escaped)
            {
                case '\\':
                case '"':
                    builder.Append(escaped);
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                default:
                    if (escaped is >= '0' and <= '7')
                    {
                        var octal =
                            new StringBuilder();
                        octal.Append(escaped);
                        while (octal.Length < 3 &&
                               index + 1 < body.Length &&
                               body[index + 1] is >= '0' and <= '7')
                        {
                            octal.Append(
                                body[++index]);
                        }

                        builder.Append(
                            (char)Convert.ToInt32(
                                octal.ToString(),
                                8));
                        break;
                    }

                    throw ParseError(
                        $"Unsupported Git path escape '\\{escaped}'.",
                        line);
            }
        }

        return builder.ToString();
    }

    private static string RequireSafeRelativePath(
        string value,
        int line)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ParseError(
                "Unified diff path is empty.",
                line);
        }

        return value.Trim();
    }

    private static string StripTimestamp(string value)
    {
        var tab =
            value.IndexOf('\t');
        return (
            tab >= 0
                ? value[..tab]
                : value)
            .Trim();
    }

    private static int ParseNumber(
        string value,
        int line)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var result) ||
            result < 0)
        {
            throw ParseError(
                $"Invalid unified diff line number/count '{value}'.",
                line);
        }

        return result;
    }

    private static void EnsureRegularTextMode(
        string mode,
        int line)
    {
        var normalized = mode.Trim();
        if (!string.Equals(
                normalized,
                "100644",
                StringComparison.Ordinal) &&
            !string.Equals(
                normalized,
                "100755",
                StringComparison.Ordinal))
        {
            throw ParseError(
                $"Unsupported Git file mode '{normalized}'. Symlink/submodule/special-file patches are not accepted by Source Edit.",
                line);
        }
    }

    private static SourceEditDomainException ParseError(
        string message,
        int? zeroBasedLine = null)
    {
        IReadOnlyDictionary<string, string>? details = null;
        if (zeroBasedLine is int line)
        {
            details =
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["line"] =
                        (line + 1).ToString(
                            CultureInfo.InvariantCulture),
                };
        }

        return new SourceEditDomainException(
            SourceEditCodes.PatchParseError,
            message,
            details: details);
    }

    private sealed class MutableFile
    {
        public string? OldPath { get; set; }
        public string? NewPath { get; set; }
        public string? RenameFrom { get; set; }
        public string? RenameTo { get; set; }
        public bool NewFileMode { get; set; }
        public bool DeletedFileMode { get; set; }
        public bool HasFileHeaders { get; set; }
        public List<UnifiedDiffHunk> Hunks { get; } = [];

        public bool HasAnyMetadata =>
            OldPath is not null ||
            NewPath is not null ||
            RenameFrom is not null ||
            RenameTo is not null ||
            NewFileMode ||
            DeletedFileMode ||
            HasFileHeaders;
    }

    [GeneratedRegex(
        "^@@ -(?<oldStart>[0-9]+)(?:,(?<oldCount>[0-9]+))? \\+(?<newStart>[0-9]+)(?:,(?<newCount>[0-9]+))? @@(?<label>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeaderPattern();
}
