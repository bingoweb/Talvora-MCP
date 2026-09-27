using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
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

            if (TryDescribeFailure(result, toolName, out var failureMessage))
            {
                Enqueue(
                    operationId,
                    displayName,
                    "Bir hata buldum",
                    failureMessage,
                    DesktopProgressKind.Failed,
                    stopwatch.Elapsed);
            }
            else
            {
                Enqueue(
                    operationId,
                    displayName,
                    "Bitti",
                    $"{BuildPlainCompletion(narrative)}\n\nŞimdi sıradaki adıma geçiyorum.",
                    DesktopProgressKind.Completed,
                    stopwatch.Elapsed);
            }

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
                BuildFriendlyExceptionMessage(toolName, narrative.Action),
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
            _ => "Sıradaki işi yapıyorum",
        };
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
        catch (JsonException)
        {
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
                "Bu iş beklediğimden uzun sürdü ve tamamlanamadı. " +
                "Takıldığı yeri kontrol edip daha güvenli bir şekilde yeniden deneyeceğim.";
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
                "Kontrol sırasında bir hata buldum. Yaptığım değişiklik henüz hazır değil. " +
                "Şimdi hatanın nedenini bulup düzelteceğim ve yeniden kontrol edeceğim.",
            "dotnet_test" =>
                "Deneme sırasında bir hata buldum. Yaptığım değişiklik beklediğim gibi çalışmadı. " +
                "Şimdi hatayı düzelteceğim ve aynı denemeyi yeniden yapacağım.",
            "apply_patch" or "apply_edits" or "structural_edit" or "semantic_edit" =>
                "Yapmak istediğim değişiklik bu denemede uygulanmadı. Mevcut dosyaları korudum. " +
                "Şimdi neden uygulanmadığını kontrol edip güvenli biçimde yeniden deneyeceğim.",
            "run_powershell" =>
                "Bilgisayarında yaptığım bu adım tamamlanmadı. " +
                "Şimdi hangi noktada kaldığını kontrol edip düzeltmeye devam edeceğim.",
            "git_run" =>
                "Yaptığım çalışmayı güvene alma adımı tamamlanmadı. Çalışmanın kendisi kaybolmadı. " +
                "Sorunu kontrol edip yeniden deneyeceğim.",
            _ =>
                "Bu adım beklediğim gibi tamamlanmadı. " +
                "Nedenini kontrol edip düzelttikten sonra yeniden deneyeceğim.",
        };
    }

    private static string BuildFriendlyExceptionMessage(
        string? toolName,
        string action) =>
        $"{action}\n\n{BuildFriendlyResultFailureMessage(toolName)}";

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
