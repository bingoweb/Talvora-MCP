using System.Windows.Automation;
using System.Windows.Input;
using Talvora.Tray.Controls;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>SN9: the 1 s traffic poll updates the socket hotspots in place, so keyboard focus and hover survive it.</summary>
    private static void AssertSn9HotspotsSurvivePolls(Views.OverviewView overview)
    {
        var board = overview.SwitchboardControl;
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        overview.ApplyTraffic(Feed(started, next: 20, devCalls: 20, adminCalls: 0, recent: []));
        var before = board.Children.OfType<SwitchboardHotspot>().ToArray();
        if (before.Length < 2)
            throw new InvalidOperationException("SN9: the switchboard has no socket hotspots to keep.");
        // A hidden smoke window may not get keyboard focus; when it does, the focus must survive the polls too.
        var focused = ReferenceEquals(Keyboard.Focus(before[1]), before[1]) && before[1].IsKeyboardFocused;
        for (var poll = 1; poll <= 5; poll++)
            overview.ApplyTraffic(Feed(started, next: 20 + poll, devCalls: 20 + poll, adminCalls: 0, recent: []));
        var after = board.Children.OfType<SwitchboardHotspot>().ToArray();
        if (after.Length != before.Length || after.Where((spot, index) => !ReferenceEquals(spot, before[index])).Any())
            throw new InvalidOperationException("SN9: a traffic poll must update the socket hotspots in place, not rebuild them.");
        if (focused && !before[1].IsKeyboardFocused)
            throw new InvalidOperationException("SN9: keyboard focus must stay on its socket across traffic polls.");
        var dev = after.Select(AutomationProperties.GetName).Single(name => name.StartsWith("Dev,", StringComparison.Ordinal));
        if (!dev.Contains("25 çağrı", StringComparison.Ordinal))
            throw new InvalidOperationException("SN9: the screen-reader name must follow the newest count: " + dev);
        TrayLog.Write($"SN9 hotspots kept across 5 polls (keyboard focus {(focused ? "kept" : "not granted to the hidden smoke window")}).");
        overview.ApplyTraffic(null);
        board.Settle();
    }
}
