using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier :
    IAsyncDisposable
{
    internal static readonly TimeSpan FirstProgressDelay =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ProgressInterval =
        TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan PipeConnectTimeout =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan PipeWriteTimeout =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ShutdownDrainTimeout =
        TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan ForcedShutdownTimeout =
        TimeSpan.FromSeconds(2);

    private const int ProgressQueueCapacity = 1;
    private const int TerminalQueueCapacity = 1_024;
    private const int MaximumRetiredProgressOperations = 512;

    private const uint NoActiveConsoleSession = 0xFFFFFFFF;

    private readonly ILogger<TalvoraDesktopProgressNotifier> _logger;
    private readonly CancellationTokenSource _deliveryStopCts = new();
    private readonly Channel<DesktopProgressMessage> _progressDeliveryQueue;
    private readonly Channel<DesktopProgressMessage> _terminalDeliveryQueue;
    private readonly SemaphoreSlim _deliverySignal = new(0, 1);
    private readonly Action<DesktopProgressMessage>? _deliveryObserver;
    private readonly Func<DesktopProgressMessage, CancellationToken, Task>? _deliveryOverride;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly TimeSpan _forcedShutdownTimeout;
    private readonly Task _deliveryTask;
    private long _droppedProgressMessages;
    private long _droppedTerminalMessages;
    private long _pendingTerminalMessages;
    private long _terminalBacklogHighWaterMark;
    private long _deliverySequence;
    private int _acceptingNotifications = 1;
    private readonly object _retiredProgressGate = new();
    private readonly HashSet<string> _retiredProgressOperations =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _retiredProgressOrder = new();

    internal long DroppedProgressMessages =>
        Interlocked.Read(ref _droppedProgressMessages);

    internal long DroppedTerminalMessages =>
        Interlocked.Read(ref _droppedTerminalMessages);

    internal long PendingTerminalMessages =>
        Interlocked.Read(ref _pendingTerminalMessages);

    public TalvoraDesktopProgressNotifier(
        ILogger<TalvoraDesktopProgressNotifier> logger)
        : this(
            logger,
            deliveryObserver: null,
            deliveryOverride: null,
            shutdownDrainTimeout: ShutdownDrainTimeout,
            forcedShutdownTimeout: ForcedShutdownTimeout)
    {
    }

    internal TalvoraDesktopProgressNotifier(
        ILogger<TalvoraDesktopProgressNotifier> logger,
        Action<DesktopProgressMessage>? deliveryObserver,
        Func<DesktopProgressMessage, CancellationToken, Task>? deliveryOverride,
        TimeSpan? shutdownDrainTimeout = null,
        TimeSpan? forcedShutdownTimeout = null)
    {
        _logger = logger;
        _deliveryObserver = deliveryObserver;
        _deliveryOverride = deliveryOverride;
        _shutdownDrainTimeout =
            shutdownDrainTimeout ?? ShutdownDrainTimeout;
        _forcedShutdownTimeout =
            forcedShutdownTimeout ?? ForcedShutdownTimeout;
        _progressDeliveryQueue =
            Channel.CreateBounded<DesktopProgressMessage>(
                new BoundedChannelOptions(ProgressQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                },
                OnProgressMessageCoalesced);
        _terminalDeliveryQueue =
            Channel.CreateBounded<DesktopProgressMessage>(
                new BoundedChannelOptions(TerminalQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });
        _deliveryTask =
            Task.Run(
                () => DeliverQueuedNotificationsAsync(
                    _deliveryStopCts.Token));
    }

    public async ValueTask<T> RunToolCallAsync<T>(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!ShouldNotifyToolCall(toolName))
        {
            return await operation(cancellationToken);
        }

        var narrative = BuildNarrative(toolName, arguments);
        var canonicalToolName = NormalizeToolName(toolName);
        var initialEvidence =
            BuildInitialEvidence(toolName, arguments);
        var displayName = narrative.Subject;
        var operationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        TryBeginWorklog(
            operationId,
            canonicalToolName,
            narrative,
            initialEvidence,
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
            var terminalEvidence =
                BuildTerminalEvidence(
                    initialEvidence,
                    result);

            try
            {
                if (TryDescribeFailure(
                        result,
                        toolName,
                        out var failureMessage))
                {
                    TryEndWorklog(
                        operationId,
                        DesktopProgressKind.Failed,
                        "Bir hata buldum",
                        failureMessage,
                        terminalEvidence,
                        stopwatch.Elapsed);
                }
                else
                {
                    if (TryBuildDeferredCompletion(
                            narrative,
                            result,
                            out var deferredTitle,
                            out var deferredMessage))
                    {
                        TryEndWorklog(
                            operationId,
                            DesktopProgressKind.Completed,
                            deferredTitle,
                            deferredMessage,
                            terminalEvidence,
                            stopwatch.Elapsed);
                    }
                    else
                    {
                        TryEndWorklog(
                            operationId,
                            DesktopProgressKind.Completed,
                            narrative.Subject,
                            BuildPlainCompletion(narrative),
                            terminalEvidence,
                            stopwatch.Elapsed);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Desktop worklog result classification failed without affecting the tool result.");
                TryEndWorklog(
                    operationId,
                    DesktopProgressKind.Completed,
                    narrative.Subject,
                    BuildPlainCompletion(narrative),
                    initialEvidence,
                    stopwatch.Elapsed);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryEndWorklog(
                operationId,
                DesktopProgressKind.Cancelled,
                narrative.Subject,
                "İşlem tamamlanmadan durduruldu. Son doğrulanmış durum aşağıdaki gerçek kanıtta korunuyor.",
                initialEvidence,
                stopwatch.Elapsed);
            throw;
        }
        catch (Exception)
        {
            TryEndWorklog(
                operationId,
                DesktopProgressKind.Failed,
                narrative.Subject,
                BuildFriendlyExceptionMessage(toolName, narrative.Action),
                initialEvidence,
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
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Desktop worklog heartbeat failed without affecting the tool result.");
            }
        }
    }

    private async Task RunHeartbeatAsync(
        string operationId,
        string displayName,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await Task.Delay(FirstProgressDelay, cancellationToken);

        TryUpdateWorklogProgress(
            operationId,
            displayName,
            stopwatch.Elapsed);

        using var timer = new PeriodicTimer(ProgressInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            TryUpdateWorklogProgress(
                operationId,
                displayName,
                stopwatch.Elapsed);
        }
    }

    private void Enqueue(
        string operationId,
        string toolName,
        string title,
        string message,
        DesktopProgressKind kind,
        TimeSpan elapsed,
        DesktopProgressEvidence? evidence = null)
    {
        if (Volatile.Read(ref _acceptingNotifications) == 0)
        {
            return;
        }

        var notification =
            new DesktopProgressMessage(
                operationId,
                toolName,
                title,
                message,
                kind,
                DateTimeOffset.UtcNow,
                elapsed.TotalSeconds,
                evidence,
                DesktopProgressLane.Worklog,
                Interlocked.Increment(ref _deliverySequence));

        _deliveryObserver?.Invoke(notification);

        if (_deliveryOverride is null &&
            (!OperatingSystem.IsWindows() ||
             IsTruthy(Environment.GetEnvironmentVariable("CI"))))
        {
            return;
        }

        var terminal = IsTerminalDelivery(kind);
        var writer = terminal
            ? _terminalDeliveryQueue.Writer
            : _progressDeliveryQueue.Writer;
        if (terminal)
        {
            RetireProgressOperation(operationId);
        }

        if (writer.TryWrite(notification))
        {
            if (terminal)
            {
                TrackTerminalQueued();
            }

            SignalDeliveryWorker();
            return;
        }

        if (terminal)
        {
            var dropped =
                Interlocked.Increment(
                    ref _droppedTerminalMessages);
            _logger.LogError(
                "Desktop progress terminal delivery could not enter the bounded outcome queue. DroppedTerminal={DroppedTerminal}; Capacity={Capacity}; Operation={OperationId}; Kind={Kind}",
                dropped,
                TerminalQueueCapacity,
                operationId,
                kind);
        }
    }

    private void OnProgressMessageCoalesced(
        DesktopProgressMessage message)
    {
        var coalesced =
            Interlocked.Increment(
                ref _droppedProgressMessages);
        if (coalesced == 1 ||
            (coalesced & (coalesced - 1)) == 0)
        {
            _logger.LogDebug(
                "Desktop progress latest-wins coalescing replaced a stale update. CoalescedProgressCount={CoalescedCount}; Operation={OperationId}",
                coalesced,
                message.OperationId);
        }
    }

    private void TrackTerminalQueued()
    {
        var pending =
            Interlocked.Increment(
                ref _pendingTerminalMessages);
        var previousHighWater =
            Interlocked.Read(
                ref _terminalBacklogHighWaterMark);
        while (pending > previousHighWater)
        {
            var observed = Interlocked.CompareExchange(
                ref _terminalBacklogHighWaterMark,
                pending,
                previousHighWater);
            if (observed == previousHighWater)
            {
                if (pending >= 64 &&
                    (pending & (pending - 1)) == 0)
                {
                    _logger.LogWarning(
                        "Desktop progress terminal backlog reached {PendingCount} messages. Outcomes are retained; delivery remains timeout-bounded.",
                        pending);
                }

                break;
            }

            previousHighWater = observed;
        }
    }

    private void RetireProgressOperation(string operationId)
    {
        lock (_retiredProgressGate)
        {
            if (!_retiredProgressOperations.Add(operationId))
            {
                return;
            }

            _retiredProgressOrder.Enqueue(operationId);
            while (_retiredProgressOrder.Count >
                   MaximumRetiredProgressOperations)
            {
                _retiredProgressOperations.Remove(
                    _retiredProgressOrder.Dequeue());
            }
        }
    }

    private bool IsProgressRetired(string operationId)
    {
        lock (_retiredProgressGate)
        {
            return _retiredProgressOperations.Contains(operationId);
        }
    }

    private void SignalDeliveryWorker()
    {
        try
        {
            _deliverySignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref _acceptingNotifications) == 0)
        {
        }
    }

    private async Task DeliverQueuedNotificationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_terminalDeliveryQueue.Reader.TryRead(
                           out var terminalMessage))
                {
                    Interlocked.Decrement(
                        ref _pendingTerminalMessages);
                    await TryDeliverAsync(
                        terminalMessage,
                        cancellationToken);
                }

                if (_progressDeliveryQueue.Reader.TryRead(
                        out var progressMessage))
                {
                    if (!IsProgressRetired(
                            progressMessage.OperationId))
                    {
                        await TryDeliverAsync(
                            progressMessage,
                            cancellationToken);
                    }
                    continue;
                }

                var terminalCompleted =
                    _terminalDeliveryQueue.Reader.Completion.IsCompleted;
                var progressCompleted =
                    _progressDeliveryQueue.Reader.Completion.IsCompleted;
                if (terminalCompleted && progressCompleted)
                {
                    break;
                }

                await _deliverySignal.WaitAsync(
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
        if (_deliveryOverride is not null)
        {
            await _deliveryOverride(
                    message,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveConsoleSession)
        {
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
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

                using var writeCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                writeCts.CancelAfter(PipeWriteTimeout);
                await DesktopProgressProtocol.WriteFrameAsync(
                    pipe,
                    message,
                    writeCts.Token);
                return;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < 3)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(120 * attempt),
                    cancellationToken);
            }
        }

        if (lastError is null)
        {
            _logger.LogDebug(
                "Desktop progress tray IPC stayed unavailable after retrying.");
        }
        else
        {
            _logger.LogDebug(
                lastError,
                "Desktop progress notification failed after retrying without affecting the tool operation.");
        }
    }

    private static bool TryDescribeFailure<T>(
        T result,
        string? toolName,
        out string message)
    {
        if (result is CallToolResult callToolResult)
        {
            if (callToolResult.IsError is true)
            {
                message = BuildFriendlyResultFailureMessage(toolName);
                return true;
            }

            if (callToolResult.StructuredContent is JsonElement structured &&
                TryDescribeStructuredFailure(
                    structured,
                    toolName,
                    out message))
            {
                return true;
            }
        }

        try
        {
            var json = JsonSerializer.SerializeToElement(result);
            if (TryDescribeStructuredFailure(
                    json,
                    toolName,
                    out message))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Desktop worklog failure inspection skipped: {ex.GetType().Name}");
        }

        message = string.Empty;
        return false;
    }

    private static bool TryDescribeStructuredFailure(
        JsonElement json,
        string? toolName,
        out string message)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            message = string.Empty;
            return false;
        }

        if (TryGetPropertyIgnoreCase(json, "timedOut", out var timedOut) &&
            timedOut.ValueKind == JsonValueKind.True)
        {
            message =
                "Bu işlem zaman sınırını aştı ve tamamlanmış sayılmıyor. " +
                "Son doğrulanmış durum ve varsa gerçek hata ayrıntısı kanıt bölümünde korunuyor.";
            return true;
        }

        if (TryGetPropertyIgnoreCase(json, "success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            message = BuildFriendlyResultFailureMessage(toolName);
            return true;
        }

        if (TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code) &&
            code != 0)
        {
            message = BuildFriendlyResultFailureMessage(toolName);
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement json,
        string name,
        out JsonElement value)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string BuildFriendlyResultFailureMessage(string? toolName)
    {
        var normalized = NormalizeToolName(toolName);
        return normalized switch
        {
            "dotnet_build" =>
                "Derleme başarılı olmadı; bu değişiklik hazır sayılmıyor. " +
                "Derleyicinin gerçek sonucu kanıt bölümünde yer alıyor.",
            "dotnet_test" =>
                "Davranış testi başarılı olmadı; beklenen sözleşme henüz doğrulanmadı. " +
                "Testin gerçek sonucu kanıt bölümünde yer alıyor.",
            "apply_patch" or "apply_edits" or "structural_edit" or "semantic_edit" =>
                "Kaynak değişikliği bu denemede uygulanmadı. " +
                "Hedef dosya/diff bilgisi mevcutsa kanıt bölümünde gösteriliyor; bu adım tamamlanmış sayılmıyor.",
            "run_powershell" =>
                "Yerel sistem adımı tamamlanmadı. " +
                "Gerçek exit/sonuç bilgisi mevcutsa kanıt bölümünde gösteriliyor.",
            "git_run" =>
                "Git işlemi tamamlanmadı. Repository durumu bu bildirimle başarılı kabul edilmiyor.",
            _ =>
                "Bu teknik adım tamamlanmadı. Yalnız doğrulanmış sonuçlar tamamlandı olarak gösterilir.",
        };
    }

    private static string BuildFriendlyExceptionMessage(
        string? toolName,
        string action) =>
        $"{action}\n\n{BuildFriendlyResultFailureMessage(toolName)}";

    private static bool ShouldNotifyToolCall(string? toolName)
    {
        var normalized = NormalizeToolName(toolName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized switch
        {
            "list" or
            "process_list" or
            "process_get" or
            "system_info" or
            "tcp_listeners" or
            "git_branches" or
            "git_status" or
            "git_info" or
            "job_get" or
            "job_list" or
            "service_get" or
            "service_list" or
            "scheduled_task_get" or
            "scheduled_task_list" or
            "registry_get" or
            "env_get" or
            "config_get" => false,
            _ when
                normalized.EndsWith(
                    "_status",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(
                    "_info",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(
                    "_list",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(
                    "_get",
                    StringComparison.OrdinalIgnoreCase) => false,
            _ => true,
        };
    }

    private static string NormalizeToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return string.Empty;
        }

        var normalized = toolName.Trim();
        if (normalized.StartsWith("talvora_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["talvora_".Length..];
        }

        return normalized;
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

    private static bool IsTerminalDelivery(
        DesktopProgressKind kind) =>
        kind is
            DesktopProgressKind.Completed or
            DesktopProgressKind.Failed or
            DesktopProgressKind.Cancelled or
            DesktopProgressKind.Info or
            DesktopProgressKind.Warning;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref _acceptingNotifications,
                0) == 0)
        {
            return;
        }

        var deliveryStopped = false;
        _progressDeliveryQueue.Writer.TryComplete();
        _terminalDeliveryQueue.Writer.TryComplete();
        SignalDeliveryWorker();
        try
        {
            await _deliveryTask.WaitAsync(
                _shutdownDrainTimeout);
            deliveryStopped = true;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Desktop progress delivery did not drain within {DrainTimeoutSeconds:F0}s. Remaining delivery is being cancelled. DroppedProgress={DroppedProgress}; DroppedTerminal={DroppedTerminal}",
                _shutdownDrainTimeout.TotalSeconds,
                Interlocked.Read(ref _droppedProgressMessages),
                Interlocked.Read(ref _droppedTerminalMessages));
            _deliveryStopCts.Cancel();
            try
            {
                await _deliveryTask.WaitAsync(
                    _forcedShutdownTimeout);
                deliveryStopped = true;
            }
            catch (OperationCanceledException)
            {
                deliveryStopped = true;
            }
            catch (TimeoutException)
            {
                _logger.LogError(
                    "Desktop progress delivery ignored forced shutdown for {ForcedShutdownSeconds:F0}s. Shutdown will continue without waiting for the notification worker.",
                    _forcedShutdownTimeout.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            deliveryStopped = true;
        }

        if (deliveryStopped)
        {
            _deliveryStopCts.Dispose();
            _deliverySignal.Dispose();
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();
}
