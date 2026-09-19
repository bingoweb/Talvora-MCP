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
            PendingResponseTimeoutSeconds,
            StartedAtUtc,
            Math.Max(0, Volatile.Read(ref QueuedRequests)),
            Pending.Count,
            Math.Max(0, Interlocked.Read(ref DroppedRequests)),
            isListening,
            Volatile.Read(ref _lastError));
    }

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

        try { Listener.Stop(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        try { Listener.Close(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        Cancellation.Dispose();
    }
}

[McpServerToolType]
public static partial class HttpMockTools
{
    private static readonly ConcurrentDictionary<string, TalvoraHttpMockRuntime> Listeners =
        new(StringComparer.OrdinalIgnoreCase);
}
