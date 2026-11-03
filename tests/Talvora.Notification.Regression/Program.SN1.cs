using System.Text.Json;
using Talvora;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>SN1: every tool call is counted on its real line; the Control Center reads the same numbers.</summary>
    private static void AssertSn1LineTraffic()
    {
        var clock = new Sn1Clock(DateTimeOffset.Parse("2026-11-03T13:00:00Z"));
        var meter = new TalvoraLineTrafficMeter(clock);
        meter.Record("/mcp/dev", "openai-mcp", ok: true);
        clock.Now += TimeSpan.FromSeconds(1);
        meter.Record("/mcp/admin/", null, ok: false);
        meter.Record("/mcp", "codex-mcp-client", ok: true);
        meter.Record("/MCP", "claude-code", ok: true);

        var snapshot = meter.Snapshot(after: 0);
        if (snapshot.NextSeq != 4 || snapshot.Recent.Count != 4)
            throw new InvalidOperationException($"SN1: four calls must give four recent entries, got {snapshot.Recent.Count} (next {snapshot.NextSeq}).");
        var lines = snapshot.Lines.ToDictionary(line => line.Line);
        if (lines["dev"].Calls != 1 || lines["dev"].Failed != 0 ||
            lines["admin"].Calls != 1 || lines["admin"].Failed != 1 ||
            lines["core"].Calls != 2)
            throw new InvalidOperationException("SN1: calls and failures must be counted on the line the request arrived on.");
        if (lines["admin"].LastCallAtUtc != clock.Now)
            throw new InvalidOperationException("SN1: the last call time must be the measured completion time.");
        var clients = snapshot.Clients.Select(client => client.Line + ":" + client.Client).Order().ToArray();
        if (!clients.SequenceEqual(["admin:ChatGPT", "core:Claude", "core:Codex", "dev:ChatGPT"]))
            throw new InvalidOperationException("SN1: clients must be classified like the worklog actor, got " + string.Join(", ", clients));

        var later = meter.Snapshot(after: 2);
        if (later.Recent.Count != 2 || later.Recent[0].Seq != 3)
            throw new InvalidOperationException("SN1: 'after' must return only newer calls.");

        for (var i = 0; i < 300; i++) meter.Record("/mcp/dev", "openai", ok: true);
        var full = meter.Snapshot(after: 0);
        if (full.Recent.Count != TalvoraLineTrafficMeter.RecentCapacity || full.NextSeq != 304 ||
            full.Lines.Single(line => line.Line == "dev").Calls != 301)
            throw new InvalidOperationException("SN1: the recent ring is bounded but the counters never lose a call.");

        var json = JsonSerializer.Serialize(full, TalvoraLineTrafficMeter.JsonOptions);
        if (json.Contains("arguments", StringComparison.OrdinalIgnoreCase) || json.Contains("tool", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SN1: the traffic feed must not carry tool names or arguments.");
        var parsed = ControlCenterTrafficService.Parse(json);
        if (parsed.NextSeq != 304 || parsed.Lines["dev"].Calls != 301 || parsed.Recent.Count != TalvoraLineTrafficMeter.RecentCapacity ||
            parsed.StartedAtUtc != full.StartedAtUtc)
            throw new InvalidOperationException("SN1: the Control Center must read exactly what the service measured.");

        var tracker = new ControlCenterStateSinceTracker();
        var t0 = DateTimeOffset.Parse("2026-11-03T13:10:00Z");
        if (tracker.Observe("talvora-admin", ControlCenterHealthState.Ready, t0) != t0 ||
            tracker.Observe("talvora-admin", ControlCenterHealthState.Ready, t0.AddSeconds(5)) != t0 ||
            tracker.Observe("talvora-admin", ControlCenterHealthState.Offline, t0.AddSeconds(8)) != t0.AddSeconds(8) ||
            tracker.Observe("talvora-admin", ControlCenterHealthState.Offline, t0.AddSeconds(12)) != t0.AddSeconds(8))
            throw new InvalidOperationException("SN1: state-since must be the first time the current state was observed.");
        Console.WriteLine("SN1_LINE_TRAFFIC_GREEN");
    }

    private sealed class Sn1Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
