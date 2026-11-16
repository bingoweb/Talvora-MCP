using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD102ReopenedTaskNoteAsync()
    {
        var project = Path.Combine(Path.GetTempPath(), "talvora-d102-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        static Dictionary<string, JsonElement> Args(string path, string? note = null)
        {
            var values = new Dictionary<string, JsonElement>
            {
                ["workspaceRoot"] = JsonSerializer.SerializeToElement(path),
            };
            if (note is not null) values["text"] = JsonSerializer.SerializeToElement(note);
            return values;
        }

        static async Task<DesktopProgressMessage> AwaitMessageAsync(
            ConcurrentQueue<DesktopProgressMessage> messages,
            Func<DesktopProgressMessage, bool> predicate)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var found = messages.LastOrDefault(predicate);
                if (found is not null) return found;
                await Task.Delay(20);
            }
            throw new InvalidOperationException("D102: expected session notification was not delivered.");
        }

        try
        {
            await using var notifier = new TalvoraDesktopProgressNotifier(
                NullLogger<TalvoraDesktopProgressNotifier>.Instance,
                observed.Enqueue, static (_, _) => Task.CompletedTask,
                sessionIdleInterval: TimeSpan.FromMilliseconds(350));

            async Task<DesktopProgressMessage> StepAsync(string tool)
            {
                var before = observed.ToArray().Length;
                var result = tool == "talvora_apply_patch"
                    ? new { success = true, status = "committed" }
                    : new { success = true, status = "ok" };
                await notifier.RunToolCallAsync(
                    tool, Args(project), _ => ValueTask.FromResult(result),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                return await AwaitMessageAsync(observed,
                    message => !message.SessionClosed &&
                        message.Kind == DesktopProgressKind.Completed &&
                        message.WorklogFrame is not null &&
                        observed.ToArray().Skip(before).Contains(message));
            }

            async Task SetNoteAsync(string note)
            {
                await notifier.RunToolCallAsync(
                    "talvora_worklog_note", Args(project, note),
                    _ => ValueTask.FromResult(new { accepted = true }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
            }

            var first = await StepAsync("talvora_apply_patch");
            var firstTitle = first.WorklogFrame!.Current.Title;
            if (first.WorklogFrame.Totals.Steps != 1)
                throw new InvalidOperationException("D102: first step must produce one history row.");

            // A real idle close must have occurred before the next note.
            await AwaitMessageAsync(observed, message =>
                message.SessionClosed &&
                message.WorklogFrame?.SessionId == first.WorklogFrame.SessionId);
            var beforeNote = observed.ToArray().Length;
            const string reopenedNote = "D102 — Yeniden açılan kartın görevi";
            await SetNoteAsync(reopenedNote);
            await Task.Delay(85); // Allow the observer to receive a wrongly published ghost frame.
            var noteFrames = observed.ToArray().Skip(beforeNote).ToArray();
            if (noteFrames.Any(message => message.WorklogFrame?.Totals.Steps == 0 &&
                message.WorklogFrame.Current.Title == firstTitle))
                throw new InvalidOperationException(
                    "D102 RED: an idle-reopened note published the previous terminal title with zero steps.");

            var next = await StepAsync("talvora_run_powershell");
            if (next.WorklogFrame?.Header.TaskNote != reopenedNote ||
                next.WorklogFrame.Totals.Steps != 1 ||
                next.WorklogFrame.Timeline.Count != 1 ||
                next.WorklogFrame.SessionId != first.WorklogFrame.SessionId)
                throw new InvalidOperationException(
                    $"D102 RED: first reopened step lost the note, note='{next.WorklogFrame?.Header.TaskNote}', " +
                    $"steps={next.WorklogFrame?.Totals.Steps}.");
            if (observed.Any(message => message.WorklogFrame?.Totals.Steps == 0 &&
                message.WorklogFrame.Current.Title == firstTitle &&
                message.WorklogFrame.Header.TaskNote == reopenedNote))
                throw new InvalidOperationException("D102: a ghost note frame was delivered as an old step.");

            // Setting a note on a still-active session must immediately refresh its visible card.
            const string updatedNote = "D102 — Güncellenen görev notu";
            await SetNoteAsync(updatedNote);
            var updated = await AwaitMessageAsync(observed,
                message => message.WorklogFrame?.Header.TaskNote == updatedNote &&
                    !message.SessionClosed);
            if (updated.WorklogFrame?.Totals.Steps != 1)
                throw new InvalidOperationException("D102: changing an open task note lost its existing step.");

            // Reopening solely for a note must also stay silent if no next step arrives.
            await AwaitMessageAsync(observed, message =>
                message.SessionClosed &&
                message.WorklogFrame?.Header.TaskNote == updatedNote &&
                message.WorklogFrame.Totals.Steps == 1);
            var beforeAbandonedNote = observed.ToArray().Length;
            await SetNoteAsync("D102 — Henüz yeni adım yok");
            await Task.Delay(760);
            var abandonedFrames = observed.ToArray().Skip(beforeAbandonedNote).ToArray();
            if (abandonedFrames.Any(message => message.WorklogFrame?.Totals.Steps == 0 &&
                message.WorklogFrame.Current.Title == next.WorklogFrame!.Current.Title))
                throw new InvalidOperationException(
                    "D102 RED: a note-only reopened session emitted the old step on idle close.");
        }
        finally
        {
            Directory.Delete(project, recursive: true);
        }
        Console.WriteLine("D102_REOPENED_TASK_NOTE_GREEN");
    }
}
