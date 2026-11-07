using System.Text.Json;
using Talvora.Shared;

namespace Talvora;

/// <summary>
/// SN1: counts every finished tool call on the line it arrived on (core <c>/mcp</c>, dev, admin) so the
/// Control Center's switchboard draws only measured traffic. It keeps no tool names or arguments.
/// </summary>
internal sealed class TalvoraLineTrafficMeter
{
    public const int RecentCapacity = 256;
    public static readonly string[] LineIds = ["core", "dev", "admin"];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, LineCounter> _lines = LineIds.ToDictionary(id => id, _ => new LineCounter());
    private readonly Dictionary<(string Line, string Client), DateTimeOffset> _clients = [];
    private readonly Queue<TrafficCall> _recent = new();
    private long _sequence;

    public TalvoraLineTrafficMeter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        StartedAtUtc = _time.GetUtcNow();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public static string ResolveLine(string? surfacePath) =>
        TalvoraMcpToolSurfacePolicy.ResolvePath(surfacePath) switch
        {
            TalvoraMcpToolSurface.Development => "dev",
            TalvoraMcpToolSurface.Administration => "admin",
            _ => "core",
        };

    // Same names as the worklog actor (TalvoraDesktopWorklogState.ResolveActor).
    public static string ResolveClient(string? clientName, string line)
    {
        if (clientName?.Contains("codex", StringComparison.OrdinalIgnoreCase) == true) return "Codex";
        if (clientName?.Contains("claude", StringComparison.OrdinalIgnoreCase) == true) return "Claude";
        if (line is "dev" or "admin") return "ChatGPT";
        if (clientName?.Contains("openai", StringComparison.OrdinalIgnoreCase) == true ||
            clientName?.Contains("chatgpt", StringComparison.OrdinalIgnoreCase) == true) return "ChatGPT";
        return "Diğer";
    }

    public void Record(string? surfacePath, string? clientName, bool ok)
    {
        // D32: Talvora's own clients are not traffic (user decision 2026-11-07).
        if (clientName?.StartsWith("Talvora.", StringComparison.OrdinalIgnoreCase) == true) return;
        var line = ResolveLine(surfacePath);
        var client = ResolveClient(clientName, line);
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            var counter = _lines[line];
            counter.Calls++;
            if (!ok) counter.Failed++;
            counter.LastCallAtUtc = now;
            _clients[(line, client)] = now;
            _recent.Enqueue(new TrafficCall(++_sequence, line, client, now, ok));
            while (_recent.Count > RecentCapacity) _recent.Dequeue();
        }
    }

    public TrafficSnapshot Snapshot(long after)
    {
        lock (_gate)
        {
            return new TrafficSnapshot(
                StartedAtUtc,
                _sequence,
                LineIds.Select(id => new TrafficLine(id, _lines[id].Calls, _lines[id].Failed, _lines[id].LastCallAtUtc)).ToArray(),
                _recent.Where(call => call.Seq > after).ToArray(),
                _clients.Select(pair => new TrafficClient(pair.Key.Line, pair.Key.Client, pair.Value)).ToArray());
        }
    }

    private sealed class LineCounter
    {
        public long Calls;
        public long Failed;
        public DateTimeOffset? LastCallAtUtc;
    }
}

internal sealed record TrafficSnapshot(
    DateTimeOffset StartedAtUtc,
    long NextSeq,
    IReadOnlyList<TrafficLine> Lines,
    IReadOnlyList<TrafficCall> Recent,
    IReadOnlyList<TrafficClient> Clients);

internal sealed record TrafficLine(string Line, long Calls, long Failed, DateTimeOffset? LastCallAtUtc);

internal sealed record TrafficCall(long Seq, string Line, string Client, DateTimeOffset AtUtc, bool Ok);

internal sealed record TrafficClient(string Line, string Client, DateTimeOffset LastSeenAtUtc);
