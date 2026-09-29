using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ManagedMcpTunnelHealthComponentSnapshot(
    string Id,
    string Status,
    string State,
    string? ReasonCode,
    bool Limited,
    string Detail,
    bool Critical);

internal sealed record ManagedMcpTunnelHealthSnapshot(
    int SchemaVersion,
    bool Live,
    bool Ready,
    string RuntimeVersion,
    string RuntimeLifecycle,
    IReadOnlyList<ManagedMcpTunnelHealthComponentSnapshot> Components,
    int? QueueDepth,
    int? DispatcherActive,
    int? ResponseInProgress,
    DateTimeOffset? LastActivityUtc)
{
    public bool HasCriticalDegradation =>
        Components.Any(component =>
            component.Critical &&
            string.Equals(
                component.Status,
                "degraded",
                StringComparison.OrdinalIgnoreCase));

    public bool IsQuietForMaintenance(
        DateTimeOffset nowUtc,
        TimeSpan quietPeriod)
    {
        if (SchemaVersion != 1 ||
            !Live ||
            !Ready ||
            !string.Equals(
                RuntimeLifecycle,
                "running",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var componentId in new[]
                 {
                     "queue",
                     "dispatcher",
                     "response-delivery",
                 })
        {
            var component = Components.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    componentId,
                    StringComparison.OrdinalIgnoreCase));
            if (component is null ||
                component.Limited ||
                !string.Equals(
                    component.Status,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return QueueDepth == 0 &&
               DispatcherActive == 0 &&
               ResponseInProgress == 0 &&
               LastActivityUtc is { } lastActivityUtc &&
               nowUtc - lastActivityUtc >= quietPeriod;
    }
}

internal static class ManagedMcpTunnelHealthService
{
    private const int MaximumHealthPayloadBytes = 256 * 1024;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(3));

    public static async Task<ManagedMcpTunnelHealthSnapshot?> GetSnapshotCachedAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var now = DateTimeOffset.UtcNow;
        if (Cache.TryGetValue(registration.Id, out var cached) &&
            cached.ExpiresAtUtc > now)
        {
            return cached.Snapshot;
        }

        var snapshot = await GetSnapshotAsync(
            registration,
            cancellationToken);
        Cache[registration.Id] = new CacheEntry(
            now.Add(CacheDuration),
            snapshot);
        return snapshot;
    }

    public static async Task<ManagedMcpTunnelHealthSnapshot?> GetSnapshotAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.Tunnel is not { Required: true })
        {
            return null;
        }

        var runtimeStatus =
            await ManagedMcpTunnelProvisioningService.GetRuntimeStatusCachedAsync(
                registration,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(runtimeStatus.HealthDetailsUrl) ||
            !TryValidateLoopbackHealthUri(
                runtimeStatus.HealthDetailsUrl,
                out var healthUri))
        {
            return null;
        }

        using var response = await Http.GetAsync(
            healthUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumHealthPayloadBytes)
        {
            throw new InvalidDataException(
                "Tunnel health payload exceeds the supported size limit.");
        }

        var payload = await ReadBoundedPayloadAsync(
            response.Content,
            cancellationToken);

        using var document = JsonDocument.Parse(
            payload,
            new JsonDocumentOptions
            {
                MaxDepth = 64,
            });
        var root = document.RootElement;

        var schemaVersion = TryGetInt32(root, "schema_version");
        if (schemaVersion <= 0)
        {
            throw new InvalidDataException(
                "Tunnel health payload does not contain a supported schema version.");
        }

        var live = TryGetBoolean(root, "live");
        var ready = TryGetBoolean(root, "ready");
        var runtimeVersion = string.Empty;
        var runtimeLifecycle = string.Empty;
        int? queueDepth = null;
        int? dispatcherActive = null;
        int? responseInProgress = null;
        DateTimeOffset? lastActivityUtc = null;
        if (TryGetObject(root, "runtime", out var runtime))
        {
            runtimeVersion = TryGetString(runtime, "version") ?? string.Empty;
            runtimeLifecycle = TryGetString(runtime, "lifecycle") ?? string.Empty;
        }

        var components = new List<ManagedMcpTunnelHealthComponentSnapshot>();
        if (TryGetObject(root, "components", out var componentMap))
        {
            foreach (var property in componentMap.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var status =
                    TryGetString(property.Value, "status") ?? "unknown";
                var state =
                    TryGetString(property.Value, "state") ?? "unknown";
                var reasonCode = TryGetString(property.Value, "reason_code");
                var limited = TryGetBoolean(property.Value, "limited");
                CaptureMaintenanceSignals(
                    property.Name,
                    property.Value,
                    ref queueDepth,
                    ref dispatcherActive,
                    ref responseInProgress,
                    ref lastActivityUtc);
                var detail = BuildComponentDetail(
                    property.Name,
                    property.Value,
                    status,
                    state,
                    reasonCode,
                    limited);

                components.Add(
                    new ManagedMcpTunnelHealthComponentSnapshot(
                        property.Name,
                        status,
                        state,
                        reasonCode,
                        limited,
                        detail,
                        IsCriticalComponent(property.Name)));
            }
        }

        return new ManagedMcpTunnelHealthSnapshot(
            schemaVersion,
            live,
            ready,
            runtimeVersion,
            runtimeLifecycle,
            components,
            queueDepth,
            dispatcherActive,
            responseInProgress,
            lastActivityUtc);
    }

    public static void Invalidate(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        _ = Cache.TryRemove(mcpId, out _);
    }

    private static async Task<byte[]> ReadBoundedPayloadAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var stream = await content.ReadAsStreamAsync(
            cancellationToken);
        using var buffer = new MemoryStream(
            capacity: Math.Min(
                MaximumHealthPayloadBytes,
                16 * 1024));
        var chunk = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            var remaining = MaximumHealthPayloadBytes + 1 - total;
            if (remaining <= 0)
            {
                throw new InvalidDataException(
                    "Tunnel health payload exceeds the supported size limit.");
            }

            var read = await stream.ReadAsync(
                chunk.AsMemory(
                    0,
                    Math.Min(chunk.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumHealthPayloadBytes)
            {
                throw new InvalidDataException(
                    "Tunnel health payload exceeds the supported size limit.");
            }

            await buffer.WriteAsync(
                chunk.AsMemory(0, read),
                cancellationToken);
        }

        return buffer.ToArray();
    }

    private static bool TryValidateLoopbackHealthUri(
        string value,
        out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttp &&
            candidate.IsLoopback)
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsCriticalComponent(string id) =>
        string.Equals(
            id,
            "control-plane",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            id,
            "response-delivery",
            StringComparison.OrdinalIgnoreCase);

    private static void CaptureMaintenanceSignals(
        string componentId,
        JsonElement component,
        ref int? queueDepth,
        ref int? dispatcherActive,
        ref int? responseInProgress,
        ref DateTimeOffset? lastActivityUtc)
    {
        if (!TryGetObject(component, "details", out var details))
        {
            return;
        }

        if (string.Equals(
                componentId,
                "queue",
                StringComparison.OrdinalIgnoreCase))
        {
            queueDepth = TryGetNullableInt32(
                details,
                "depth");
            UpdateLatestTimestamp(
                details,
                "last_enqueue",
                ref lastActivityUtc);
            UpdateLatestTimestamp(
                details,
                "last_dequeue",
                ref lastActivityUtc);
            return;
        }

        if (string.Equals(
                componentId,
                "dispatcher",
                StringComparison.OrdinalIgnoreCase))
        {
            dispatcherActive = TryGetNullableInt32(
                details,
                "active");
            UpdateLatestTimestamp(
                details,
                "last_start",
                ref lastActivityUtc);
            UpdateLatestTimestamp(
                details,
                "last_completion",
                ref lastActivityUtc);
            return;
        }

        if (string.Equals(
                componentId,
                "response-delivery",
                StringComparison.OrdinalIgnoreCase))
        {
            responseInProgress = TryGetNullableInt32(
                details,
                "in_progress");
            UpdateLatestTimestamp(
                details,
                "last_accepted",
                ref lastActivityUtc);
            UpdateLatestTimestamp(
                details,
                "last_completed",
                ref lastActivityUtc);
        }
    }

    private static void UpdateLatestTimestamp(
        JsonElement source,
        string propertyName,
        ref DateTimeOffset? latestUtc)
    {
        var value = TryGetString(
            source,
            propertyName);
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return;
        }

        if (latestUtc is null ||
            parsed > latestUtc.Value)
        {
            latestUtc = parsed;
        }
    }

    private static string BuildComponentDetail(
        string id,
        JsonElement component,
        string status,
        string state,
        string? reasonCode,
        bool limited)
    {
        var detailParts = new List<string>
        {
            $"Durum={status}",
            $"State={state}",
        };

        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            detailParts.Add($"Neden={reasonCode}");
        }

        if (limited)
        {
            detailParts.Add("Sınırlı gözlem");
        }

        if (!TryGetObject(component, "details", out var details))
        {
            return string.Join(" • ", detailParts);
        }

        if (string.Equals(id, "control-plane", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "consecutive_failures", "Ardışık hata");
            AddSeconds(detailParts, details, "current_poll_age_seconds", "Poll yaşı");
            AddString(detailParts, details, "last_success", "Son başarı");
        }
        else if (string.Equals(id, "response-delivery", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "accepted", "Kabul");
            AddInt(detailParts, details, "completed", "Tamamlanan");
            AddInt(detailParts, details, "retries", "Retry");
            AddInt(detailParts, details, "terminal_failures", "Kalıcı hata");
        }
        else if (string.Equals(id, "queue", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "depth", "Kuyruk");
            AddInt(detailParts, details, "capacity", "Kapasite");
            AddPercent(detailParts, details, "utilization", "Doluluk");
            AddSeconds(detailParts, details, "backpressure_seconds", "Backpressure");
        }
        else if (string.Equals(id, "dispatcher", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "active", "Aktif");
            AddInt(detailParts, details, "pool_limit", "Havuz");
            AddInt(detailParts, details, "failures", "Hata");
            AddInt(detailParts, details, "timeouts", "Timeout");
        }
        else if (string.Equals(id, "mcp", StringComparison.OrdinalIgnoreCase))
        {
            AddString(detailParts, details, "transport", "Transport");
            AddString(detailParts, details, "evidence", "Kanıt");
            if (TryGetObject(details, "tools_list", out var toolsList))
            {
                AddInt(detailParts, toolsList, "retained_count", "Gözlenen araç");
                if (TryGetBoolean(toolsList, "complete"))
                {
                    detailParts.Add("Araç listesi=tam");
                }
            }

            if (TryGetObject(details, "startup_probe", out var startupProbe))
            {
                AddString(detailParts, startupProbe, "state", "Startup probe");
            }
        }
        else if (string.Equals(id, "oauth", StringComparison.OrdinalIgnoreCase))
        {
            AddBoolean(detailParts, details, "discovery_complete", "Discovery");
        }
        else if (string.Equals(id, "proxy", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "route_count", "Rota");
        }
        else if (string.Equals(id, "cloudflared", StringComparison.OrdinalIgnoreCase))
        {
            AddBoolean(detailParts, details, "enabled", "Etkin");
            AddBoolean(detailParts, details, "ready", "Hazır");
        }
        else if (string.Equals(id, "harpoon", StringComparison.OrdinalIgnoreCase))
        {
            AddInt(detailParts, details, "target_count", "Hedef");
            AddString(detailParts, details, "catalog_state", "Katalog");
        }

        return string.Join(" • ", detailParts);
    }

    private static void AddString(
        List<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        var value = TryGetString(source, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}={value}");
        }
    }

    private static void AddInt(
        List<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (source.TryGetProperty(propertyName, out var value) &&
            value.TryGetInt64(out var number))
        {
            parts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}={number}"));
        }
    }

    private static void AddSeconds(
        List<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (source.TryGetProperty(propertyName, out var value) &&
            value.TryGetDouble(out var seconds))
        {
            parts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}={seconds:0.0}s"));
        }
    }

    private static void AddPercent(
        List<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (source.TryGetProperty(propertyName, out var value) &&
            value.TryGetDouble(out var fraction))
        {
            parts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}={fraction:P0}"));
        }
    }

    private static void AddBoolean(
        List<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (source.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            parts.Add($"{label}={(value.GetBoolean() ? "evet" : "hayır")}");
        }
    }

    private static bool TryGetObject(
        JsonElement source,
        string propertyName,
        out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? TryGetString(
        JsonElement source,
        string propertyName)
    {
        if (source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryGetBoolean(
        JsonElement source,
        string propertyName)
    {
        if (source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false,
        };
    }

    private static int TryGetInt32(
        JsonElement source,
        string propertyName)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(propertyName, out var value) &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return 0;
    }

    private static int? TryGetNullableInt32(
        JsonElement source,
        string propertyName)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(propertyName, out var value) &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAtUtc,
        ManagedMcpTunnelHealthSnapshot? Snapshot);
}
