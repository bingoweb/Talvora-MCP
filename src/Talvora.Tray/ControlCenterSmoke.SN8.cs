using System.Windows.Automation;
using Talvora.Tray.Controls;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>SN8: measured failures show beside the call counter and a failed call still lights its cable.</summary>
    private static void AssertSn8Failures(ShellWindow window, Views.OverviewView overview, string variant)
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        var board = overview.SwitchboardControl;
        board.Settle();
        overview.ApplyTraffic(Feed(started, next: 80, devCalls: 80, adminCalls: 0, recent: []));
        var now = DateTimeOffset.UtcNow;
        var feed = Feed(started, next: 83, devCalls: 83, adminCalls: 0, recent:
        [
            new(81, "dev", "ChatGPT", now, true), new(82, "dev", "ChatGPT", now, false), new(83, "dev", "ChatGPT", now, true),
        ]);
        var withFailures = feed with
        {
            Lines = new Dictionary<string, TrafficFeedLine>(feed.Lines, StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = new("dev", 83, 7, now),
            },
        };
        overview.ApplyTraffic(withFailures);
        var dev = overview.Model.Sockets.Single(socket => socket.Id == "talvora-dev");
        if (dev.Calls != 83 || dev.Failed != 7)
            throw new InvalidOperationException("SN8 must carry the measured failure count.");
        if (Switchboard.MotionAllowed && board.ActivePulseCount != 3)
            throw new InvalidOperationException($"SN8: a failed call must light its cable too, got {board.ActivePulseCount} lights.");
        var name = board.Children.OfType<SwitchboardHotspot>()
            .Select(AutomationProperties.GetName).Single(text => text.StartsWith("Dev,", StringComparison.Ordinal));
        if (!name.Contains("83 çağrı, 7 hata", StringComparison.Ordinal))
            throw new InvalidOperationException("SN8 screen-reader name must include failures: " + name);
        board.Settle();
        window.UpdateLayout();
        SaveVisualSnapshotIfRequested(window, "sn8-failures-" + variant);
        overview.ApplyTraffic(null);
        board.Settle();
    }
}
