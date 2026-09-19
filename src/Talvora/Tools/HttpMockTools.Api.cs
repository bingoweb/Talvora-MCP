using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class HttpMockTools
{
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
                .Where(request => request.Sequence > afterSequence);

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
}
