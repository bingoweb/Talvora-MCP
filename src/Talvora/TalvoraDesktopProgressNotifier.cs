using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier(
    ILogger<TalvoraDesktopProgressNotifier> logger)
{
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan MinimumDeliveryInterval = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CompletionNotificationThreshold = TimeSpan.FromSeconds(20);

    private const uint NoActiveConsoleSession = 0xFFFFFFFF;
    private const uint MessageBoxInformation = 0x00000040;
    private const uint MessageTimeoutSeconds = 8;

    private readonly object _gate = new();
    private DateTimeOffset _lastDeliveryUtc = DateTimeOffset.MinValue;
    private string? _lastDeliveredMessage;

    public async ValueTask<T> RunToolCallAsync<T>(
        string toolName,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var displayName = NormalizeToolName(toolName);
        var stopwatch = Stopwatch.StartNew();
        TryNotify(
            "Talvora çalışıyor",
            $"Başladı: {displayName}",
            force: false);

        using var heartbeatCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask =
            RunHeartbeatAsync(
                displayName,
                stopwatch,
                heartbeatCts.Token);

        try
        {
            var result = await operation(cancellationToken);

            if (stopwatch.Elapsed >= CompletionNotificationThreshold)
            {
                TryNotify(
                    "Talvora tamamladı",
                    $"Tamamlandı: {displayName} ({FormatElapsed(stopwatch.Elapsed)})",
                    force: true);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryNotify(
                "Talvora durduruldu",
                $"İptal edildi: {displayName}",
                force: true);
            throw;
        }
        catch (Exception)
        {
            TryNotify(
                "Talvora hata bildirdi",
                $"Başarısız: {displayName}",
                force: true);
            throw;
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the tool call completes before the next heartbeat.
            }
        }
    }

    private async Task RunHeartbeatAsync(
        string displayName,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            TryNotify(
                "Talvora çalışmaya devam ediyor",
                $"{displayName} sürüyor ({FormatElapsed(stopwatch.Elapsed)})",
                force: true);
        }
    }

    private bool TryNotify(
        string title,
        string message,
        bool force)
    {
        try
        {
            if (!OperatingSystem.IsWindows() ||
                IsTruthy(Environment.GetEnvironmentVariable("CI")))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                if (!force &&
                    now - _lastDeliveryUtc < MinimumDeliveryInterval)
                {
                    return false;
                }

                if (string.Equals(
                        _lastDeliveredMessage,
                        message,
                        StringComparison.Ordinal) &&
                    now - _lastDeliveryUtc < DuplicateWindow)
                {
                    return false;
                }

                var sessionId = WTSGetActiveConsoleSessionId();
                if (sessionId == NoActiveConsoleSession)
                {
                    return false;
                }

                var response = 0u;
                var delivered = WTSSendMessageW(
                    IntPtr.Zero,
                    sessionId,
                    title,
                    checked((uint)(title.Length * sizeof(char))),
                    message,
                    checked((uint)(message.Length * sizeof(char))),
                    MessageBoxInformation,
                    MessageTimeoutSeconds,
                    out response,
                    false);

                if (!delivered)
                {
                    logger.LogDebug(
                        "Desktop progress notification was not delivered. Win32Error={Win32Error} SessionId={SessionId}",
                        Marshal.GetLastWin32Error(),
                        sessionId);
                    return false;
                }

                _lastDeliveryUtc = now;
                _lastDeliveredMessage = message;
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Desktop progress notification failed without affecting the tool operation.");
            return false;
        }
    }

    private static string NormalizeToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return "MCP çalışması";
        }

        const int maxLength = 120;
        var normalized = toolName.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes} dk {elapsed.Seconds} sn";
        }

        return $"{Math.Max(1, (int)elapsed.TotalSeconds)} sn";
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport(
        "wtsapi32.dll",
        EntryPoint = "WTSSendMessageW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSSendMessageW(
        IntPtr hServer,
        uint sessionId,
        string title,
        uint titleLength,
        string message,
        uint messageLength,
        uint style,
        uint timeout,
        out uint response,
        [MarshalAs(UnmanagedType.Bool)] bool wait);
}
