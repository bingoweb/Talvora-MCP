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

                files.Add((System.IO.Path.GetFileName(filePath), 0, 0));
                lines.Add(new DiffLine(DiffLineKind.File, string.Empty, filePath.Replace('\\', '/')));
                oldNumber = null;
                newNumber = null;
                continue;
            }

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                var match = HunkHeader().Match(raw);
                oldNumber = match.Success ? int.Parse(match.Groups["old"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                newNumber = match.Success ? int.Parse(match.Groups["new"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                lines.Add(new DiffLine(DiffLineKind.Hunk, string.Empty, raw));
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

        return (files.Select(f => new DiffFileTab(f.Name, f.Added, f.Removed)).ToArray(), lines);
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
