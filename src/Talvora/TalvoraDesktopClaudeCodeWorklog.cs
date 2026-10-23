using System.Text.Json;
using Talvora.Shared;
using Talvora.SourceEditing;
using Talvora.WorklogDiff;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    internal static DesktopProgressEvidence? BuildClaudeCodeEvidence(ClaudeAppliedCodeResult change, string workingDirectory)
    {
        try
        {
            var workspace = SourceWorkspaceClassifier.Classify(workingDirectory).WorkspaceRoot;
            var classification = SourceWorkspaceClassifier.Classify(change.File);
            if (workspace is null || !classification.IsDevelopmentWorkspace ||
                !string.Equals(workspace, classification.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return null;
            var result = change.Result;
            var before = result.TryGetProperty("originalFile", out var original) && original.ValueKind == JsonValueKind.String
                ? original.GetString() : null;
            var create = FactText(result, "type") == "create";
            string? after = null;
            int? added = null, removed = null;
            if (change.ToolName == "Write" && result.TryGetProperty("content", out var written) && written.ValueKind == JsonValueKind.String)
            {
                after = written.GetString();
                if (create) before = "";
            }
            if (result.TryGetProperty("structuredPatch", out var patches) && patches.ValueKind == JsonValueKind.Array)
            {
                var hunks = patches.EnumerateArray().ToArray();
                added = hunks.Sum(hunk => hunk.GetProperty("lines").EnumerateArray().Count(line => line.GetString()?.StartsWith('+') == true));
                removed = hunks.Sum(hunk => hunk.GetProperty("lines").EnumerateArray().Count(line => line.GetString()?.StartsWith('-') == true));
                if (after is null && before is not null && hunks.Length > 0)
                {
                    var lines = before.Replace("\r\n", "\n").Split('\n');
                    var applied = new List<string>();
                    var cursor = 0;
                    foreach (var hunk in hunks)
                    {
                        var start = Math.Max(0, hunk.GetProperty("oldStart").GetInt32() - 1);
                        if (start < cursor || start > lines.Length) return null;
                        while (cursor < start) applied.Add(lines[cursor++]);
                        foreach (var row in hunk.GetProperty("lines").EnumerateArray())
                        {
                            var line = row.GetString() ?? "";
                            if (line.Length == 0 || line[0] == '\\') continue;
                            if (line[0] is ' ' or '-')
                            {
                                if (cursor >= lines.Length || lines[cursor] != line[1..]) return null;
                                cursor++;
                            }
                            if (line[0] is ' ' or '+') applied.Add(line[1..]);
                        }
                    }
                    while (cursor < lines.Length) applied.Add(lines[cursor++]);
                    after = string.Join("\n", applied);
                }
            }
            var name = Path.GetRelativePath(workspace, change.File).Replace('\\', '/');
            if (before is null || after is null)
                return new DesktopProgressEvidence("1 dosya değişti", [name], added, removed, CodeChangeId: change.Id);
            var diff = TextDiffBuilder.Build(before, after, create ? "" : name, name,
                result.TryGetProperty("originalFileBytes", out var size) ? size.GetInt64() : null,
                result.TryGetProperty("contentBytes", out var finalSize) ? finalSize.GetInt64() : null);
            return new DesktopProgressEvidence(diff.Summary ?? "1 dosya değişti", [name],
                create ? diff.Added : added ?? diff.Added, create ? diff.Removed : removed ?? diff.Removed,
                diff.Preview, CodeChangeId: change.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { return null; }
    }

    internal void ObserveClaudeCodeResult(string jobId, string workingDirectory, ClaudeAppliedCodeResult change, TimeSpan elapsed)
    {
        var evidence = BuildClaudeCodeEvidence(change, workingDirectory);
        if (evidence is null) return;
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue("claude-" + jobId, out var item)) return;
            Enqueue(item.Session.Id, item.ToolName, item.Narrative.Titles.Running,
                WorklogCopy.RunningFallback, DesktopProgressKind.Running, elapsed, evidence);
        }
    }
}
