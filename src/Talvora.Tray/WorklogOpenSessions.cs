using Talvora.Shared;

namespace Talvora.Tray;

/// <summary>
/// Keeps only the last terminal step of an open worklog session so a tray shutdown
/// or the next session can preserve work without writing heartbeats to disk.
/// </summary>
internal sealed class WorklogOpenSessions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DesktopProgressMessage> _open = new(StringComparer.Ordinal);

    public IReadOnlyList<DesktopProgressMessage> Observe(DesktopProgressMessage message)
    {
        if (message.WorklogFrame is not { } frame || message.Lane != DesktopProgressLane.Worklog)
        {
            return [];
        }

        lock (_gate)
        {
            IReadOnlyList<DesktopProgressMessage> displaced = [];
            if (_open.TryGetValue(frame.SessionId, out var previous) &&
                message.TimestampUtc.AddSeconds(-Math.Max(0, frame.Totals.SessionSeconds)) > previous.TimestampUtc)
            {
                displaced = [previous];
                _open.Remove(frame.SessionId);
            }

            if (message.SessionClosed)
            {
                _open.Remove(frame.SessionId);
            }
            else if (message.Kind is DesktopProgressKind.Completed or
                     DesktopProgressKind.Failed or DesktopProgressKind.Cancelled)
            {
                _open[frame.SessionId] = message;
            }

            return displaced;
        }
    }

    public IReadOnlyList<DesktopProgressMessage> Drain()
    {
        lock (_gate)
        {
            var remaining = _open.Values.ToArray();
            _open.Clear();
            return remaining;
        }
    }
}
