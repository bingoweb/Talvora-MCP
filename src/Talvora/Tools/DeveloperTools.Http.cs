using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_http_request",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpResponse)),
     Description("Send an arbitrary HTTP request to any URI reachable by the Talvora service. Supports any method, headers, text or base64 request bodies, redirect control, optional TLS certificate bypass, and text/base64/none response modes.")]
    public static async Task<TalvoraHttpResponse> HttpRequest(
        string method,
        string url,
        Dictionary<string, string>? headers = null,
        string? body = null,
        string? bodyBase64 = null,
        string? contentType = null,
        int timeoutSeconds = 60,
        bool allowAutoRedirect = true,
        bool ignoreTlsErrors = false,
        string responseMode = "text",
        long maxResponseBytes = 2 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("HTTP method is required.", nameof(method));
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("A valid absolute URI is required.", nameof(url));
        }
        if (body is not null && bodyBase64 is not null)
        {
            throw new ArgumentException("Provide either body or bodyBase64, not both.");
        }
        if (timeoutSeconds < 0 || maxResponseBytes < 0)
        {
            throw new ArgumentOutOfRangeException("Timeout and response limits cannot be negative.");
        }

        responseMode = responseMode.Trim().ToLowerInvariant();
        if (responseMode is not ("text" or "base64" or "none"))
        {
            throw new ArgumentOutOfRangeException(nameof(responseMode), "responseMode must be text, base64, or none.");
        }

        using var client = TalvoraHttp.CreateClient(
            ignoreTlsErrors,
            allowAutoRedirect);
        using var request = new HttpRequestMessage(new HttpMethod(method.Trim().ToUpperInvariant()), uri);

        if (bodyBase64 is not null)
        {
            request.Content = new ByteArrayContent(Convert.FromBase64String(bodyBase64));
        }
        else if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
        }

        if (request.Content is not null && !string.IsNullOrWhiteSpace(contentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            if (request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
            {
                continue;
            }

            if (request.Content is null)
            {
                request.Content = new ByteArrayContent([]);
            }

            if (!request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
            {
                throw new InvalidOperationException($"Unable to apply HTTP header: {pair.Key}");
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        stopwatch.Stop();

        var responseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            responseHeaders[header.Key] = header.Value.ToArray();
        }

        string? responseBody = null;
        long bodyBytes = 0;
        var truncated = false;

        if (responseMode != "none")
        {
            var read = await ReadResponseBytesAsync(response.Content, maxResponseBytes, timeout.Token);
            bodyBytes = read.Bytes.LongLength;
            truncated = read.Truncated;

            if (responseMode == "base64")
            {
                responseBody = Convert.ToBase64String(read.Bytes);
            }
            else
            {
                var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
                responseBody = encoding.GetString(read.Bytes);
            }
        }

        return new TalvoraHttpResponse(
            request.Method.Method,
            response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString(),
            (int)response.StatusCode,
            response.ReasonPhrase,
            response.Version.ToString(),
            responseHeaders,
            responseMode,
            responseBody,
            bodyBytes,
            truncated,
            stopwatch.ElapsedMilliseconds);
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadResponseBytesAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var truncated = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = maxBytes == 0
                ? buffer.Length
                : (int)Math.Min(buffer.Length, Math.Max(0, maxBytes + 1 - memory.Length));

            if (remaining == 0)
            {
                truncated = true;
                break;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);

            if (maxBytes > 0 && memory.Length > maxBytes)
            {
                truncated = true;
                memory.SetLength(maxBytes);
                break;
            }
        }

        return (memory.ToArray(), truncated);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
