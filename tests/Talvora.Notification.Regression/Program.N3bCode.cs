using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;
using Talvora.WorklogDiff;

internal static partial class Program
{
    private static async Task AssertN3bCodeTerminalAsync()
    {
        await AssertN3bClaudeResultsAsync();
        var before = "class Demo\n{\n    void Change()\n    {\n        // context one\n        // context two\n        // context three\n        // context four\n        Old();\n    }\n}\n";
        var diff = TextDiffBuilder.Build(before, before.Replace("Old();", "New();"), "Demo.cs", "Demo.cs");
        if (diff.Added != 1 || diff.Removed != 1 ||
            !diff.Preview!.Contains("@@ -6,6 +6,6 @@ void Change()", StringComparison.Ordinal))
            throw new InvalidOperationException("N3b: real diff lacks counts or nearest declaration context.");
        var sql = TextDiffBuilder.Build("-- comment\nkeep\n", "keep\n", "one.sql", "one.sql");
        if (sql.Removed != 1 || !sql.Preview!.Contains("--- comment"))
            throw new InvalidOperationException("N3b: a removed SQL comment was lost.");
        if (TextDiffBuilder.Build("same\r\n", "same\n", "one", "one").Summary != "Yalnız satır sonları değişti" ||
            TextDiffBuilder.Build("same", "same", "one", "one", encodingChanged: true).Summary != "Yalnız kodlama değişti")
            throw new InvalidOperationException("N3b: text-only conversion evidence is wrong.");
        var oversized = TextDiffBuilder.Build("", new string('x', 1024 * 1024 + 1), "large", "large");
        if (oversized.Preview is not null || !oversized.Summary!.StartsWith("Dosya çok büyük; değişiklik özeti:"))
            throw new InvalidOperationException("N3b: large-file preview guard is missing.");

        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            var args = new Dictionary<string, JsonElement>
            {
                ["workspaceRoot"] = JsonSerializer.SerializeToElement(@"C:\n3b"),
                ["patch"] = JsonSerializer.SerializeToElement("*** Begin Talvora Patch\n*** Update File: Demo.cs\n@@\n-REQUEST-ONLY\n+UNAPPLIED\n*** End Talvora Patch")
            };
            var preview = "@@ FILE Demo.cs\n--- a/Demo.cs\n+++ b/Demo.cs\n@@ -1 +1 @@ void Change()\n-old\n+committed\n";
            await notifier.RunToolCallAsync<object>("talvora_apply_patch", args,
                _ => ValueTask.FromResult<object>(new { success = true, status = "committed",
                    files = new[] { new { newPath = "Demo.cs", addedLines = 1, removedLines = 1, codePreview = preview } } }),
                CancellationToken.None);
            if (observed.Any(m => m.Kind == DesktopProgressKind.Started && m.Evidence?.CodePreview is not null))
                throw new InvalidOperationException("N3b: requested code appeared before commit.");
            var completed = observed.Last(m => m.Kind == DesktopProgressKind.Completed);
            if (completed.Evidence?.CodePreview != preview || string.IsNullOrEmpty(completed.Evidence.CodeChangeId))
                throw new InvalidOperationException("N3b: committed result preview was not published.");
            var codeId = completed.Evidence.CodeChangeId;
            var pane = new DesktopProgressCodeState();
            pane.Restore(completed);
            await notifier.RunToolCallAsync<object>("talvora_dotnet_build", args,
                async _ => { await Task.Delay(5500); return new { success = true, exitCode = 0 }; },
                CancellationToken.None);
            await notifier.RunToolCallAsync<object>("talvora_dotnet_test", args,
                _ => ValueTask.FromResult<object>(new { success = true, exitCode = 0 }), CancellationToken.None);
            foreach (var next in observed.Where(m => m.Sequence > completed.Sequence))
            {
                if (next.Evidence?.CodeChangeId != codeId || next.Evidence.CodePreview is not null ||
                    pane.Restore(next).Evidence?.CodePreview != preview)
                    throw new InvalidOperationException("N3b: build/test did not retain the applied code identity and pane.");
            }
            if (!observed.Any(m => m.Kind == DesktopProgressKind.Running))
                throw new InvalidOperationException("N3b: heartbeat fixture did not produce a heartbeat.");

            foreach (var tool in new[] { "talvora_structural_edit", "talvora_semantic_edit" })
            {
                object transaction = new { success = true, status = "committed",
                    files = new[] { new { newPath = "Demo.cs", addedLines = 1, removedLines = 1, codePreview = preview } } };
                object result = tool == "talvora_semantic_edit" ? new { success = true, transaction } : transaction;
                await notifier.RunToolCallAsync<object>(tool, args, _ => ValueTask.FromResult(result), CancellationToken.None);
                var applied = observed.Last(m => m.Kind == DesktopProgressKind.Completed);
                if (applied.Evidence?.AddedLines != 1 || applied.WorklogFrame?.Current.Changes?.Count != 1)
                    throw new InvalidOperationException("N3b: adapter results lost committed diff counts or places.");
            }

            // A realistic large committed preview is sent once; subsequent progress carries only its id.
            var large = "@@ FILE Large.cs\n" + new string('x', 300 * 1024);
            await notifier.RunToolCallAsync<object>("talvora_apply_patch", args,
                _ => ValueTask.FromResult<object>(new { success = true, status = "committed",
                    files = new[] { new { newPath = "Large.cs", addedLines = 1, removedLines = 0, codePreview = large } } }),
                CancellationToken.None);
            var largeEnd = observed.Last(m => m.Kind == DesktopProgressKind.Completed);
            await notifier.RunToolCallAsync<object>("talvora_dotnet_test", args,
                async _ => { await Task.Delay(5500); return new { success = true, exitCode = 0 }; }, CancellationToken.None);
            long maximumHeartbeatBytes = 0;
            foreach (var heartbeat in observed.Where(m => m.Sequence > largeEnd.Sequence && m.Kind == DesktopProgressKind.Running))
            {
                await using var wire = new MemoryStream();
                await DesktopProgressProtocol.WriteFrameAsync(wire, heartbeat);
                maximumHeartbeatBytes = Math.Max(maximumHeartbeatBytes, wire.Length);
                wire.Position = 0;
                var restored = await DesktopProgressProtocol.ReadFrameAsync(wire);
                if (restored?.Evidence?.CodeChangeId != largeEnd.Evidence?.CodeChangeId)
                    throw new InvalidOperationException("N3b: code identity was lost during the heartbeat round-trip.");
                if (wire.Length > 4096 || heartbeat.Evidence?.CodePreview is not null)
                    throw new InvalidOperationException($"N3b: heartbeat repeated preview or exceeded 4 KB ({wire.Length}).");
            }
            Console.WriteLine($"N3B_HEARTBEAT_MAX_BYTES={maximumHeartbeatBytes}");
        }
        finally { await notifier.DisposeAsync(); }

        var rejected = new ConcurrentQueue<DesktopProgressMessage>();
        var rejectedNotifier = CreateNotifier(rejected);
        try
        {
            var args = new Dictionary<string, JsonElement> {
                ["workspaceRoot"] = JsonSerializer.SerializeToElement(@"C:\rejected"),
                ["patch"] = JsonSerializer.SerializeToElement("*** Update File: Demo.cs\n@@\n-old\n+NEVER-APPLIED") };
            await rejectedNotifier.RunToolCallAsync<object>("talvora_apply_patch", args,
                _ => ValueTask.FromResult<object>(new { success = false, status = "rejected", files = Array.Empty<object>(),
                    error = new { code = "STALE_REVISION", message = "stale" } }), CancellationToken.None);
            if (rejected.Any(m => m.Evidence?.CodePreview is not null) ||
                !rejected.Any(m => m.Evidence?.Summary == "Değişiklik uygulanmadı"))
                throw new InvalidOperationException("N3b: a rejected change exposed code or omitted its unapplied fact.");
        }
        finally { await rejectedNotifier.DisposeAsync(); }
    }
}
