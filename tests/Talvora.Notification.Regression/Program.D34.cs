using System.Text.Json;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D34: the published health address is read from its file; a fully ready status is kept 10 minutes.</summary>
    private static void AssertD34TunnelHealthFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-d34-" + Guid.NewGuid().ToString("N"));
        var health = Directory.CreateDirectory(Path.Combine(root, "health")).FullName;
        try
        {
            var file = Path.Combine(health, "alias-x.url");
            File.WriteAllText(file, "http://127.0.0.1:60409");
            if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "alias-x") != "http://127.0.0.1:60409/health?details=true")
                throw new InvalidOperationException("D34: the published health address must be read.");
            foreach (var bad in new[] { "http://example.com:1", "https://127.0.0.1:60409", "http://127.0.0.1", "http://127.0.0.1:60409/other", new string('1', 300) })
            {
                File.WriteAllText(file, bad);
                if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "alias-x") is not null)
                    throw new InvalidOperationException("D34: an unsafe health address was accepted.");
            }
            if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "missing") is not null)
                throw new InvalidOperationException("D34: a missing file must give no address.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var ready = new ManagedMcpTunnelRuntimeStatus(Ready: true, ProcessRunning: true, Healthy: true, IdentityMatches: true, Detail: "ok");
        if (ManagedMcpTunnelProvisioningService.RuntimeStatusCacheDurationFor(ready) != TimeSpan.FromMinutes(10) ||
            ManagedMcpTunnelProvisioningService.RuntimeStatusCacheDurationFor(ready with { IdentityMatches = false }) != TimeSpan.FromSeconds(15))
            throw new InvalidOperationException("D34: only a fully ready status may be kept for 10 minutes.");

        using var notReady = JsonDocument.Parse("""{"schema_version":1,"live":true,"ready":false}""");
        using var fine = JsonDocument.Parse("""{"schema_version":1,"live":true,"ready":true}""");
        if (!ManagedMcpTunnelHealthService.ShouldRenewRuntimeStatus(ManagedMcpTunnelHealthService.ParseSnapshot(notReady.RootElement)) ||
            ManagedMcpTunnelHealthService.ShouldRenewRuntimeStatus(ManagedMcpTunnelHealthService.ParseSnapshot(fine.RootElement)))
            throw new InvalidOperationException("D34: a not-ready health answer must drop the cached status, a ready one must not.");
        Console.WriteLine("D34_TUNNEL_HEALTH_FILE_GREEN");
    }
}
