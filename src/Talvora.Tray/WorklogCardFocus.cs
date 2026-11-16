using Talvora.Shared;

namespace Talvora.Tray;

/// <summary>Decides whether a worklog frame may replace the session shown in the one visible card.</summary>
internal static class WorklogCardFocus
{
    internal static bool ShouldPresent(string? visibleSessionId, DesktopProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var sessionId = message.WorklogFrame?.SessionId ?? message.OperationId;
        if (visibleSessionId is null ||
            string.Equals(visibleSessionId, sessionId, StringComparison.Ordinal))
            return true;

        return !message.SessionClosed && message.Kind is (
            DesktopProgressKind.Started or DesktopProgressKind.Completed or
            DesktopProgressKind.Failed or DesktopProgressKind.Cancelled);
    }
}
