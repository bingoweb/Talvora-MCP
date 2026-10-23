using System.Text.Json;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertN3bClaudeResultsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-N3b-Claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var progressPath = Path.Combine(root, "progress.json");
        try
        {
            var tracker = new ClaudeCliStreamingProgress(root);
            var file = Path.Combine(root, "Demo.cs");
            string ToolUse(string id) => JsonSerializer.Serialize(new { type = "assistant",
                message = new { content = new[] { new { type = "tool_use", id, name = "Edit",
                    input = new { file_path = file, old_string = "REQUEST", new_string = "UNAPPLIED" } } } } });
            string Result(string id, bool failed, bool staged = false, string? path = null) =>
                JsonSerializer.Serialize(new { type = "user", message = new { content = new[] {
                    new { type = "tool_result", tool_use_id = id, is_error = failed, content = "done" } } },
                    tool_use_result = new { filePath = path ?? file, originalFile = "old\n", oldString = "old", newString = "real",
                        replaceAll = false, staged,
                        structuredPatch = new[] { new { oldStart = 1, oldLines = 1, newStart = 1, newLines = 1,
                            lines = new[] { "-old", "+real" } } } } });
            await tracker.ObserveAsync((ToolUse("failed") + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (tracker.Snapshot().CodeChange is not null) throw new InvalidOperationException("N3b Claude request leaked code.");
            await tracker.ObserveAsync((Result("failed", true) + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (tracker.Snapshot().CodeChange is not null) throw new InvalidOperationException("N3b Claude rejected edit leaked code.");
            await tracker.ObserveAsync((ToolUse("staged") + "\n" + Result("staged", false, true) + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (tracker.Snapshot().CodeChange is not null) throw new InvalidOperationException("N3b Claude staged edit leaked code.");
            await tracker.ObserveAsync((ToolUse("outside") + "\n" + Result("outside", false, path: Path.Combine(Path.GetTempPath(), "outside.txt")) + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (tracker.Snapshot().CodeChange is not null) throw new InvalidOperationException("N3b Claude outside edit leaked code.");
            await tracker.ObserveAsync((ToolUse("real") + "\n" + Result("real", false) + "\n").AsMemory(), progressPath, CancellationToken.None);
            var change = tracker.Snapshot().CodeChange ?? throw new InvalidOperationException("N3b Claude applied result missing.");
            var evidence = Talvora.TalvoraDesktopProgressNotifier.BuildClaudeCodeEvidence(change, root);
            if (evidence?.CodePreview?.Contains("+real") != true || evidence.CodePreview.Contains("UNAPPLIED"))
                throw new InvalidOperationException("N3b Claude preview did not use the actual tool result.");
            await tracker.ObserveAsync((ToolUse("multi").Replace("\"Edit\"", "\"MultiEdit\"") + "\n" +
                Result("multi", false) + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (Talvora.TalvoraDesktopProgressNotifier.BuildClaudeCodeEvidence(tracker.Snapshot().CodeChange!, root)?.CodePreview?.Contains("+real") != true)
                throw new InvalidOperationException("N3b Claude MultiEdit actual result missing.");
            var writeResult = JsonSerializer.Serialize(new { type = "user",
                message = new { content = new[] { new { type = "tool_result", tool_use_id = "write", is_error = false } } },
                tool_use_result = new { type = "create", filePath = file, content = "created\npassword=fixture-sensitive-value\n",
                    originalFile = (string?)null, structuredPatch = Array.Empty<object>() } });
            await tracker.ObserveAsync((ToolUse("write").Replace("\"Edit\"", "\"Write\"") + "\n" + writeResult + "\n")
                .AsMemory(), progressPath, CancellationToken.None);
            var written = Talvora.TalvoraDesktopProgressNotifier.BuildClaudeCodeEvidence(tracker.Snapshot().CodeChange!, root);
            if (written?.CodePreview?.Contains("+created") != true || written.CodePreview.Contains("fixture-sensitive-value"))
                throw new InvalidOperationException("N3b Claude Write result was lost or not redacted.");
            await tracker.CompleteAsync(progressPath, CancellationToken.None);
            var bounded = new ClaudeCliStreamingProgress(root);
            await bounded.ObserveAsync((ToolUse("bounded") + "\n").AsMemory(), progressPath, CancellationToken.None);
            var oversized = JsonSerializer.Serialize(new { type = "user",
                message = new { content = new[] { new { type = "tool_result", tool_use_id = "bounded", is_error = false } } },
                tool_use_result = new { filePath = file, originalFile = new string('x', 1024 * 1024 + 1) } });
            await bounded.ObserveAsync((oversized + "\n").AsMemory(), progressPath, CancellationToken.None);
            if (bounded.Snapshot().CodeChange is not null)
                throw new InvalidOperationException("N3b: the existing one-MB Claude event guard was weakened.");
            if ((await File.ReadAllTextAsync(progressPath)).Contains("UNAPPLIED"))
                throw new InvalidOperationException("N3b Claude persisted raw request arguments.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
