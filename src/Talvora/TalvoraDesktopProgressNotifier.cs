using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier :
    IAsyncDisposable
{
    internal static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan PipeConnectTimeout =
        TimeSpan.FromMilliseconds(350);

    private const uint NoActiveConsoleSession = 0xFFFFFFFF;

    private readonly ILogger<TalvoraDesktopProgressNotifier> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Channel<DesktopProgressMessage> _deliveryQueue;
    private readonly Task _deliveryTask;

    public TalvoraDesktopProgressNotifier(
        ILogger<TalvoraDesktopProgressNotifier> logger)
    {
        _logger = logger;
        _deliveryQueue =
            Channel.CreateBounded<DesktopProgressMessage>(
                new BoundedChannelOptions(64)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                });
        _deliveryTask =
            Task.Run(
                () => DeliverQueuedNotificationsAsync(
                    _lifetimeCts.Token));
    }

    public async ValueTask<T> RunToolCallAsync<T>(
        string toolName,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var displayName = NormalizeToolName(toolName);
        var operationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        Enqueue(
            operationId,
            displayName,
            "Talvora çalışıyor",
            $"{displayName} çağrısı başladı. İşlem sürerken önemli adımlar ve düzenli ilerleme durumu bu kartta güncellenecek.",
            DesktopProgressKind.Started,
            stopwatch.Elapsed);

        using var heartbeatCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask =
            RunHeartbeatAsync(
                operationId,
                displayName,
                stopwatch,
                heartbeatCts.Token);

        try
        {
            var result = await operation(cancellationToken);

            Enqueue(
                operationId,
                displayName,
                "Talvora tamamladı",
                $"{displayName} çağrısı başarıyla tamamlandı. Toplam süre: {FormatElapsed(stopwatch.Elapsed)}.",
                DesktopProgressKind.Completed,
                stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Enqueue(
                operationId,
                displayName,
                "Talvora durduruldu",
                $"{displayName} çağrısı iptal edildi. Geçen süre: {FormatElapsed(stopwatch.Elapsed)}.",
                DesktopProgressKind.Cancelled,
                stopwatch.Elapsed);
            throw;
        }
        catch (Exception)
        {
            Enqueue(
                operationId,
                displayName,
                "Talvora hata bildirdi",
                $"{displayName} çağrısı başarısız oldu. Ayrıntılar çağrı sonucunda ve Talvora günlüklerinde korunuyor.",
                DesktopProgressKind.Failed,
                stopwatch.Elapsed);
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
        string operationId,
        string displayName,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Enqueue(
                operationId,
                displayName,
                "Talvora çalışmaya devam ediyor",
                $"{displayName} hâlâ çalışıyor. Geçen süre: {FormatElapsed(stopwatch.Elapsed)}. İşlem devam ediyor; yeni bir dönüm noktası olduğunda bu kart güncellenecek.",
                DesktopProgressKind.Running,
                stopwatch.Elapsed);
        }
    }

    private void Enqueue(
        string operationId,
        string toolName,
        string title,
        string message,
        DesktopProgressKind kind,
        TimeSpan elapsed)
    {
        if (!OperatingSystem.IsWindows() ||
            IsTruthy(Environment.GetEnvironmentVariable("CI")))
        {
            return;
        }

        _deliveryQueue.Writer.TryWrite(
            new DesktopProgressMessage(
                operationId,
                toolName,
                title,
                message,
                kind,
                DateTimeOffset.UtcNow,
                elapsed.TotalSeconds));
    }

    private async Task DeliverQueuedNotificationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                var message in
                _deliveryQueue.Reader.ReadAllAsync(cancellationToken))
            {
                await TryDeliverAsync(
                    message,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryDeliverAsync(
        DesktopProgressMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == NoActiveConsoleSession)
            {
                return;
            }

            await using var pipe = new NamedPipeClientStream(
                ".",
                DesktopProgressProtocol.GetPipeName(
                    checked((int)sessionId)),
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            using var connectCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            connectCts.CancelAfter(PipeConnectTimeout);
            await pipe.ConnectAsync(connectCts.Token);

            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync(
                DesktopProgressProtocol.Serialize(message));
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Desktop progress tray IPC was unavailable within {TimeoutMs} ms.",
                PipeConnectTimeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Desktop progress notification failed without affecting the tool operation.");
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

    public async ValueTask DisposeAsync()
    {
        _deliveryQueue.Writer.TryComplete();
        _lifetimeCts.Cancel();
        try
        {
            await _deliveryTask;
        }
        catch (OperationCanceledException)
        {
        }

        _lifetimeCts.Dispose();
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();
}
