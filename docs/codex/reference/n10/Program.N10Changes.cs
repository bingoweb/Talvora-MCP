using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>N10: task line, step purpose and changed places come only from real input (§6.2).</summary>
    private static async Task AssertN10ChangesAsync()
    {
        static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

        var project = Path.Combine(Path.GetTempPath(), "Talvora-N10-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var messages = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(messages);
        try
        {
            await notifier.RunToolCallAsync<object>("talvora_worklog_note",
                Arguments(("workspaceRoot", project), ("text", "D12 — git pull\noturum kullanıcısıyla çalışsın")),
                _ => ValueTask.FromResult<object>(new { accepted = true }), CancellationToken.None);

            const string diff =
                "--- a/src/Talvora/Tools/GitTools.cs\n+++ b/src/Talvora/Tools/GitTools.cs\n" +
                "@@ -786,7 +786,11 @@ private static async Task<bool> ShouldUseInteractiveUserForRemoteGitAsync(\n" +
                "-                    \"fetch\",\n+                    \"fetch\",\n+                    \"pull\",\n" +
                "--- a/docs/README.md\n+++ b/docs/README.md\n@@ -10,2 +10,3 @@ ## Git kimliği\n+yeni satır\n" +
                "--- a/tests/x.ps1\n+++ b/tests/x.ps1\n@@ -1 +1 @@\n-a\n+b\n";
            var receipt = new
            {
                success = true, status = "committed",
                files = new object[]
                {
                    new { operation = "update", newPath = project + "\\src\\Talvora\\Tools\\GitTools.cs", addedLines = 4, removedLines = 0 },
                    new { operation = "update", newPath = project + "\\docs\\README.md", addedLines = 1, removedLines = 0 },
                    new { operation = "update", newPath = project + "\\tests\\x.ps1", addedLines = 1, removedLines = 1 },
                },
            };
            await notifier.RunToolCallAsync<object>("talvora_apply_patch",
                Arguments(("workspaceRoot", project), ("patch", diff), ("inputFormat", "unified-diff"),
                    ("purpose", "git pull artık oturum kullanıcısının SSH anahtarını kullanıyor")),
                _ => ValueTask.FromResult<object>(receipt), CancellationToken.None);

            var started = messages.First(message => message.ToolName == "apply_patch" &&
                message.Kind == DesktopProgressKind.Started).WorklogFrame!;
            if (started.Header.TaskNote != "D12 — git pull oturum kullanıcısıyla çalışsın")
                throw new InvalidOperationException($"N10: task note missing or not one line: '{started.Header.TaskNote}'.");
            if (started.Current.Purpose != "git pull artık oturum kullanıcısının SSH anahtarını kullanıyor" ||
                started.Current.Changes is not null)
                throw new InvalidOperationException("N10: running frame must carry the purpose and no changes yet.");

            var done = messages.Last(message => message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
            var changes = done.Current.Changes ?? throw new InvalidOperationException("N10: committed edit lost its changes.");
            if (changes.Count != 3 ||
                changes[0] is not { File: "GitTools.cs", Added: 4, Removed: 0 } ||
                !changes[0].Places.SequenceEqual(["ShouldUseInteractiveUserForRemoteGitAsync"]) ||
                !changes[1].Places.SequenceEqual(["Git kimliği"]) ||
                changes[2].Places.Count != 0)
                throw new InvalidOperationException("N10: wrong files, counts or places: " + JsonSerializer.Serialize(changes));

            // A rejected edit never claims changes.
            await notifier.RunToolCallAsync<object>("talvora_apply_patch",
                Arguments(("workspaceRoot", project), ("patch", diff), ("inputFormat", "unified-diff")),
                _ => ValueTask.FromResult<object>(new { success = false, status = "rejected", files = Array.Empty<object>(),
                    error = new { code = "EXPECTED_REVISION_MISMATCH" } }), CancellationToken.None);
            if (messages.Last(message => message.Kind == DesktopProgressKind.Failed).WorklogFrame!.Current.Changes is not null)
                throw new InvalidOperationException("N10: a rejected edit showed changes.");
        }
        finally
        {
            await notifier.DisposeAsync();
            Directory.Delete(project, recursive: true);
        }

        foreach (var (context, expected) in new (string, string?)[]
        {
            ("router.post('/pairing-codes', async (req, res) =>", "router.post"),
            ("public sealed class WorklogCardMapper", "WorklogCardMapper"),
            ("function Invoke-Checked {", "Invoke-Checked"),
            ("    if (pending.size >= limit) {", null),
            ("", null),
        })
        {
            if (Talvora.WorklogDiff.DeclarationLine.PlaceName(context) != expected)
                throw new InvalidOperationException($"N10: place for '{context}' was '{Talvora.WorklogDiff.DeclarationLine.PlaceName(context)}'.");
        }
    }
}
