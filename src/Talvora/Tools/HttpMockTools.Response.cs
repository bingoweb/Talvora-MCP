using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class HttpMockTools
{
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = Interlocked.CompareExchange(
                ref pending.Replied,
                0,
                1);
            throw;
        }
        catch
        {
            _ = Interlocked.CompareExchange(
                ref pending.Replied,
                0,
                1);
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
