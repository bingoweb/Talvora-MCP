using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraHttpMockListenerInfo(
    string ListenerId,
    IReadOnlyList<string> Prefixes,
    bool AutoReply,
    int DefaultStatusCode,
    string DefaultContentType,
    int MaxQueuedRequests,
    int MaxRequestBodyBytes,
    int PendingResponseTimeoutSeconds,
    DateTime StartedAtUtc,
    int QueuedRequests,
    int PendingRequests,
    long DroppedRequests,
    bool IsListening);

public sealed record TalvoraHttpMockStartResponse(
    string ListenerId,
    IReadOnlyList<string> Prefixes,
    bool AutoReply,
    int DefaultStatusCode,
    string DefaultContentType,
    DateTime StartedAtUtc);

public sealed record TalvoraHttpMockListResponse(
    int Count,
    IReadOnlyList<TalvoraHttpMockListenerInfo> Listeners);

public sealed record TalvoraHttpMockRequest(
    long Sequence,
    string RequestId,
    DateTime ReceivedAtUtc,
    string Method,
    string Url,
    string RawUrl,
    string ProtocolVersion,
    IReadOnlyDictionary<string, string[]> Headers,
    IReadOnlyDictionary<string, string[]> Query,
    string? RemoteEndPoint,
    string? LocalEndPoint,
    string BodyMode,
    string? Body,
    long BodyBytes,
    bool BodyTruncated,
    bool PendingResponse);

public sealed record TalvoraHttpMockReadResponse(
    string ListenerId,
    int Count,
    int Remaining,
    int PendingRequests,
    long DroppedRequests,
    IReadOnlyList<TalvoraHttpMockRequest> Requests);

public sealed record TalvoraHttpMockReplyResponse(
    string ListenerId,
    string RequestId,
    bool Found,
    bool Replied,
    int? StatusCode);

public sealed record TalvoraHttpMockStopResponse(
    string ListenerId,
    bool Found,
    bool Stopped,
    int PendingRequestsClosed);

internal sealed record TalvoraHttpMockResponseDefinition(
    int StatusCode,
    string ContentType,
    Dictionary<string, string> Headers,
    byte[] Body);

internal sealed class TalvoraPendingHttpMockRequest
{
    public required HttpListenerContext Context { get; init; }
    public required TaskCompletionSource<bool> Completion { get; init; }
    public int Replied;
}

internal sealed class TalvoraHttpMockRuntime : IDisposable
{
    public required string ListenerId { get; init; }
    public required HttpListener Listener { get; init; }
    public required string[] Prefixes { get; init; }
    public required bool AutoReply { get; init; }
    public required TalvoraHttpMockResponseDefinition DefaultResponse { get; init; }
    public required int MaxQueuedRequests { get; init; }
    public required int MaxRequestBodyBytes { get; init; }
    public required string RequestBodyMode { get; init; }
    public required Encoding RequestEncoding { get; init; }
    public required int PendingResponseTimeoutSeconds { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required CancellationTokenSource Cancellation { get; init; }

    public ConcurrentQueue<TalvoraHttpMockRequest> Requests { get; } = new();
    public ConcurrentDictionary<string, TalvoraPendingHttpMockRequest> Pending { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public long Sequence;
    public int QueuedRequests;
    public long DroppedRequests;

    public TalvoraHttpMockListenerInfo ToInfo() =>
        new(
            ListenerId,
            Prefixes,
            AutoReply,
            DefaultResponse.StatusCode,
            DefaultResponse.ContentType,
            MaxQueuedRequests,
            MaxRequestBodyBytes,
            PendingResponseTimeoutSeconds,
            StartedAtUtc,
            Math.Max(0, Volatile.Read(ref QueuedRequests)),
            Pending.Count,
            Math.Max(0, Interlocked.Read(ref DroppedRequests)),
            Listener.IsListening);

    public void Enqueue(TalvoraHttpMockRequest request)
    {
        Requests.Enqueue(request);
        Interlocked.Increment(ref QueuedRequests);

        if (MaxQueuedRequests <= 0)
        {
            return;
        }

        while (Volatile.Read(ref QueuedRequests) > MaxQueuedRequests &&
               Requests.TryDequeue(out _))
        {
            Interlocked.Decrement(ref QueuedRequests);
            Interlocked.Increment(ref DroppedRequests);
        }
    }

    public void Dispose()
    {
        try { Cancellation.Cancel(); } catch { }

        foreach (var pair in Pending.ToArray())
        {
            if (!Pending.TryRemove(pair.Key, out var pending))
            {
                continue;
            }

            try
            {
                if (Interlocked.CompareExchange(ref pending.Replied, 1, 0) == 0)
                {
                    pending.Context.Response.StatusCode = 503;
                    pending.Context.Response.ContentLength64 = 0;
                    pending.Context.Response.Close();
                }
            }
            catch
            {
            }
            finally
            {
                pending.Completion.TrySetResult(true);
            }
        }

        try { Listener.Stop(); } catch { }
        try { Listener.Close(); } catch { }
        Cancellation.Dispose();
    }
}

[McpServerToolType]
public static class HttpMockTools
{
    private static readonly ConcurrentDictionary<string, TalvoraHttpMockRuntime> Listeners =
        new(StringComparer.OrdinalIgnoreCase);

    [McpServerTool(
        Name = "talvora_http_mock_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockStartResponse)),
     Description("Start an in-process HTTP mock/webhook listener on arbitrary HttpListener prefixes. Supports automatic default replies or manual replies, arbitrary response headers/body, bounded or unlimited capture queues, and unrestricted host/port prefixes.")]
    public static TalvoraHttpMockStartResponse Start(
        string[] prefixes,
        bool autoReply = true,
        int defaultStatusCode = 200,
        string? defaultBody = null,
        string? defaultBodyBase64 = null,
        string defaultContentType = "text/plain; charset=utf-8",
        Dictionary<string, string>? defaultHeaders = null,
        string requestBodyMode = "text",
        string requestEncoding = "utf-8",
        int maxRequestBodyBytes = 2 * 1024 * 1024,
        int maxQueuedRequests = 1000,
        int pendingResponseTimeoutSeconds = 30)
    {
        if (prefixes is null || prefixes.Length == 0)
        {
            throw new ArgumentException("At least one HTTP listener prefix is required.", nameof(prefixes));
        }
        if (defaultStatusCode is < 100 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultStatusCode));
        }
        if (defaultBody is not null && defaultBodyBase64 is not null)
        {
            throw new ArgumentException("Provide either defaultBody or defaultBodyBase64, not both.");
        }
        if (maxRequestBodyBytes < 0 || maxQueuedRequests < 0 || pendingResponseTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException("Request/queue/timeout limits cannot be negative.");
        }

        requestBodyMode = requestBodyMode.Trim().ToLowerInvariant();
        if (requestBodyMode is not ("text" or "base64" or "none"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestBodyMode),
                "requestBodyMode must be text, base64, or none.");
        }

        var encoding = Encoding.GetEncoding(requestEncoding);
        var responseBody = defaultBodyBase64 is not null
            ? Convert.FromBase64String(defaultBodyBase64)
            : encoding.GetBytes(defaultBody ?? string.Empty);

        var listener = new HttpListener();
        var normalizedPrefixes = prefixes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPrefixes.Length == 0)
        {
            throw new ArgumentException("At least one non-empty HTTP listener prefix is required.", nameof(prefixes));
        }

        foreach (var prefix in normalizedPrefixes)
        {
            listener.Prefixes.Add(prefix);
        }

        var listenerId = Guid.NewGuid().ToString("N");
        var runtime = new TalvoraHttpMockRuntime
        {
            ListenerId = listenerId,
            Listener = listener,
            Prefixes = normalizedPrefixes,
            AutoReply = autoReply,
            DefaultResponse = new TalvoraHttpMockResponseDefinition(
                defaultStatusCode,
                defaultContentType,
                new Dictionary<string, string>(
                    defaultHeaders ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                responseBody),
            MaxQueuedRequests = maxQueuedRequests,
            MaxRequestBodyBytes = maxRequestBodyBytes,
            RequestBodyMode = requestBodyMode,
            RequestEncoding = encoding,
            PendingResponseTimeoutSeconds = pendingResponseTimeoutSeconds,
            StartedAtUtc = DateTime.UtcNow,
            Cancellation = new CancellationTokenSource(),
        };

        if (!Listeners.TryAdd(listenerId, runtime))
        {
            runtime.Dispose();
            throw new InvalidOperationException($"Failed to register HTTP mock listener: {listenerId}");
        }

        try
        {
            listener.Start();
            _ = ListenLoopAsync(runtime);
        }
        catch
        {
            Listeners.TryRemove(listenerId, out _);
            runtime.Dispose();
            throw;
        }

        return new TalvoraHttpMockStartResponse(
            listenerId,
            normalizedPrefixes,
            autoReply,
            defaultStatusCode,
            defaultContentType,
            runtime.StartedAtUtc);
    }

    [McpServerTool(
        Name = "talvora_http_mock_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockListenerInfo)),
     Description("Return live state for one Talvora HTTP mock listener.")]
    public static TalvoraHttpMockListenerInfo Get(string listenerId) =>
        GetRuntime(listenerId).ToInfo();

    [McpServerTool(
        Name = "talvora_http_mock_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockListResponse)),
     Description("List all active Talvora HTTP mock/webhook listeners in the current service instance.")]
    public static TalvoraHttpMockListResponse List()
    {
        var listeners = Listeners.Values
            .OrderBy(value => value.StartedAtUtc)
            .ThenBy(value => value.ListenerId, StringComparer.OrdinalIgnoreCase)
            .Select(value => value.ToInfo())
            .ToArray();

        return new TalvoraHttpMockListResponse(listeners.Length, listeners);
    }

    [McpServerTool(
        Name = "talvora_http_mock_read",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockReadResponse)),
     Description("Read captured HTTP requests. consume=true removes returned requests. afterSequence filters by monotonically increasing sequence; maxRequests=0 returns all queued matches.")]
    public static TalvoraHttpMockReadResponse Read(
        string listenerId,
        long afterSequence = 0,
        int maxRequests = 200,
        bool consume = true)
    {
        if (afterSequence < 0 || maxRequests < 0)
        {
            throw new ArgumentOutOfRangeException("afterSequence and maxRequests cannot be negative.");
        }

        var runtime = GetRuntime(listenerId);
        List<TalvoraHttpMockRequest> result;

        if (consume)
        {
            result = new List<TalvoraHttpMockRequest>();

            while (runtime.Requests.TryPeek(out var head) &&
                   head.Sequence <= afterSequence)
            {
                if (runtime.Requests.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref runtime.QueuedRequests);
                }
            }

            while ((maxRequests == 0 || result.Count < maxRequests) &&
                   runtime.Requests.TryDequeue(out var request))
            {
                Interlocked.Decrement(ref runtime.QueuedRequests);
                if (request.Sequence > afterSequence)
                {
                    result.Add(request);
                }
            }
        }
        else
        {
            IEnumerable<TalvoraHttpMockRequest> query = runtime.Requests
                .ToArray()
                .Where(request => request.Sequence > afterSequence)
                .OrderBy(request => request.Sequence);

            if (maxRequests > 0)
            {
                query = query.Take(maxRequests);
            }

            result = query.ToList();
        }

        return new TalvoraHttpMockReadResponse(
            listenerId,
            result.Count,
            Math.Max(0, Volatile.Read(ref runtime.QueuedRequests)),
            runtime.Pending.Count,
            Math.Max(0, Interlocked.Read(ref runtime.DroppedRequests)),
            result);
    }

    [McpServerTool(
        Name = "talvora_http_mock_reply",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockReplyResponse)),
     Description("Reply to a pending request captured by an autoReply=false listener. Status, headers, content type, and text/base64 body are caller-controlled.")]
    public static async Task<TalvoraHttpMockReplyResponse> Reply(
        string listenerId,
        string requestId,
        int statusCode = 200,
        string? body = null,
        string? bodyBase64 = null,
        string contentType = "text/plain; charset=utf-8",
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        if (statusCode is < 100 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }
        if (body is not null && bodyBase64 is not null)
        {
            throw new ArgumentException("Provide either body or bodyBase64, not both.");
        }

        var runtime = GetRuntime(listenerId);
        if (!runtime.Pending.TryGetValue(requestId, out var pending))
        {
            return new TalvoraHttpMockReplyResponse(listenerId, requestId, false, false, null);
        }

        var bytes = bodyBase64 is not null
            ? Convert.FromBase64String(bodyBase64)
            : Encoding.UTF8.GetBytes(body ?? string.Empty);

        var response = new TalvoraHttpMockResponseDefinition(
            statusCode,
            contentType,
            new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            bytes);

        var replied = await TrySendResponseAsync(pending, response, cancellationToken);

        if (replied)
        {
            runtime.Pending.TryRemove(requestId, out _);
            pending.Completion.TrySetResult(true);
        }

        return new TalvoraHttpMockReplyResponse(
            listenerId,
            requestId,
            true,
            replied,
            replied ? statusCode : null);
    }

    [McpServerTool(
        Name = "talvora_http_mock_stop",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpMockStopResponse)),
     Description("Stop and dispose one HTTP mock listener. Pending requests are closed with HTTP 503. Missing listener IDs are handled idempotently.")]
    public static TalvoraHttpMockStopResponse Stop(string listenerId)
    {
        if (!Listeners.TryRemove(listenerId, out var runtime))
        {
            return new TalvoraHttpMockStopResponse(listenerId, false, false, 0);
        }

        var pending = runtime.Pending.Count;
        runtime.Dispose();

        return new TalvoraHttpMockStopResponse(listenerId, true, true, pending);
    }

    private static async Task ListenLoopAsync(TalvoraHttpMockRuntime runtime)
    {
        var cancellationToken = runtime.Cancellation.Token;

        while (!cancellationToken.IsCancellationRequested &&
               runtime.Listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await runtime.Listener.GetContextAsync()
                    .WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (
                cancellationToken.IsCancellationRequested ||
                !runtime.Listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = HandleContextAsync(runtime, context);
        }
    }

    private static async Task HandleContextAsync(
        TalvoraHttpMockRuntime runtime,
        HttpListenerContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");

        try
        {
            var capture = await CaptureRequestAsync(runtime, requestId, context);

            if (runtime.AutoReply)
            {
                runtime.Enqueue(capture with { PendingResponse = false });

                var pending = new TalvoraPendingHttpMockRequest
                {
                    Context = context,
                    Completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously),
                };

                _ = await TrySendResponseAsync(
                    pending,
                    runtime.DefaultResponse,
                    runtime.Cancellation.Token);
                return;
            }

            var pendingRequest = new TalvoraPendingHttpMockRequest
            {
                Context = context,
                Completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously),
            };

            if (!runtime.Pending.TryAdd(requestId, pendingRequest))
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                return;
            }

            runtime.Enqueue(capture with { PendingResponse = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                runtime.Cancellation.Token);
            if (runtime.PendingResponseTimeoutSeconds > 0)
            {
                timeout.CancelAfter(
                    TimeSpan.FromSeconds(runtime.PendingResponseTimeoutSeconds));
            }

            try
            {
                await pendingRequest.Completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (Interlocked.CompareExchange(
                        ref pendingRequest.Replied,
                        1,
                        0) == 0)
                {
                    try
                    {
                        await SendResponseAsync(
                            context.Response,
                            runtime.DefaultResponse,
                            CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                runtime.Pending.TryRemove(requestId, out _);
            }
        }
        catch
        {
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentLength64 = 0;
                    context.Response.Close();
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<TalvoraHttpMockRequest> CaptureRequestAsync(
        TalvoraHttpMockRuntime runtime,
        string requestId,
        HttpListenerContext context)
    {
        var request = context.Request;

        var headers = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is null)
            {
                continue;
            }

            headers[key] = request.Headers.GetValues(key) ?? [];
        }

        var query = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.QueryString.AllKeys)
        {
            if (key is null)
            {
                continue;
            }

            query[key] = request.QueryString.GetValues(key) ?? [];
        }

        var bodyBytes = 0L;
        var truncated = false;
        string? body = null;

        if (runtime.RequestBodyMode != "none" &&
            request.HasEntityBody)
        {
            using var memory = new MemoryStream();
            var buffer = new byte[64 * 1024];

            while (true)
            {
                var read = await request.InputStream.ReadAsync(
                    buffer,
                    runtime.Cancellation.Token);
                if (read == 0)
                {
                    break;
                }

                bodyBytes += read;

                if (runtime.MaxRequestBodyBytes == 0)
                {
                    memory.Write(buffer, 0, read);
                    continue;
                }

                var remaining = runtime.MaxRequestBodyBytes - memory.Length;
                if (remaining <= 0)
                {
                    truncated = true;
                    continue;
                }

                var toWrite = (int)Math.Min(read, remaining);
                memory.Write(buffer, 0, toWrite);
                if (toWrite < read)
                {
                    truncated = true;
                }
            }

            var bytes = memory.ToArray();
            body = runtime.RequestBodyMode == "base64"
                ? Convert.ToBase64String(bytes)
                : runtime.RequestEncoding.GetString(bytes);
        }
        else if (request.HasEntityBody)
        {
            bodyBytes = request.ContentLength64 >= 0
                ? request.ContentLength64
                : 0;
        }

        return new TalvoraHttpMockRequest(
            Interlocked.Increment(ref runtime.Sequence),
            requestId,
            DateTime.UtcNow,
            request.HttpMethod,
            request.Url?.ToString() ?? string.Empty,
            request.RawUrl ?? string.Empty,
            request.ProtocolVersion.ToString(),
            headers,
            query,
            request.RemoteEndPoint?.ToString(),
            request.LocalEndPoint?.ToString(),
            runtime.RequestBodyMode,
            body,
            bodyBytes,
            truncated,
            PendingResponse: false);
    }

    private static async Task<bool> TrySendResponseAsync(
        TalvoraPendingHttpMockRequest pending,
        TalvoraHttpMockResponseDefinition response,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref pending.Replied, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            await SendResponseAsync(
                pending.Context.Response,
                response,
                cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task SendResponseAsync(
        HttpListenerResponse response,
        TalvoraHttpMockResponseDefinition definition,
        CancellationToken cancellationToken)
    {
        response.StatusCode = definition.StatusCode;
        response.ContentType = definition.ContentType;

        foreach (var pair in definition.Headers)
        {
            response.Headers[pair.Key] = pair.Value;
        }

        response.ContentLength64 = definition.Body.LongLength;

        if (definition.Body.Length > 0)
        {
            await response.OutputStream.WriteAsync(
                definition.Body,
                cancellationToken);
        }

        response.OutputStream.Close();
        response.Close();
    }

    private static TalvoraHttpMockRuntime GetRuntime(string listenerId)
    {
        if (string.IsNullOrWhiteSpace(listenerId))
        {
            throw new ArgumentException("listenerId is required.", nameof(listenerId));
        }

        return Listeners.TryGetValue(listenerId, out var runtime)
            ? runtime
            : throw new KeyNotFoundException(
                $"Talvora HTTP mock listener was not found: {listenerId}");
    }

    private static string NormalizePrefix(string prefix)
    {
        var normalized = prefix.Trim();
        if (!normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized += "/";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                $"Invalid HttpListener prefix: {prefix}");
        }

        return normalized;
    }
}