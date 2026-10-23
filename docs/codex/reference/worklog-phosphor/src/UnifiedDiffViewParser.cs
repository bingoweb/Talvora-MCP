using System.Text.RegularExpressions;

namespace Talvora.Tray.Worklog;

/// <summary>
/// Turns the redacted unified-diff preview of a real code change into terminal rows and per-file tabs.
/// Understands "diff --git", "+++ b/…", Talvora's "@@ FILE …" marker, "@@ -a,b +c,d @@" hunks,
/// '+', '-', ' ' lines and "…" notes. Never invents numbers: when no hunk header is known, numbers stay empty.
/// </summary>
public static partial class UnifiedDiffViewParser
{
    public static (IReadOnlyList<DiffFileTab> Files, IReadOnlyList<DiffLine> Lines) Parse(string? preview)
    {
        var files = new List<(string Name, int Added, int Removed)>();
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(preview))
        {
            return ([], []);
        }

        int? oldNumber = null;
        int? newNumber = null;
        string? announcedFile = null;

        foreach (var raw in preview.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("diff --git ", StringComparison.Ordinal) ||
                raw.StartsWith("--- ", StringComparison.Ordinal) ||
                raw.StartsWith("index ", StringComparison.Ordinal))
            {
                continue;
            }

            string? filePath = null;
            if (raw.StartsWith("+++ ", StringComparison.Ordinal))
            {
                filePath = raw[4..].Trim();
                if (filePath.StartsWith("b/", StringComparison.Ordinal))
                {
                    filePath = filePath[2..];
                }
            }
            else if (raw.StartsWith("@@ FILE ", StringComparison.Ordinal))
            {
                filePath = raw[8..].Trim();
            }

            if (filePath is not null)
            {
                if (filePath == "/dev/null")
                {
                    continue;
                }

                // "@@ FILE x" then "--- a/x" / "+++ b/x" describe the same file: one header, one tab.
                if (raw.StartsWith("+++ ", StringComparison.Ordinal) &&
                    string.Equals(announcedFile, filePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    announcedFile = null;
                    continue;
                }

                announcedFile = raw.StartsWith("@@ FILE ", StringComparison.Ordinal) ? filePath.Replace('\\', '/') : null;

                files.Add((System.IO.Path.GetFileName(filePath), 0, 0));
                lines.Add(new DiffLine(DiffLineKind.File, string.Empty, filePath.Replace('\\', '/')));
                oldNumber = null;
                newNumber = null;
                continue;
            }

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                announcedFile = null;
                var match = HunkHeader().Match(raw);
                oldNumber = match.Success ? int.Parse(match.Groups["old"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                newNumber = match.Success ? int.Parse(match.Groups["new"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                lines.Add(new DiffLine(DiffLineKind.Hunk, string.Empty, CompactHunk(raw, match)));
                continue;
            }

            if (raw.StartsWith('+'))
            {
                lines.Add(new DiffLine(DiffLineKind.Added, Number(newNumber), raw[1..]));
                newNumber++;
                Count(files, added: 1);
                continue;
            }

            if (raw.StartsWith('-'))
            {
                lines.Add(new DiffLine(DiffLineKind.Removed, Number(oldNumber), raw[1..]));
                oldNumber++;
                Count(files, removed: 1);
                continue;
            }

            if (raw.StartsWith(' '))
            {
                lines.Add(new DiffLine(DiffLineKind.Context, Number(newNumber), raw[1..]));
                oldNumber++;
                newNumber++;
                continue;
            }

            if (raw.Length > 0)
            {
                lines.Add(new DiffLine(DiffLineKind.Note, string.Empty, raw));
            }
        }

        Dedent(lines);
        Emphasize(lines);
        return (files.Select(f => new DiffFileTab(f.Name, f.Added, f.Removed)).ToArray(), lines);
    }

    /// <summary>"@@ -790,6 +790,10 @@ private static … Name(" → "@@ 790 · private static … Name(" (max 48 chars, one line).</summary>
    private static string CompactHunk(string raw, Match match)
    {
        if (!match.Success)
        {
            return raw;
        }

        var context = raw[match.Length..].Trim();
        var text = "@@ " + match.Groups["new"].Value + (context.Length > 0 ? " · " + context : string.Empty);
        return text.Length <= 48 ? text : text[..47] + "…";
    }

    /// <summary>
    /// Display only: removes the indentation every code line of a hunk shares, so deeply nested code does not
    /// wrap on its leading spaces. Relative indentation is kept; "kopyala" still copies the real preview.
    /// </summary>
    private static void Dedent(List<DiffLine> lines)
    {
        var start = 0;
        while (start < lines.Count)
        {
            var end = start;
            while (end < lines.Count && lines[end].Kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed)
            {
                end++;
            }

            if (end > start)
            {
                var common = lines.Skip(start).Take(end - start)
                    .Where(line => line.Text.Trim().Length > 0)
                    .Select(line => line.Text.Length - line.Text.TrimStart(' ').Length)
                    .DefaultIfEmpty(0)
                    .Min();
                if (common > 0)
                {
                    for (var index = start; index < end; index++)
                    {
                        var text = lines[index].Text;
                        lines[index] = lines[index] with { Text = text.Length >= common ? text[common..] : text.TrimStart(' ') };
                    }
                }
            }

            start = Math.Max(end, start + 1);
        }
    }

    /// <summary>
    /// Word-level highlight (DESIGN-WORKLOG §6.3). A block of removed lines directly followed by a block of
    /// added lines is paired line by line in order (up to 8 pairs; extra added lines are new code). Each pair
    /// marks the span between the common prefix and suffix, widened to whole words. Pairs that share less
    /// than half of the longer line are a rewrite, not an edit, and stay unmarked.
    /// </summary>
    private static void Emphasize(List<DiffLine> lines)
    {
        var start = 0;
        while (start < lines.Count)
        {
            if (lines[start].Kind != DiffLineKind.Removed)
            {
                start++;
                continue;
            }

            var removedEnd = start;
            while (removedEnd < lines.Count && lines[removedEnd].Kind == DiffLineKind.Removed)
            {
                removedEnd++;
            }

            var addedEnd = removedEnd;
            while (addedEnd < lines.Count && lines[addedEnd].Kind == DiffLineKind.Added)
            {
                addedEnd++;
            }

            var count = Math.Min(Math.Min(removedEnd - start, addedEnd - removedEnd), 8);
            {
                for (var index = 0; index < count; index++)
                {
                    var before = lines[start + index];
                    var after = lines[removedEnd + index];
                    var prefix = WordStart(before.Text, CommonPrefix(before.Text, after.Text));
                    var suffix = WordEnd(before.Text, CommonSuffix(before.Text, after.Text, prefix));
                    var longer = Math.Max(before.Text.Length, after.Text.Length);
                    if ((prefix + suffix) * 2 < longer)
                    {
                        continue;
                    }

                    lines[start + index] = before with
                    {
                        EmphasisStart = prefix,
                        EmphasisLength = before.Text.Length - prefix - suffix,
                    };
                    lines[removedEnd + index] = after with
                    {
                        EmphasisStart = prefix,
                        EmphasisLength = after.Text.Length - prefix - suffix,
                    };
                }
            }

            start = Math.Max(addedEnd, start + 1);
        }
    }

    private static int CommonPrefix(string a, string b)
    {
        var length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length])
        {
            length++;
        }

        return length;
    }

    private static int CommonSuffix(string a, string b, int prefix)
    {
        var length = 0;
        while (length < a.Length - prefix && length < b.Length - prefix &&
               a[a.Length - 1 - length] == b[b.Length - 1 - length])
        {
            length++;
        }

        return length;
    }

    /// <summary>Moves a prefix length back to the start of the word it cuts ("limit = 5" | "0" → "limit = " | "50").</summary>
    private static int WordStart(string text, int prefix)
    {
        while (prefix > 0 && prefix <= text.Length && char.IsLetterOrDigit(text[prefix - 1]))
        {
            prefix--;
        }

        return prefix;
    }

    /// <summary>Shrinks a suffix length so it does not start inside a word.</summary>
    private static int WordEnd(string text, int suffix)
    {
        while (suffix > 0 && text.Length - suffix < text.Length &&
               char.IsLetterOrDigit(text[text.Length - suffix]) &&
               text.Length - suffix - 1 >= 0 && char.IsLetterOrDigit(text[text.Length - suffix - 1]))
        {
            suffix--;
        }

        return suffix;
    }

    private static string Number(int? value) =>
        value is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private static void Count(List<(string Name, int Added, int Removed)> files, int added = 0, int removed = 0)
    {
        if (files.Count == 0)
        {
            files.Add(("değişiklik", 0, 0));
        }

        var last = files[^1];
        files[^1] = (last.Name, last.Added + added, last.Removed + removed);
    }

    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
