using Talvora.Shared;

namespace Talvora.Tray;

internal enum WorklogSessionTabState
{
    Running,
    Silent,
    Succeeded,
    Failed,
    Cancelled,
}

internal sealed record WorklogSessionTab(
    int Index,
    string SessionId,
    string? ProjectName,
    string ActorName,
    WorklogSessionTabState State,
    bool IsShown);

/// <summary>
/// Remembers observed worklog sessions until their terminal or silent lifetime expires.
/// The window may show only one session; observations of other sessions remain available.
/// </summary>
internal sealed class WorklogSessionTabs
{
    internal static readonly TimeSpan SucceededLifetime = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan FailedLifetime = TimeSpan.FromMinutes(8);
    internal static readonly TimeSpan CancelledLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan SilentLifetime = TimeSpan.FromMinutes(8);

    private sealed record Observation(
        int Index,
        DesktopProgressMessage Message,
        DateTimeOffset ObservedUtc,
        DateTimeOffset? CodeChangedUtc);

    private readonly object _gate = new();
    private readonly Dictionary<string, Observation> _sessions = new(StringComparer.Ordinal);

    internal void Observe(
        DesktopProgressMessage message,
        DateTimeOffset observedUtc,
        DateTimeOffset? codeChangedUtc)
    {
        if (message.Lane != DesktopProgressLane.Worklog ||
            message.WorklogFrame is not { } frame)
        {
            return;
        }

        lock (_gate)
        {
            if (_sessions.TryGetValue(frame.SessionId, out var previous))
            {
                if (frame.Sequence < previous.Message.WorklogFrame!.Sequence)
                {
                    return;
                }

                _sessions[frame.SessionId] = previous with
                {
                    Message = message,
                    ObservedUtc = observedUtc,
                    CodeChangedUtc = codeChangedUtc,
                };
                return;
            }

            var index = 0;
            var used = _sessions.Values.Select(static session => session.Index).ToHashSet();
            while (used.Contains(index))
            {
                index++;
            }

            _sessions.Add(frame.SessionId, new Observation(
                index, message, observedUtc, codeChangedUtc));
        }
    }

    internal IReadOnlyList<WorklogSessionTab> Snapshot(
        DateTimeOffset nowUtc,
        string? shownSessionId)
    {
        lock (_gate)
        {
            foreach (var (sessionId, observation) in _sessions.ToArray())
            {
                if (string.Equals(sessionId, shownSessionId, StringComparison.Ordinal))
                {
                    continue;
                }

                var frame = observation.Message.WorklogFrame!;
                var age = nowUtc - observation.ObservedUtc;
                var state = GetState(frame, age);
                var lifetime = state switch
                {
                    WorklogSessionTabState.Succeeded => SucceededLifetime,
                    WorklogSessionTabState.Failed => FailedLifetime,
                    WorklogSessionTabState.Cancelled => CancelledLifetime,
                    WorklogSessionTabState.Silent => SilentLifetime,
                    _ => (TimeSpan?)null,
                };

                if (lifetime is { } expiry && age > expiry)
                {
                    _sessions.Remove(sessionId);
                }
            }

            return _sessions
                .Select(static item => (
                    Id: item.Key,
                    Observation: item.Value))
                .OrderBy(static item => item.Observation.Index)
                .Select(item =>
                {
                    var frame = item.Observation.Message.WorklogFrame!;
                    return new WorklogSessionTab(
                        item.Observation.Index,
                        item.Id,
                        frame.Header.ProjectName,
                        frame.Header.ActorName,
                        GetState(frame, nowUtc - item.Observation.ObservedUtc),
                        string.Equals(item.Id, shownSessionId, StringComparison.Ordinal));
                })
                .ToArray();
        }
    }

    internal bool TryGetNewest(
        string sessionId,
        out DesktopProgressMessage message,
        out DateTimeOffset? codeChangedUtc) =>
        TryGetNewest(sessionId, out message, out codeChangedUtc, out _);

    /// <summary>
    /// The newest message of a session the registry still holds, with the code-change time and the time the tray
    /// observed it (D109v: a card switched to it counts the session's lifetime from then).
    /// </summary>
    internal bool TryGetNewest(
        string sessionId,
        out DesktopProgressMessage message,
        out DateTimeOffset? codeChangedUtc,
        out DateTimeOffset observedUtc)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var observation))
            {
                message = observation.Message;
                codeChangedUtc = observation.CodeChangedUtc;
                observedUtc = observation.ObservedUtc;
                return true;
            }

            message = null!;
            codeChangedUtc = null;
            observedUtc = default;
            return false;
        }
    }

    private static WorklogSessionTabState GetState(
        DesktopWorklogFrame frame,
        TimeSpan sinceObserved)
    {
        return frame.Current.State switch
        {
            WorklogStepState.Succeeded => WorklogSessionTabState.Succeeded,
            WorklogStepState.Failed => WorklogSessionTabState.Failed,
            WorklogStepState.Cancelled => WorklogSessionTabState.Cancelled,
            _ when sinceObserved > TimeSpan.FromSeconds(
                Math.Max(10, frame.ExpectedNextUpdateSeconds) * 2L) =>
                WorklogSessionTabState.Silent,
            _ => WorklogSessionTabState.Running,
        };
    }
}
