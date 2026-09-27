using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier :
    IAsyncDisposable
{
    internal static readonly TimeSpan FirstProgressDelay =
        TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ProgressInterval =
        TimeSpan.FromSeconds(60);
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
        var displayName = narrative.Subject;
        var operationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        Enqueue(
            operationId,
            displayName,
            "Şimdi bunu yapıyorum",
            $"{narrative.Action}\n\nNeden: {narrative.Reason}",
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
                "Bitti",
                $"Bu işi tamamladım. {narrative.Action}\n\nŞimdi sıradaki adıma geçiyorum.",
                DesktopProgressKind.Completed,
                stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Enqueue(
                operationId,
                displayName,
                "Bu işi durdurdum",
                $"Şu işi tamamlayamadım: {narrative.Action}",
                DesktopProgressKind.Cancelled,
                stopwatch.Elapsed);
            throw;
        }
        catch (Exception)
        {
            Enqueue(
                operationId,
                displayName,
                "Burada bir sorun çıktı",
                $"Şunu yapmaya çalışıyordum: {narrative.Action}\n\nSorunu kontrol edip düzelteceğim.",
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
        await Task.Delay(FirstProgressDelay, cancellationToken);

        Enqueue(
            operationId,
            displayName,
            "Hâlâ bununla uğraşıyorum",
            $"{displayName}.\n\nYaklaşık {FormatElapsed(stopwatch.Elapsed)} oldu. Bitince sonucu burada göstereceğim.",
            DesktopProgressKind.Running,
            stopwatch.Elapsed);

        using var timer = new PeriodicTimer(ProgressInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Enqueue(
                operationId,
                displayName,
                "Hâlâ bununla uğraşıyorum",
                $"{displayName}.\n\nYaklaşık {FormatElapsed(stopwatch.Elapsed)} oldu. Bitince sonucu burada göstereceğim.",
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

    private static string GetFriendlyToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return "MCP çalışması";
        }

        var normalized = NormalizeToolName(toolName);

        return normalized switch
        {
            "read_source" or
            "read_text_range" or
            "read_bytes" or
            "tail_text" or
            "search_text" or
            "find_files" or
            "list" => "Bilgileri kontrol ediyorum",

            "apply_patch" or
            "apply_edits" or
            "structural_edit" or
            "semantic_edit" => "İstediğin değişikliği uyguluyorum",

            "dotnet_build" => "Yaptığım değişikliği kontrol ediyorum",
            "dotnet_test" => "Yaptığım değişikliği deniyorum",
            "dotnet_restore" => "Gerekli hazırlıkları tamamlıyorum",

            "git_diff" => "Yaptığım değişiklikleri gözden geçiriyorum",
            "git_run" => "Yaptığım değişiklikleri toparlıyorum",
            "git_branches" => "Çalışmanın son durumunu kontrol ediyorum",

            "run_powershell" => "Bilgisayarında gerekli işlemi yapıyorum",
            "http_request" => "Programın çalışıp çalışmadığını kontrol ediyorum",
            "process_list" or "process_get" => "Arka planda çalışanları kontrol ediyorum",
            "system_info" => "Bilgisayarındaki durumu kontrol ediyorum",
            _ => HumanizeToolName(normalized),
        };
    }

    private static bool ShouldNotifyToolCall(string? toolName)
    {
        var normalized = NormalizeToolName(toolName);
        return normalized switch
        {
            "read_source" or
            "read_text_range" or
            "read_bytes" or
            "tail_text" or
            "search_text" or
            "find_files" or
            "list" or
            "path_info" or
            "file_hash" or
            "process_list" or
            "process_get" or
            "system_info" or
            "http_request" or
            "tcp_listeners" or
            "git_diff" or
            "git_branches" => false,
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

    private static string HumanizeToolName(string toolName)
    {
        const int maxLength = 96;
        var friendly = toolName.Replace('_', ' ').Trim();
        if (friendly.Length > maxLength)
        {
            friendly = friendly[..maxLength];
        }

        return string.IsNullOrWhiteSpace(friendly)
            ? "Talvora çalışması"
            : $"Talvora işlemi: {friendly}";
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
