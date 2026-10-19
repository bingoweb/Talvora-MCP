namespace Talvora;

internal static class LoopbackRequestGuard
{
    private static readonly HashSet<string> AllowedOriginHosts =
        new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "[::1]" };

    public static WebApplication UseLoopbackOriginGuard(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 &&
                !IsStaticPenpotAssetRequest(context.Request) &&
                !IsLoopbackOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Bu istek yerel bir Talvora istemcisinden gelmediği için reddedildi.",
                });
                return;
            }
            await next(context);
        });
        return app;
    }

    private static bool IsStaticPenpotAssetRequest(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) &&
        request.Path.Value is
            "/penpot-ai/manifest.json" or
            "/penpot-ai/plugin.js" or
            "/penpot-ai/index.html" or
            "/penpot-ai/icon.svg";

    // Uri.Host keeps the brackets for IPv6 ("[::1]"). Any loopback port is local by definition.
    private static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        AllowedOriginHosts.Contains(uri.Host);
}
