using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraContext7Response(string LibraryId, string Query, string Text, int Bytes);

/// <summary>CTX1: look up pinned Context7 documentation and persist the question, never the answer.</summary>
[McpServerToolType]
public static class Context7Tools
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Regex LibraryIdPattern = new(
        @"^/[^/\s]+/[^/\s]+(/[^/\s]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [McpServerTool(
        Name = "talvora_context7_docs",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraContext7Response)),
     Description("Ask Context7 one precise question about one library (an id from the project's Context7 map). Talvora records the question; Context7 evidence must name questions asked here.")]
    public static Task<TalvoraContext7Response> DocsAsync(
        [Description("Context7 library id from docs/codex/CONTEXT7.md, e.g. /dotnet/docs.")] string libraryId,
        [Description("One precise 12–400 character question with at least three words, e.g. HttpClient Timeout and HttpMessageHandler for tests with a capped response read.")] string query,
        McpServer server,
        CancellationToken cancellationToken = default)
    {
        var services = server.Services ?? throw new InvalidOperationException("MCP services are unavailable.");
        var ledger = services.GetRequiredService<Context7Ledger>();
        var headers = services.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers;
        var caller = TalvoraLineTrafficMeter.ResolveCallerName(
            headers?["X-Talvora-Client"].ToString(),
            server.ClientInfo?.Name,
            headers?.UserAgent.ToString());
        return FetchAsync(libraryId, query, Client, ledger, caller, cancellationToken);
    }

    internal static async Task<TalvoraContext7Response> FetchAsync(
        string libraryId, string query, HttpClient client, Context7Ledger ledger,
        string? caller, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(libraryId) || !LibraryIdPattern.IsMatch(libraryId))
            throw new ArgumentException("Context7 libraryId must be /owner/library or /owner/library/version.", nameof(libraryId));
        if (query is null || query.Length is < 12 or > 400 ||
            query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 3)
            throw new ArgumentException("Context7 query must be 12–400 characters and contain at least three words.", nameof(query));

        var url = "https://context7.com/api/v2/context?libraryId=" +
                  Uri.EscapeDataString(libraryId) + "&query=" +
                  Uri.EscapeDataString(query) + "&type=txt";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Talvora");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                    !cancellationToken.IsCancellationRequested)
        {
            ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, caller,
                libraryId, query, 0, 0, string.Empty));
            throw new InvalidOperationException("Context7 request failed: " + ex.Message, ex);
        }

        using (response)
        {
            byte[] body;
            var truncated = false;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                while (buffer.Length <= MaximumResponseBytes)
                {
                    var left = (MaximumResponseBytes + 1) - (int)buffer.Length;
                    var n = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, left)), timeout.Token);
                    if (n == 0)
                        break;
                    buffer.Write(chunk, 0, n);
                }
                var captured = buffer.ToArray();
                truncated = captured.Length > MaximumResponseBytes;
                body = truncated ? captured[..MaximumResponseBytes] : captured;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException &&
                                        !cancellationToken.IsCancellationRequested)
            {
                ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, caller,
                    libraryId, query, (int)response.StatusCode, 0, string.Empty));
                throw new InvalidOperationException("Context7 response could not be read: " + ex.Message, ex);
            }
            var text = Encoding.UTF8.GetString(body);
            var sha = Convert.ToHexString(SHA256.HashData(body))[..16].ToLowerInvariant();
            ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, caller,
                libraryId, query, (int)response.StatusCode, body.Length, sha));

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"Context7 does not know {libraryId}; take the id from the project's Context7 map (HTTP 404).");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Context7 HTTP {(int)response.StatusCode} for {libraryId}.");
            if (truncated)
                text += "\n[Context7 response truncated at 64 KB]";
            return new TalvoraContext7Response(libraryId, query, text, body.Length);
        }
    }
}
