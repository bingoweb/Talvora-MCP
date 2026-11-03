using System.Windows.Automation;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>SN3: the switchboard draws the real registry, measured counters and one light per new call.</summary>
    private static void AssertSn3Switchboard(Views.OverviewView overview)
    {
        var sockets = overview.Model.Sockets;
        var ids = string.Join(",", sockets.Select(socket => socket.Id));
        if (ids != "talvora,talvora-dev,talvora-admin,gitea,penpot")
            throw new InvalidOperationException("SN3 switchboard sockets are not in exchange order: " + ids);
        var lines = string.Join(",", sockets.Select(socket => socket.Line ?? "-"));
        if (lines != "core,dev,admin,-,-" || sockets[4].OutsideTitle is not null || sockets[3].OutsideTitle != "CHATGPT")
            throw new InvalidOperationException("SN3 lines must follow the real surfaces; Gitea is not measured, Penpot has no outside line.");
        if (sockets[2].State != ComponentVisualState.Attention || sockets[4].State != ComponentVisualState.Neutral)
            throw new InvalidOperationException("SN3 socket states must come from the health snapshot.");

        // First read only sets the counters; lights start with calls that finish after it.
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        var board = overview.SwitchboardControl;
        board.Settle();
        overview.ApplyTraffic(Feed(started, next: 10, devCalls: 10, adminCalls: 0, recent: []));
        if (board.ActivePulseCount != 0)
            throw new InvalidOperationException("SN3 must not replay old calls as lights on the first read.");
        var now = DateTimeOffset.UtcNow;
        overview.ApplyTraffic(Feed(started, next: 14, devCalls: 13, adminCalls: 1, recent:
        [
            new(11, "dev", "ChatGPT", now, true), new(12, "dev", "ChatGPT", now, true),
            new(13, "dev", "ChatGPT", now, false), new(14, "admin", "ChatGPT", now, true),
        ]));
        if (Switchboard.MotionAllowed && board.ActivePulseCount != 4)
            throw new InvalidOperationException($"SN3 must light exactly one light per new call, got {board.ActivePulseCount}.");
        var dev = overview.Model.Sockets.Single(socket => socket.Id == "talvora-dev");
        if (dev.Calls != 13 || dev.LastCallAtUtc is null)
            throw new InvalidOperationException("SN3 counters must show the measured totals.");
        var core = overview.Model.Sockets.Single(socket => socket.Id == "talvora");
        if (core.OutsideSub != "Codex")
            throw new InvalidOperationException("SN3 local line must name the clients really seen in the last hour, got " + core.OutsideSub);
        var names = board.Children.OfType<SwitchboardHotspot>().Select(AutomationProperties.GetName).ToArray();
        if (names.Length != 5 || !names.Any(name => name.StartsWith("Admin, dikkat", StringComparison.Ordinal)) ||
            !names.Any(name => name.Contains("13 çağrı", StringComparison.Ordinal)))
            throw new InvalidOperationException("SN3 sockets must be reachable and named for screen readers: " + string.Join(" | ", names));
        board.Settle();
    }

    /// <summary>SN4: an unreachable line drops its plug and shows a reconnect timer measured from the change.</summary>
    private static void AssertSn4DroppedLine(ShellWindow window, Views.OverviewView overview,
        ControlCenterDashboardSnapshot snapshot, string variant)
    {
        var dropped = snapshot with
        {
            Mcps = snapshot.Mcps.Select(state => state.Registration.Id == "talvora-admin"
                ? state with { Health = ControlCenterHealthState.Offline, StatusText = "Ulaşılamıyor" } : state).ToArray(),
        };
        var before = DateTimeOffset.UtcNow;
        overview.ApplySnapshot(dropped, DateTimeOffset.Now, []);
        var admin = overview.Model.Sockets.Single(socket => socket.Id == "talvora-admin");
        if (admin.State != ComponentVisualState.Offline || admin.StateSinceUtc < before.AddSeconds(-1))
            throw new InvalidOperationException("SN4 reconnect timer must start at the observed change.");
        overview.SwitchboardControl.Settle();
        window.UpdateLayout();
        SaveVisualSnapshotIfRequested(window, "sn4-dropped-" + variant);
        overview.ApplySnapshot(snapshot, DateTimeOffset.Now, []);
        overview.SwitchboardControl.Settle();
        window.UpdateLayout();
    }

    private static TrafficFeed Feed(DateTimeOffset started, long next, long devCalls, long adminCalls,
        IReadOnlyList<TrafficFeedCall> recent)
    {
        var last = recent.Count > 0 ? recent[^1].AtUtc : started;
        return new TrafficFeed(started, next,
            new Dictionary<string, TrafficFeedLine>(StringComparer.OrdinalIgnoreCase)
            {
                ["core"] = new("core", 2, 0, started.AddMinutes(1)),
                ["dev"] = new("dev", devCalls, 1, last),
                ["admin"] = new("admin", adminCalls, 0, adminCalls > 0 ? last : null),
            },
            recent,
            [new("core", "Codex", DateTimeOffset.UtcNow.AddMinutes(-2)), new("core", "Claude", DateTimeOffset.UtcNow.AddHours(-3))]);
    }
}
