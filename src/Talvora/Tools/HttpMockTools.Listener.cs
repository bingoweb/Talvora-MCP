using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class HttpMockTools
{
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
            catch (Exception ex)
            {
                runtime.RecordError(ex);
                try
                {
                    runtime.Listener.Stop();
                }
                catch (Exception stopError) when (stopError is ObjectDisposedException or HttpListenerException)
                {
                }
                break;
            }

            var handlerSlotHeld = false;
            var globalSlotHeld = false;
            try
            {
                await runtime.HandlerSlots.WaitAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
                handlerSlotHeld = true;
                await GlobalHandlerSlots.WaitAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
                globalSlotHeld = true;
            }
            catch (OperationCanceledException)
            {
                if (globalSlotHeld)
                {
                    GlobalHandlerSlots.Release();
                }
                if (handlerSlotHeld)
                {
                    runtime.HandlerSlots.Release();
                }
                RejectContext(context, 503);
                break;
            }

            _ = HandleContextWithSlotsAsync(
                runtime,
                context);
        }
    }

    private static async Task HandleContextWithSlotsAsync(
        TalvoraHttpMockRuntime runtime,
        HttpListenerContext context)
    {
        try
        {
            await HandleContextAsync(
                    runtime,
                    context)
                .ConfigureAwait(false);
        }
        finally
        {
            GlobalHandlerSlots.Release();
            runtime.HandlerSlots.Release();
        }
    }

    private static async Task HandleContextAsync(
        TalvoraHttpMockRuntime runtime,
        HttpListenerContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pendingSlotHeld = false;

        try
        {
            if (!runtime.AutoReply)
            {
                pendingSlotHeld =
                    await runtime.PendingSlots.WaitAsync(
                            0,
                            runtime.Cancellation.Token)
                        .ConfigureAwait(false);
                if (!pendingSlotHeld)
                {
                    Interlocked.Increment(
                        ref runtime.RejectedRequests);
                    RejectContext(context, 503);
                    return;
                }
            }

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
                Interlocked.Increment(
                    ref runtime.RejectedRequests);
                RejectContext(context, 503);
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
                    catch (Exception ex)
                    {
                        runtime.RecordError(ex);
                    }
                }
            }
            finally
            {
                runtime.Pending.TryRemove(requestId, out _);
            }
        }
        catch (Exception ex)
        {
            runtime.RecordError(ex);
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentLength64 = 0;
                    context.Response.Close();
                }
            }
            catch (Exception responseError) when (responseError is ObjectDisposedException or InvalidOperationException or HttpListenerException)
            {
            }
        }
        finally
        {
            if (pendingSlotHeld)
            {
                runtime.PendingSlots.Release();
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

                bodyBytes =
                    checked(bodyBytes + read);

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

    private static void RejectContext(
        HttpListenerContext context,
        int statusCode)
    {
        try
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
        }
        catch (Exception ex) when (
            ex is ObjectDisposedException or
                InvalidOperationException or
                HttpListenerException)
        {
        }
    }
}
