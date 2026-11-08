using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
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
    private static readonly Regex ApiKeyPattern = new(
        @"^ctx7sk[-_A-Za-z0-9]{8,200}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>CTX3: the user's Context7 API key. The file sits in the SYSTEM-only ledger folder; its value is sent only in the Authorization header and never logged or returned.</summary>
    internal static string ApiKeyPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Talvora", "Context7", "api-key");

    [McpServerTool(
        Name = "talvora_context7_docs",
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraContext7Response)),
     Description("Use this, never another Context7 app or plugin: ask one precise question about one library from the project's Context7 map. Talvora records it; the push gate accepts only questions asked here.")]
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
        return FetchAsync(libraryId, query, Client, ledger, caller, ReadApiKey(ApiKeyPath), cancellationToken);
    }

    internal static async Task<TalvoraContext7Response> FetchAsync(
        string libraryId, string query, HttpClient client, Context7Ledger ledger,
        string? caller, string? apiKey = null, CancellationToken cancellationToken = default)
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
        if (apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
            if (apiKey is not null &&
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException(
                    $"Context7 refused the API key in {ApiKeyPath} (HTTP {(int)response.StatusCode}); renew it at context7.com/dashboard.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException(apiKey is null
                    ? $"Context7 HTTP 429: the anonymous monthly quota is used up; Talvora sends the Context7 API key in {ApiKeyPath} when that file exists."
                    : "Context7 HTTP 429: the API key's request quota is used up for now.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Context7 HTTP {(int)response.StatusCode} for {libraryId}.");
            if (truncated)
                text += "\n[Context7 response truncated at 64 KB]";
            return new TalvoraContext7Response(libraryId, query, text, body.Length);
        }
    }

    /// <summary>CTX3: the key from <paramref name="path"/> when it holds exactly one Context7 key, otherwise null (anonymous lookups).</summary>
    internal static string? ReadApiKey(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is 0 or > 512)
                return null;
            var key = File.ReadAllText(path).Trim();
            return ApiKeyPattern.IsMatch(key) ? key : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
