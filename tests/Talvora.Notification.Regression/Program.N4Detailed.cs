using System.Collections.Concurrent;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertN4CodexSnapshotAsync(string events, CodexCliEventResult parsed)
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-N4-Codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var file = Path.Combine(root, "Demo.cs");
        var progressFile = Path.Combine(root, "progress.json");
        await File.WriteAllTextAsync(file, "pre-existing dirty line\n");
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            var stream = new CodexCliStreamingProgress();
            await stream.ObserveAsync(events.AsMemory(), progressFile, CancellationToken.None);
            await stream.CompleteAsync(progressFile, CancellationToken.None);
            if (stream.Snapshot().ChangedFiles.Count != 1 ||
                File.ReadAllText(progressFile).Contains("SECRET_COMMAND_SENTINEL", StringComparison.Ordinal))
                throw new InvalidOperationException("N4: Codex stream lost applied paths or persisted a raw command.");
            notifier.BeginCodexJob("n4-codex", root);
            await File.WriteAllTextAsync(file, "pre-existing dirty line\nnew line\n");
            notifier.UpdateCodexJob("n4-codex", parsed.CompletedCommands, parsed.McpToolCalls,
                "Dosya düzenliyor · Demo.cs", TimeSpan.FromSeconds(4), parsed.ChangedFiles);
            var live = observed.Last(m => m.WorklogFrame?.Header.ActorName == "Codex");
            if (live.Evidence?.CodePreview is not { } preview || !preview.Contains("+new line", StringComparison.Ordinal) ||
                preview.Contains("-pre-existing dirty line", StringComparison.Ordinal) ||
                live.WorklogFrame!.Totals.FilesChanged != 1 ||
                live.WorklogFrame.Current.FactLine != "5 adım · 1 dosya değişti")
                throw new InvalidOperationException("N4: Codex preview attributed pre-existing edits or lost real totals.");
            notifier.UpdateCodexJob("n4-codex", 3, 1, null, TimeSpan.FromSeconds(5), ["../outside.cs"]);
            if (observed.Last().WorklogFrame?.Totals.FilesChanged != 1)
                throw new InvalidOperationException("N4: a path outside the Codex workspace was counted.");
            notifier.EndCodexJob("n4-codex", "completed", TimeSpan.FromSeconds(8), 3, 1,
                parsed.ChangedFiles, "Kısa sonuç: dosya güncellendi.");
            var done = observed.Last(m => m.Kind == DesktopProgressKind.Completed);
            if (done.WorklogFrame?.Header.ActorName != "Codex" ||
                done.WorklogFrame.Totals.FilesChanged != 1 ||
                done.WorklogFrame.Totals.Steps < 5 ||
                done.Evidence?.Result?.Contains("Kısa sonuç", StringComparison.Ordinal) != true ||
                done.Title.Contains("Kısa sonuç", StringComparison.Ordinal) ||
                done.Message.Contains("Kısa sonuç", StringComparison.Ordinal))
                throw new InvalidOperationException("N4: Codex completion totals or evidence-only summary mismatch.");
        }
        finally
        {
            await notifier.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertN4ClaudeAsync()
    {
        var fixture = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "N4-claude-events.jsonl")));
        var progressFile = Path.GetTempFileName();
        try
        {
            var stream = new ClaudeCliStreamingProgress();
            await stream.ObserveAsync(fixture.AsMemory(), progressFile, CancellationToken.None);
            await stream.CompleteAsync(progressFile, CancellationToken.None);
            var snapshot = stream.Snapshot();
            if (snapshot.ToolCalls != 2 || snapshot.PermissionDenials != 1 ||
                !snapshot.RecentActivities.Contains("Testleri çalıştırıyor") ||
                !snapshot.RecentActivities.Contains("Dosya düzenliyor · Demo.cs") ||
                File.ReadAllText(progressFile).Contains("SECRET_COMMAND_SENTINEL", StringComparison.Ordinal))
                throw new InvalidOperationException("N4: Claude events were not classified or safely persisted.");
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(observed);
            try
            {
                notifier.BeginClaudeJob("n4-claude");
                notifier.UpdateClaudeJob("n4-claude", snapshot.ToolCalls, TimeSpan.FromSeconds(25),
                    snapshot.FailedTools, snapshot.PermissionDenials, snapshot.RecentActivities.Last());
                var live = observed.Last();
                if (live.WorklogFrame?.Header.ActorName != "Claude" ||
                    live.WorklogFrame.ExpectedNextUpdateSeconds != 60 ||
                    live.WorklogFrame.Current.FactLine != "2 adım · 1 izin reddi")
                    throw new InvalidOperationException("N4: Claude heartbeat or activity totals do not match events.");
                notifier.EndClaudeJob("n4-claude", "completed", TimeSpan.FromSeconds(27),
                    snapshot.ToolCalls, snapshot.FailedTools, snapshot.PermissionDenials, snapshot.FinalAnswer);
                var done = observed.Last();
                if (done.Kind != DesktopProgressKind.Completed ||
                    done.Evidence?.Result?.Contains("Claude işi tamamladı.", StringComparison.Ordinal) != true ||
                    done.Message.Contains("Claude işi tamamladı.", StringComparison.Ordinal))
                    throw new InvalidOperationException("N4: Claude summary escaped the evidence-only surface.");
            }
            finally { await notifier.DisposeAsync(); }
        }
        finally { File.Delete(progressFile); }
    }
}
