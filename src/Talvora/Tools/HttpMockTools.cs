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
    int MaxConcurrentRequests,
    int MaxPendingRequests,
    int PendingResponseTimeoutSeconds,
    DateTime StartedAtUtc,
    int QueuedRequests,
    int PendingRequests,
    long DroppedRequests,
    long RejectedRequests,
    bool IsListening,
    string? LastError);

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
    long RejectedRequests,
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
    public required int MaxConcurrentRequests { get; init; }
    public required int MaxPendingRequests { get; init; }
    public required string RequestBodyMode { get; init; }
    public required Encoding RequestEncoding { get; init; }
    public required int PendingResponseTimeoutSeconds { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required CancellationTokenSource Cancellation { get; init; }
    public required SemaphoreSlim HandlerSlots { get; init; }
    public required SemaphoreSlim PendingSlots { get; init; }

    public ConcurrentQueue<TalvoraHttpMockRequest> Requests { get; } = new();
    public ConcurrentDictionary<string, TalvoraPendingHttpMockRequest> Pending { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public long Sequence;
    public int QueuedRequests;
    public long QueuedRetainedBytes;
    public long DroppedRequests;
    public long RejectedRequests;
    private string? _lastError;

    public void RecordError(Exception exception) =>
        Volatile.Write(
            ref _lastError,
            $"{exception.GetType().Name}: {exception.Message}");

    public TalvoraHttpMockListenerInfo ToInfo()
    {
        var isListening = false;
        try
        {
            isListening = Listener.IsListening;
        }
        catch (ObjectDisposedException)
        {
        }

        return new TalvoraHttpMockListenerInfo(
            ListenerId,
            Prefixes,
            AutoReply,
            DefaultResponse.StatusCode,
            DefaultResponse.ContentType,
            MaxQueuedRequests,
            MaxRequestBodyBytes,
            MaxConcurrentRequests,
            MaxPendingRequests,
            PendingResponseTimeoutSeconds,
            StartedAtUtc,
            Math.Max(0, Volatile.Read(ref QueuedRequests)),
            Pending.Count,
            Math.Max(0, Interlocked.Read(ref DroppedRequests)),
            Math.Max(0, Interlocked.Read(ref RejectedRequests)),
            isListening,
            Volatile.Read(ref _lastError));
    }

    public void Enqueue(TalvoraHttpMockRequest request)
    {
        var retainedBytes =
            HttpMockTools.EstimateRetainedBytes(request);
        Interlocked.Increment(ref QueuedRequests);
        Interlocked.Add(
            ref QueuedRetainedBytes,
            retainedBytes);
        HttpMockTools.AddGlobalQueuedRequest(
            retainedBytes);
        Requests.Enqueue(request);

        while ((Cancellation.IsCancellationRequested ||
                Volatile.Read(ref QueuedRequests) > MaxQueuedRequests ||
                HttpMockTools.GlobalQueuedRequests >
                    HttpMockTools.AbsoluteGlobalQueuedRequests ||
                HttpMockTools.GlobalQueuedRetainedBytes >
                    HttpMockTools.AbsoluteGlobalQueuedRetainedBytes) &&
               TryDequeue(out _))
        {
            Interlocked.Increment(ref DroppedRequests);
        }
    }

    public bool TryDequeue(
        out TalvoraHttpMockRequest request)
    {
        if (!Requests.TryDequeue(out var dequeued))
        {
            request = null!;
            return false;
        }

        request = dequeued;
        var retainedBytes =
            HttpMockTools.EstimateRetainedBytes(dequeued);
        Interlocked.Decrement(ref QueuedRequests);
        Interlocked.Add(
            ref QueuedRetainedBytes,
            -retainedBytes);
        HttpMockTools.RemoveGlobalQueuedRequest(
            retainedBytes);
        return true;
    }

    public void Dispose()
    {
        try { Cancellation.Cancel(); } catch (ObjectDisposedException) { }

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
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or HttpListenerException)
            {
            }
            finally
            {
                pending.Completion.TrySetResult(true);
            }
        }

        while (TryDequeue(out _))
        {
        }

        try { Listener.Stop(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        try { Listener.Close(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        Cancellation.Dispose();
    }
}

[McpServerToolType]
public static partial class HttpMockTools
{
    private const int DefaultMaxRequestBodyBytes =
        2 * 1024 * 1024;
    private const int AbsoluteMaxRequestBodyBytes =
        64 * 1024 * 1024;
    private const int DefaultMaxQueuedRequests = 1000;
    private const int AbsoluteMaxQueuedRequests = 100_000;
    private const int DefaultMaxConcurrentRequests = 64;
    private const int AbsoluteMaxConcurrentRequests = 512;
    private const int DefaultMaxPendingRequests = 1000;
    private const int AbsoluteMaxPendingRequests = 10_000;
    private const int DefaultPendingResponseTimeoutSeconds = 30;
    private const int AbsolutePendingResponseTimeoutSeconds =
        24 * 60 * 60;
    internal const int AbsoluteGlobalQueuedRequests =
        100_000;
    internal const long AbsoluteGlobalQueuedRetainedBytes =
        512L * 1024 * 1024;
    private const int AbsoluteGlobalConcurrentRequests = 512;

    private static readonly ConcurrentDictionary<string, TalvoraHttpMockRuntime> Listeners =
        new(StringComparer.OrdinalIgnoreCase);
    internal static readonly SemaphoreSlim GlobalHandlerSlots =
        new(
            AbsoluteGlobalConcurrentRequests,
            AbsoluteGlobalConcurrentRequests);
    private static int globalQueuedRequests;
    private static long globalQueuedRetainedBytes;

    internal static int GlobalQueuedRequests =>
        Math.Max(
            0,
            Volatile.Read(ref globalQueuedRequests));

    internal static long GlobalQueuedRetainedBytes =>
        Math.Max(
            0L,
            Interlocked.Read(
                ref globalQueuedRetainedBytes));

    internal static void AddGlobalQueuedRequest(
        long retainedBytes)
    {
        Interlocked.Increment(
            ref globalQueuedRequests);
        Interlocked.Add(
            ref globalQueuedRetainedBytes,
            retainedBytes);
    }

    internal static void RemoveGlobalQueuedRequest(
        long retainedBytes)
    {
        Interlocked.Decrement(
            ref globalQueuedRequests);
        Interlocked.Add(
            ref globalQueuedRetainedBytes,
            -retainedBytes);
    }

    internal static long EstimateRetainedBytes(
        TalvoraHttpMockRequest request)
    {
        var characters =
            (long)request.RequestId.Length +
            request.Method.Length +
            request.Url.Length +
            request.RawUrl.Length +
            request.ProtocolVersion.Length +
            (request.RemoteEndPoint?.Length ?? 0) +
            (request.LocalEndPoint?.Length ?? 0) +
            request.BodyMode.Length +
            (request.Body?.Length ?? 0);

        foreach (var pair in request.Headers)
        {
            characters += pair.Key.Length;
            foreach (var value in pair.Value)
            {
                characters += value.Length;
            }
        }

        foreach (var pair in request.Query)
        {
            characters += pair.Key.Length;
            foreach (var value in pair.Value)
            {
                characters += value.Length;
            }
        }

        return checked(
            512L +
            characters * sizeof(char));
    }
}
