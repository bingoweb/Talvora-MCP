namespace Talvora;

internal static class PenpotAiPluginEndpoints
{
    private const string RoutePrefix = "/penpot-ai";
    private const string AssetDirectoryName = "PenpotAiPlugin";

    private static readonly IReadOnlyDictionary<string, string> Assets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest.json"] = "application/json; charset=utf-8",
            ["plugin.js"] = "text/javascript; charset=utf-8",
            ["index.html"] = "text/html; charset=utf-8",
            ["icon.svg"] = "image/svg+xml",
        };

    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            $"{RoutePrefix}/healthz",
            (HttpContext context) =>
            {
                ApplyHeaders(context.Response);
                return Results.Json(
                    new
                    {
                        product = "Talvora Penpot AI",
                        version = "0.1.0",
                        ready = Assets.Keys.All(AssetExists),
                        manifest = $"{RoutePrefix}/manifest.json",
                    });
            });

        foreach (var asset in Assets)
        {
            var assetName = asset.Key;
            var contentType = asset.Value;
            app.MapGet(
                $"{RoutePrefix}/{assetName}",
                (HttpContext context) =>
                {
                    ApplyHeaders(context.Response);
                    var path = GetAssetPath(assetName);
                    return File.Exists(path)
                        ? Results.File(path, contentType)
                        : Results.NotFound(
                            new
                            {
                                error = "Talvora Penpot AI asset is missing.",
                                asset = assetName,
                            });
                });
        }
    }

    private static bool AssetExists(string assetName) =>
        File.Exists(GetAssetPath(assetName));

    private static string GetAssetPath(string assetName)
    {
        if (!Assets.ContainsKey(assetName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetName),
                assetName,
                "Unknown Talvora Penpot AI asset.");
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            AssetDirectoryName,
            assetName);
    }

    private static void ApplyHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
    }
}
