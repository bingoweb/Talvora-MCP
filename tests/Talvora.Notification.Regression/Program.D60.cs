using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D60: a quiet wait keeps its existing session open, but does not add a step or start a session.</summary>
    private static async Task AssertD60QuietWaitKeepsSessionAsync()
    {
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        await using (var notifier = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            observed.Enqueue, static (_, _) => Task.CompletedTask,
            sessionIdleInterval: TimeSpan.FromMilliseconds(400)))
        {
            _ = await notifier.RunToolCallAsync("talvora_apply_patch", null,
                _ => ValueTask.FromResult(1), CancellationToken.None, "/mcp/dev", "ChatGPT");

            _ = await notifier.RunToolCallAsync("talvora_tail_text", null,
                async _ =>
                {
                    await Task.Delay(1200);
                    return 1;
                }, CancellationToken.None, "/mcp/dev", "ChatGPT");

            _ = await notifier.RunToolCallAsync("talvora_apply_patch", null,
                _ => ValueTask.FromResult(2), CancellationToken.None, "/mcp/dev", "ChatGPT");

            for (var attempt = 0; attempt < 40 &&
                !observed.Any(message => message.WorklogFrame?.Totals.Steps == 2); attempt++)
            {
                await Task.Delay(25);
            }
            var closedDuringWait = observed.Count(message => message.SessionClosed);
            var steps = observed.LastOrDefault(message => message.WorklogFrame?.Totals.Steps == 2)
                ?.WorklogFrame?.Totals.Steps;
            if (closedDuringWait != 0 || steps != 2)
            {
                throw new InvalidOperationException(
                    $"D60: a quiet 1.2 s wait must keep one 2-step session open; early closes={closedDuringWait}, steps={steps}.");
            }

            for (var attempt = 0; attempt < 40 && !observed.Any(message => message.SessionClosed); attempt++)
            {
                await Task.Delay(50);
            }
            if (observed.Count(message => message.SessionClosed) != 1)
            {
                throw new InvalidOperationException(
                    $"D60: after the final step, exactly one idle close is required; got {observed.Count(message => message.SessionClosed)}.");
            }
        }

        var emptyObserved = new ConcurrentQueue<DesktopProgressMessage>();
        await using (var empty = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            emptyObserved.Enqueue, static (_, _) => Task.CompletedTask,
            sessionIdleInterval: TimeSpan.FromMilliseconds(400)))
        {
            _ = await empty.RunToolCallAsync("talvora_tail_text", null,
                async _ => { await Task.Delay(100); return 1; },
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            await Task.Delay(75);
            if (!emptyObserved.IsEmpty)
                throw new InvalidOperationException("D60: a quiet call without an open session must remain invisible.");
        }
        Console.WriteLine("D60_QUIET_WAIT_KEEPS_SESSION_GREEN");
    }
}
