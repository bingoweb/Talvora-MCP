using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class TrayLog
{
    private static readonly object Sync = new();
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, DateTimeOffset> RecentMessages =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTimeOffset> RecentFailures =
        new(StringComparer.Ordinal);
    internal static string? PathOverrideForTests { get; set; }

    internal static string PathName =>
        PathOverrideForTests ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "Tray",
            "tray.log");

    public static void Write(string message, Exception? exception = null)
    {
        var now = DateTimeOffset.UtcNow;
        var safeMessage = FileLog.RedactSensitiveData(message);
        lock (Sync)
        {
            PruneExpired(RecentMessages, now);
            PruneExpired(RecentFailures, now);
            if (RecentMessages.TryGetValue(safeMessage, out var lastSeen) &&
                now - lastSeen < DuplicateWindow)
            {
                return;
            }
            RecentMessages[safeMessage] = now;

            var includeStack = false;
            if (exception is not null)
            {
                var detail = FileLog.RedactSensitiveData(exception.Message);
                var shortDetail = detail[..Math.Min(detail.Length, 256)];
                var mcpPosition = safeMessage.IndexOf("MCP=",
                    StringComparison.OrdinalIgnoreCase);
                var mcpId = string.Empty;
                if (mcpPosition >= 0)
                {
                    var start = mcpPosition + 4;
                    var end = safeMessage.IndexOfAny([' ', ';'], start);
                    mcpId = safeMessage[start..(end < 0 ? safeMessage.Length : end)];
                }
                var failureKey = mcpId + ":" + exception.GetType().FullName +
                    ":" + shortDetail;
                includeStack = !RecentFailures.TryGetValue(failureKey,
                    out var previousFailure) ||
                    now - previousFailure >= DuplicateWindow;
                if (includeStack)
                {
                    RecentFailures[failureKey] = now;
                }
            }

            FileLog.Write(
                PathName, message, exception,
                maxBytes: 3L * 1024 * 1024,
                includeExceptionDetails: includeStack);
        }
    }

    private static void PruneExpired(
        Dictionary<string, DateTimeOffset> entries,
        DateTimeOffset now)
    {
        foreach (var key in entries.Where(entry =>
                     now - entry.Value >= DuplicateWindow)
                 .Select(entry => entry.Key).ToArray())
        {
            entries.Remove(key);
        }
        if (entries.Count > 512)
        {
            foreach (var key in entries.OrderBy(entry => entry.Value)
                         .Take(entries.Count - 512)
                         .Select(entry => entry.Key).ToArray())
            {
                entries.Remove(key);
            }
        }
    }
}