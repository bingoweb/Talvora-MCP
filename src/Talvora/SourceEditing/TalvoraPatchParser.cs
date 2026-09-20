namespace Talvora.SourceEditing;

internal enum SourcePatchBlockKind
{
    Update,
    Add,
    Delete,
}

internal enum SourcePatchLineKind
{
    Context,
    Delete,
    Add,
}

internal sealed record SourcePatchHunkLine(
    SourcePatchLineKind Kind,
    string Text);

internal sealed record SourcePatchHunk(
    string? Label,
    IReadOnlyList<SourcePatchHunkLine> Lines);

internal sealed record SourcePatchFileBlock(
    SourcePatchBlockKind Kind,
    string Path,
    string? ExpectedRevision,
    string? MoveTo,
    string? Encoding,
    string? Newline,
    bool FinalNewline,
    IReadOnlyList<SourcePatchHunk> Hunks,
    IReadOnlyList<string> AddedLines);

internal sealed record SourcePatchDocument(
    IReadOnlyList<SourcePatchFileBlock> Files);

internal static class TalvoraPatchParser
{
    private const string BeginMarker = "*** Begin Talvora Patch";
    private const string EndMarker = "*** End Talvora Patch";
    private const string UpdatePrefix = "*** Update File: ";
    private const string AddPrefix = "*** Add File: ";
    private const string DeletePrefix = "*** Delete File: ";
    private const string RevisionPrefix = "*** Revision: ";
    private const string MovePrefix = "*** Move to: ";
    private const string EncodingPrefix = "*** Encoding: ";
    private const string NewlinePrefix = "*** Newline: ";
    private const string FinalNewlinePrefix = "*** Final Newline: ";

    public static SourcePatchDocument Parse(string patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            throw ParseError("Patch input is empty.");
        }

        var normalized = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n').ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count < 2 ||
            !string.Equals(lines[0], BeginMarker, StringComparison.Ordinal) ||
            !string.Equals(lines[^1], EndMarker, StringComparison.Ordinal))
        {
            throw ParseError(
                $"Patch must start with '{BeginMarker}' and end with '{EndMarker}'.");
        }

        var blocks = new List<SourcePatchFileBlock>();
        var index = 1;

        while (index < lines.Count - 1)
        {
            if (lines[index].Length == 0)
            {
                index++;
                continue;
            }

            if (lines[index].StartsWith(UpdatePrefix, StringComparison.Ordinal))
            {
                blocks.Add(ParseUpdate(lines, ref index));
                continue;
            }

            if (lines[index].StartsWith(AddPrefix, StringComparison.Ordinal))
            {
                blocks.Add(ParseAdd(lines, ref index));
                continue;
            }

            if (lines[index].StartsWith(DeletePrefix, StringComparison.Ordinal))
            {
                blocks.Add(ParseDelete(lines, ref index));
                continue;
            }

            throw ParseError(
                $"Unexpected patch directive at line {index + 1}: {lines[index]}");
        }

        if (blocks.Count == 0)
        {
            throw ParseError("Patch contains no file operations.");
        }

        return new SourcePatchDocument(blocks);
    }

    private static SourcePatchFileBlock ParseUpdate(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var headerLine = index;
        var path = ParsePath(lines[index], UpdatePrefix, headerLine);
        index++;

        string? revision = null;
        string? moveTo = null;
        var hunks = new List<SourcePatchHunk>();

        while (index < lines.Count - 1)
        {
            var line = lines[index];

            if (line.StartsWith(RevisionPrefix, StringComparison.Ordinal))
            {
                if (hunks.Count > 0)
                {
                    throw ParseError(
                        "Revision metadata must appear before update hunks.",
                        index);
                }

                if (revision is not null)
                {
                    throw ParseError("Duplicate Revision directive.", index);
                }

                revision = ParseRevision(line[RevisionPrefix.Length..], index);
                index++;
                continue;
            }

            if (line.StartsWith(MovePrefix, StringComparison.Ordinal))
            {
                if (hunks.Count > 0)
                {
                    throw ParseError(
                        "Move metadata must appear before update hunks.",
                        index);
                }

                if (moveTo is not null)
                {
                    throw ParseError("Duplicate Move to directive.", index);
                }

                moveTo = RequireNonEmpty(line[MovePrefix.Length..], "Move destination", index);
                index++;
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                hunks.Add(ParseHunk(lines, ref index));
                continue;
            }

            if (IsBlockStart(line) || line == EndMarker)
            {
                break;
            }

            if (line.Length == 0)
            {
                index++;
                continue;
            }

            throw ParseError(
                $"Unexpected update directive/content: {line}",
                index);
        }

        if (revision is null)
        {
            throw ParseError(
                $"Update block for '{path}' requires a Revision directive.",
                headerLine);
        }

        if (hunks.Count == 0 && moveTo is null)
        {
            throw ParseError(
                $"Update block for '{path}' must contain at least one hunk or a Move to directive.",
                headerLine);
        }

        return new SourcePatchFileBlock(
            SourcePatchBlockKind.Update,
            path,
            revision,
            moveTo,
            null,
            null,
            FinalNewline: true,
            hunks,
            []);
    }

    private static SourcePatchFileBlock ParseAdd(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var headerLine = index;
        var path = ParsePath(lines[index], AddPrefix, headerLine);
        index++;

        string? encoding = null;
        string? newline = null;
        var finalNewline = true;
        var contentStarted = false;
        var added = new List<string>();

        while (index < lines.Count - 1)
        {
            var line = lines[index];

            if (!contentStarted &&
                line.StartsWith(EncodingPrefix, StringComparison.Ordinal))
            {
                if (encoding is not null)
                {
                    throw ParseError("Duplicate Encoding directive.", index);
                }

                encoding = RequireNonEmpty(
                    line[EncodingPrefix.Length..],
                    "Encoding",
                    index);
                index++;
                continue;
            }

            if (!contentStarted &&
                line.StartsWith(NewlinePrefix, StringComparison.Ordinal))
            {
                if (newline is not null)
                {
                    throw ParseError("Duplicate Newline directive.", index);
                }

                newline = RequireNonEmpty(
                    line[NewlinePrefix.Length..],
                    "Newline policy",
                    index);
                index++;
                continue;
            }

            if (!contentStarted &&
                line.StartsWith(FinalNewlinePrefix, StringComparison.Ordinal))
            {
                if (!bool.TryParse(
                        line[FinalNewlinePrefix.Length..].Trim(),
                        out finalNewline))
                {
                    throw ParseError(
                        "Final Newline must be true or false.",
                        index);
                }

                index++;
                continue;
            }

            if (IsBlockStart(line) || line == EndMarker)
            {
                break;
            }

            if (line.StartsWith('+'))
            {
                contentStarted = true;
                added.Add(line[1..]);
                index++;
                continue;
            }

            if (line.Length == 0 && !contentStarted)
            {
                index++;
                continue;
            }

            throw ParseError(
                "Add-file content lines must start with '+'.",
                index);
        }

        return new SourcePatchFileBlock(
            SourcePatchBlockKind.Add,
            path,
            null,
            null,
            encoding,
            newline,
            finalNewline,
            [],
            added);
    }

    private static SourcePatchFileBlock ParseDelete(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var headerLine = index;
        var path = ParsePath(lines[index], DeletePrefix, headerLine);
        index++;
        string? revision = null;

        while (index < lines.Count - 1)
        {
            var line = lines[index];
            if (line.StartsWith(RevisionPrefix, StringComparison.Ordinal))
            {
                if (revision is not null)
                {
                    throw ParseError("Duplicate Revision directive.", index);
                }

                revision = ParseRevision(line[RevisionPrefix.Length..], index);
                index++;
                continue;
            }

            if (IsBlockStart(line) || line == EndMarker)
            {
                break;
            }

            if (line.Length == 0)
            {
                index++;
                continue;
            }

            throw ParseError(
                $"Unexpected delete directive/content: {line}",
                index);
        }

        if (revision is null)
        {
            throw ParseError(
                $"Delete block for '{path}' requires a Revision directive.",
                headerLine);
        }

        return new SourcePatchFileBlock(
            SourcePatchBlockKind.Delete,
            path,
            revision,
            null,
            null,
            null,
            FinalNewline: true,
            [],
            []);
    }

    private static SourcePatchHunk ParseHunk(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var header = lines[index];
        var label = header.Length > 2
            ? header[2..].Trim()
            : null;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = null;
        }

        index++;
        var items = new List<SourcePatchHunkLine>();
        var hasChange = false;
        var oldSideCount = 0;

        while (index < lines.Count - 1)
        {
            var line = lines[index];
            if (line.StartsWith("@@", StringComparison.Ordinal) ||
                IsBlockStart(line) ||
                line == EndMarker)
            {
                break;
            }

            if (line.Length == 0)
            {
                throw ParseError(
                    "Patch hunk lines require an explicit prefix: space, '-', or '+'. Use a single prefix character to represent an empty line.",
                    index);
            }

            var kind = line[0] switch
            {
                ' ' => SourcePatchLineKind.Context,
                '-' => SourcePatchLineKind.Delete,
                '+' => SourcePatchLineKind.Add,
                _ => throw ParseError(
                    "Patch hunk lines must start with space, '-', or '+'.",
                    index),
            };

            var text = line[1..];
            items.Add(new SourcePatchHunkLine(kind, text));

            if (kind is SourcePatchLineKind.Context or SourcePatchLineKind.Delete)
            {
                oldSideCount++;
            }

            if (kind is SourcePatchLineKind.Delete or SourcePatchLineKind.Add)
            {
                hasChange = true;
            }

            index++;
        }

        if (items.Count == 0)
        {
            throw ParseError("Patch hunk contains no lines.", index);
        }

        if (!hasChange)
        {
            throw ParseError("Patch hunk contains context but no change.", index);
        }

        if (oldSideCount == 0)
        {
            throw ParseError(
                "Pure insertion hunks are not allowed because they have no exact anchor. Include at least one exact context line.",
                index);
        }

        return new SourcePatchHunk(label, items);
    }

    private static string ParsePath(
        string line,
        string prefix,
        int zeroBasedLine)
    {
        var value = line[prefix.Length..];
        return RequireNonEmpty(value, "File path", zeroBasedLine);
    }

    private static string ParseRevision(string value, int zeroBasedLine)
    {
        try
        {
            return SourceEditRevision.Normalize(value);
        }
        catch (SourceEditDomainException ex)
        {
            throw ParseError(ex.Message, zeroBasedLine);
        }
    }

    private static string RequireNonEmpty(
        string value,
        string name,
        int zeroBasedLine)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw ParseError($"{name} cannot be empty.", zeroBasedLine);
        }

        return trimmed;
    }

    private static bool IsBlockStart(string line) =>
        line.StartsWith(UpdatePrefix, StringComparison.Ordinal) ||
        line.StartsWith(AddPrefix, StringComparison.Ordinal) ||
        line.StartsWith(DeletePrefix, StringComparison.Ordinal);

    private static SourceEditDomainException ParseError(
        string message,
        int? zeroBasedLine = null)
    {
        IReadOnlyDictionary<string, string>? details = null;
        if (zeroBasedLine is int line)
        {
            details = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["line"] = (line + 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        return new SourceEditDomainException(
            SourceEditCodes.PatchParseError,
            message,
            details: details);
    }
}
