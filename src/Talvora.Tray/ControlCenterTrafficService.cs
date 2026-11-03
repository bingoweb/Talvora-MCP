using System.IO;
using System.Net.Http;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record TrafficFeedLine(string Line, long Calls, long Failed, DateTimeOffset? LastCallAtUtc);

internal sealed record TrafficFeedCall(long Seq, string Line, string Client, DateTimeOffset AtUtc, bool Ok);

internal sealed record TrafficFeedClient(string Line, string Client, DateTimeOffset LastSeenAtUtc);

/// <summary>SN1: what the service measured, read as-is. Lines are keyed core / dev / admin.</summary>
internal sealed record TrafficFeed(
    DateTimeOffset StartedAtUtc,
    long NextSeq,
    IReadOnlyDictionary<string, TrafficFeedLine> Lines,
    IReadOnlyList<TrafficFeedCall> Recent,
    IReadOnlyList<TrafficFeedClient> Clients);

/// <summary>SN1: reads <c>/healthz/traffic</c> from the local Talvora service (loopback only).</summary>
internal static class ControlCenterTrafficService
{
    private const string TrafficUrl = "http://127.0.0.1:7676/healthz/traffic";
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(timeout: TimeSpan.FromSeconds(2));
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<TrafficFeed> GetAsync(long after, CancellationToken cancellationToken)
    {
        var json = await Http.GetStringAsync(TrafficUrl + "?after=" + Math.Max(0, after), cancellationToken);
        return Parse(json);
    }

    public static TrafficFeed Parse(string json)
    {
        var wire = JsonSerializer.Deserialize<Wire>(json, Options)
            ?? throw new InvalidDataException("Talvora traffic feed is empty.");
        return new TrafficFeed(
            wire.StartedAtUtc,
            wire.NextSeq,
            (wire.Lines ?? []).ToDictionary(line => line.Line, StringComparer.OrdinalIgnoreCase),
            wire.Recent ?? [],
            wire.Clients ?? []);
    }

    private sealed record Wire(
        DateTimeOffset StartedAtUtc,
        long NextSeq,
        TrafficFeedLine[]? Lines,
        TrafficFeedCall[]? Recent,
        TrafficFeedClient[]? Clients);
}

/// <summary>
/// SN1: the first time the Control Center observed each service's current health state. The reconnect timer
/// on the switchboard counts from this measured moment, never from a guess.
/// </summary>
internal sealed class ControlCenterStateSinceTracker
{
    private readonly Dictionary<string, (ControlCenterHealthState State, DateTimeOffset Since)> _seen =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Observe(string id, ControlCenterHealthState state, DateTimeOffset now)
    {
        if (_seen.TryGetValue(id, out var known) && known.State == state) return known.Since;
        _seen[id] = (state, now);
        return now;
    }
}
