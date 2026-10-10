using Talvora.Tools;

namespace Talvora;

internal sealed record EmbeddedMcpIntegrationHealthState(
    string Id,
    string DisplayName,
    bool Ready,
    bool Authenticated,
    string Version,
    int ToolCount,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    string? Detail);

/// <summary>
/// Read-only, bounded checks of embedded MCP integrations. No OAuth/API
/// credential values or MCP tool schemas are exposed by the health endpoint.
/// These integrations are not separate Windows services or managed tunnels.
/// </summary>
internal static class EmbeddedMcpIntegrationHealth
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private static readonly Dictionary<string, DateTimeOffset> LastSuccessful =
        new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<EmbeddedMcpIntegrationHealthState>? Cached;
    private static DateTimeOffset CachedUntilUtc;

    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/healthz/integrations", async (
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var integrations = await SnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Json(new
            {
                schemaVersion = 1,
                integrations,
            });
        });
    }

    private static async Task<IReadOnlyList<EmbeddedMcpIntegrationHealthState>>
        SnapshotAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Cached is not null && DateTimeOffset.UtcNow < CachedUntilUtc)
            {
                return Cached;
            }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(18));

            var checks = await Task.WhenAll(
                ProbeStitchAsync(budget.Token),
                ProbeHostingerAsync(budget.Token)).ConfigureAwait(false);

            var result = new List<EmbeddedMcpIntegrationHealthState>(
                checks.Length);
            foreach (var state in checks)
            {
                if (state.Ready)
                {
                    LastSuccessful[state.Id] = state.CheckedAtUtc;
                }

                result.Add(state with
                {
                    LastSuccessfulAtUtc =
                        LastSuccessful.TryGetValue(state.Id, out var when)
                            ? when
                            : null,
                });
            }

            Cached = result;
            CachedUntilUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<EmbeddedMcpIntegrationHealthState>
        ProbeStitchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await StitchTools.Info(cancellationToken)
                .ConfigureAwait(false);
            if (!info.Found || !info.Authenticated ||
                !info.CanvasCredentialPresent)
            {
                return NewState(
                    "stitch", "Google Stitch", false,
                    info.Authenticated, info.Version, 0,
                    "Stitch OAuth veya Canvas oturumu doğrulanamadı.");
            }

            var mcp = await StitchMcpTools.Status(cancellationToken)
                .ConfigureAwait(false);
            var ready = mcp.Ready && mcp.ToolCount > 0;
            return NewState(
                "stitch", "Google Stitch", ready,
                info.Authenticated, info.Version ?? mcp.Version,
                mcp.ToolCount,
                ready ? null : "Stitch MCP araç keşfi başarısız.");
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return NewState("stitch", "Google Stitch", false, false,
                null, 0, "Stitch sağlık kontrolü zaman aşımına uğradı.");
        }
        catch (Exception)
        {
            return NewState("stitch", "Google Stitch", false, false,
                null, 0, "Stitch sağlık kontrolü tamamlanamadı.");
        }
    }

    private static async Task<EmbeddedMcpIntegrationHealthState>
        ProbeHostingerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await HostingerMcpTools.Status(cancellationToken)
                .ConfigureAwait(false);
            var ready = status.Ready &&
                status.CredentialConfigured &&
                status.ToolCount > 0;
            return NewState(
                "hostinger", "Hostinger API", ready,
                status.CredentialConfigured, status.Version, status.ToolCount,
                ready ? null : "Hostinger MCP bağlantısı veya araç keşfi başarısız.");
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return NewState("hostinger", "Hostinger API", false, false,
                null, 0, "Hostinger sağlık kontrolü zaman aşımına uğradı.");
        }
        catch (Exception)
        {
            return NewState("hostinger", "Hostinger API", false, false,
                null, 0, "Hostinger sağlık kontrolü tamamlanamadı.");
        }
    }

    private static EmbeddedMcpIntegrationHealthState NewState(
        string id, string name, bool ready, bool authenticated,
        string? version, int toolCount, string? detail) =>
        new(
            id,
            name,
            ready,
            authenticated,
            string.IsNullOrWhiteSpace(version) ? "Bilinmiyor" : version,
            toolCount,
            DateTimeOffset.UtcNow,
            null,
            detail);
}
