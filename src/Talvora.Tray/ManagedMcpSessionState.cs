using System.Collections.Concurrent;

namespace Talvora.Tray;

internal static class ManagedMcpSessionState
{
    private static readonly ConcurrentDictionary<string, byte> ManualStops =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsManuallyStopped(string mcpId) =>
        ManualStops.ContainsKey(mcpId);

    public static void MarkManuallyStopped(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        ManualStops[mcpId] = 0;
    }

    public static void ClearManualStop(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        _ = ManualStops.TryRemove(mcpId, out _);
    }
}
