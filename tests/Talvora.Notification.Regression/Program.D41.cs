using System.Collections.Concurrent;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D41: one caller name; Talvora's own calls leave no trace; the bridge header names Claude.</summary>
    private static async Task AssertD41CallerIdentityAsync()
    {
        if (TalvoraLineTrafficMeter.ResolveCallerName("Claude", "bridge", "node") != "Claude" ||
            TalvoraLineTrafficMeter.ResolveCallerName(null, "Talvora.Smoke", "node") != "Talvora.Smoke" ||
            TalvoraLineTrafficMeter.ResolveCallerName(" ", null, "codex_cli_rs/0.1") != "codex_cli_rs/0.1" ||
            TalvoraLineTrafficMeter.ResolveCallerName(null, null, "") is not null ||
            !TalvoraLineTrafficMeter.IsOwnClient("Talvora.Smoke") || TalvoraLineTrafficMeter.IsOwnClient("Claude"))
            throw new InvalidOperationException("D41: the caller name order is header, client name, User-Agent.");

        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            _ = await notifier.RunToolCallAsync<int>("talvora_apply_patch", null,
                _ => ValueTask.FromResult(1), CancellationToken.None, "/mcp/dev", "Talvora.Smoke");
            if (!observed.IsEmpty)
                throw new InvalidOperationException("D41: Talvora's own calls must leave no desktop card or worklog session.");
            foreach (var (client, actor) in new[] { ("Claude", "Claude"), ("curl/8.9.1", "Diğer") })
            {
                _ = await notifier.RunToolCallAsync<int>("talvora_apply_patch", null,
                    _ => ValueTask.FromResult(1), CancellationToken.None, "/mcp", client);
                var named = observed.LastOrDefault(message => message.WorklogFrame is not null)
                    ?.WorklogFrame?.Header.ActorName;
                if (named != actor)
                    throw new InvalidOperationException($"D41: caller {client} must be named {actor}, got {named}.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
        Console.WriteLine("D41_CALLER_IDENTITY_GREEN");
    }
}
