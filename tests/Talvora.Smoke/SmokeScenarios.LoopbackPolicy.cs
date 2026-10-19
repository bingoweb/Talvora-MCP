using System.Net;
using System.Text;

internal static partial class SmokeScenarios
{
    internal static async Task RunLoopbackHostOriginLiveAsync()
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:7676"),
            Timeout = TimeSpan.FromSeconds(10),
        };

        async Task ExpectAsync(
            HttpMethod method,
            string path,
            HttpStatusCode expected,
            string? host = null,
            string? origin = null,
            bool penpotAsset = false)
        {
            using var request = new HttpRequestMessage(method, path);
            if (host is not null)
            {
                request.Headers.Host = host;
            }

            if (origin is not null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            if (method == HttpMethod.Post)
            {
                request.Content = new StringContent(
                    """{"jsonrpc":"2.0","id":1,"method":"ping"}""",
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await client.SendAsync(request);
            if (response.StatusCode != expected)
            {
                throw new InvalidOperationException(
                    $"Loopback security: {method} {path}, Host={host ?? "(default)"}, " +
                    $"Origin={origin ?? "(absent)"}: expected {(int)expected}, " +
                    $"got {(int)response.StatusCode}.");
            }

            if (penpotAsset &&
                (!response.Headers.TryGetValues(
                        "Access-Control-Allow-Origin", out var origins) ||
                    !origins.Contains("*", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Penpot static asset {path} lost its wildcard CORS header.");
            }
        }

        await ExpectAsync(HttpMethod.Get, "/healthz",
            HttpStatusCode.BadRequest, host: "rebind.evil.example:7676");
        await ExpectAsync(HttpMethod.Get, "/penpot-ai/manifest.json",
            HttpStatusCode.BadRequest, host: "rebind.evil.example:7676");
        await ExpectAsync(HttpMethod.Get, "/healthz",
            HttpStatusCode.Forbidden, origin: "https://design.example");
        await ExpectAsync(HttpMethod.Get, "/penpot-ai/healthz",
            HttpStatusCode.Forbidden, origin: "https://design.example");

        foreach (var endpoint in new[] { "/mcp", "/mcp/dev", "/mcp/admin" })
        {
            await ExpectAsync(HttpMethod.Post, endpoint,
                HttpStatusCode.Forbidden, origin: "https://design.example");
        }

        await ExpectAsync(HttpMethod.Get, "/healthz", HttpStatusCode.OK);
        await ExpectAsync(HttpMethod.Get, "/healthz", HttpStatusCode.OK,
            origin: "http://127.0.0.1:7676");
        await ExpectAsync(HttpMethod.Get, "/healthz", HttpStatusCode.OK,
            origin: "http://localhost:7676");
        await ExpectAsync(HttpMethod.Get, "/healthz", HttpStatusCode.OK,
            origin: "http://[::1]:7676");

        foreach (var asset in new[]
        {
            "manifest.json", "plugin.js", "index.html", "icon.svg",
        })
        {
            await ExpectAsync(HttpMethod.Get, "/penpot-ai/" + asset,
                HttpStatusCode.OK, origin: "https://design.example",
                penpotAsset: true);
        }
    }
}
