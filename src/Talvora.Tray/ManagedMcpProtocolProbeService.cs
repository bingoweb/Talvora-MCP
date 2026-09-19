using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ManagedMcpProtocolProbeResult(
    bool Ready,
    int ToolCount,
    IReadOnlyList<string> MissingTools,
    bool BrowserRuntimeReady,
    bool BrowserSmokeRan,
    bool BrowserSmokePassed,
    string Detail);

internal static class ManagedMcpProtocolProbeService
{
    private static readonly TimeSpan BrowserIdentityTimeout =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NonSmokeCacheDuration =
        TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, ProtocolCacheEntry> NonSmokeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<ManagedMcpProtocolProbeResult> ProbeCachedAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var now = DateTimeOffset.UtcNow;
        if (NonSmokeCache.TryGetValue(
                registration.Id,
                out var cached) &&
            cached.ExpiresAtUtc > now)
        {
            return cached.Result;
        }

        var result = await ProbeAsync(
            registration,
            runBrowserSmoke: false,
            cancellationToken);

        NonSmokeCache[registration.Id] = new ProtocolCacheEntry(
            now.Add(NonSmokeCacheDuration),
            result);
        return result;
    }

    public static void InvalidateCache(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        _ = NonSmokeCache.TryRemove(mcpId, out _);
    }

    public static async Task<ManagedMcpProtocolProbeResult> ProbeAsync(
        ManagedMcpRegistration registration,
        bool runBrowserSmoke,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!string.Equals(
                registration.Transport,
                "streamable-http",
                StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                $"Desteklenmeyen MCP transport: {registration.Transport}",
                browserSmokeRan: false);
        }

        var probe = registration.ProtocolProbe;
        if (probe is null)
        {
            return Failed(
                "MCP protokol sağlık kontrolü tanımlı değil.",
                browserSmokeRan: false);
        }

        try
        {
            await using var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(registration.Endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(8),
                });

            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);

            var tools = await client.ListToolsAsync(
                cancellationToken: cancellationToken);
            var names = tools
                .Select(tool => tool.Name)
                .ToHashSet(StringComparer.Ordinal);
            var missing = probe.RequiredTools
                .Where(name => !names.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (missing.Length > 0)
            {
                return new ManagedMcpProtocolProbeResult(
                    false,
                    tools.Count,
                    missing,
                    false,
                    false,
                    false,
                    $"MCP initialize başarılı ancak {missing.Length} beklenen capability aracı eksik.");
            }

            var browserSmokePassed =
                !probe.BrowserSmokeRequired ||
                HasCurrentGenerationBrowserSmoke(probe);

            if (!runBrowserSmoke ||
                !probe.BrowserSmokeRequired)
            {
                return new ManagedMcpProtocolProbeResult(
                    true,
                    tools.Count,
                    [],
                    !probe.BrowserSmokeRequired ||
                    browserSmokePassed,
                    false,
                    browserSmokePassed,
                    browserSmokePassed
                        ? $"MCP initialize, capability araçları ve current runtime generation için gerçek browser smoke doğrulaması başarılı. Araç sayısı: {tools.Count}."
                        : $"MCP initialize ve capability araçları başarılı; current runtime generation için gerçek browser smoke bekleniyor. Araç sayısı: {tools.Count}.");
            }

            if (!names.Contains("browser_tabs"))
            {
                return new ManagedMcpProtocolProbeResult(
                    false,
                    tools.Count,
                    ["browser_tabs"],
                    false,
                    false,
                    false,
                    "Gerçek browser runtime doğrulaması için browser_tabs aracı bulunamadı.");
            }

            var tabsResult = await client.CallToolAsync(
                "browser_tabs",
                new Dictionary<string, object?>
                {
                    ["action"] = "list",
                },
                cancellationToken: cancellationToken);

            var browserRuntimeReady =
                tabsResult.IsError is not true;
            if (!browserRuntimeReady)
            {
                return new ManagedMcpProtocolProbeResult(
                    false,
                    tools.Count,
                    [],
                    false,
                    false,
                    false,
                    "MCP protokolü sağlıklı ancak browser runtime erişilebilir değil.");
            }

            var browserIdentity =
                await WaitForBrowserIdentityAsync(
                    registration,
                    cancellationToken);
            if (browserIdentity is null)
            {
                return new ManagedMcpProtocolProbeResult(
                    false,
                    tools.Count,
                    [],
                    false,
                    false,
                    false,
                    "Browser MCP çağrısı başarılı ancak smoke için canlı browser process kimliği doğrulanamadı.");
            }

            if (!names.Contains("browser_run_code_unsafe"))
            {
                return new ManagedMcpProtocolProbeResult(
                    false,
                    tools.Count,
                    ["browser_run_code_unsafe"],
                    browserRuntimeReady,
                    false,
                    false,
                    "İzole health sayfası smoke'u için browser_run_code_unsafe aracı bulunamadı.");
            }

            var capturedGeneration =
                ReadGeneration(probe.RuntimeGenerationStatePath);
            if (string.IsNullOrWhiteSpace(capturedGeneration))
            {
                throw new InvalidOperationException(
                    "Playwright runtime generation bilgisi bulunamadı.");
            }

            InvalidateBrowserSmoke(probe);

            var smokeUrl = string.IsNullOrWhiteSpace(
                    probe.BrowserSmokeUrl)
                ? "data:text/html,<h1>Talvora Playwright Health</h1>"
                : probe.BrowserSmokeUrl;

            var jsUrl = JsonSerializer.Serialize(smokeUrl);
            var code =
                "async (page) => { " +
                "const healthPage = await page.context().newPage(); " +
                "try { " +
                $"await healthPage.goto({jsUrl}); " +
                "const snapshot = await healthPage.locator('body').ariaSnapshot(); " +
                "return { url: healthPage.url(), title: await healthPage.title(), snapshot }; " +
                "} finally { await healthPage.close(); } " +
                "}";

            var smokeResult = await client.CallToolAsync(
                "browser_run_code_unsafe",
                new Dictionary<string, object?>
                {
                    ["code"] = code,
                },
                cancellationToken: cancellationToken);

            if (smokeResult.IsError is true)
            {
                throw new InvalidOperationException(
                    "Playwright izole browser smoke sayfasını çalıştıramadı.");
            }

            var smokeText = string.Join(
                Environment.NewLine,
                smokeResult.Content
                    .OfType<TextContentBlock>()
                    .Select(block => block.Text));

            var expectedText = probe.BrowserSmokeExpectedText;
            if (!string.IsNullOrWhiteSpace(expectedText) &&
                !smokeText.Contains(
                    expectedText,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Playwright browser smoke beklenen ARIA içeriğini içermiyor: {expectedText}");
            }

            var currentGeneration =
                ReadGeneration(probe.RuntimeGenerationStatePath);
            if (!string.Equals(
                    capturedGeneration,
                    currentGeneration,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Playwright runtime generation smoke sırasında değişti; sonuç geçersiz sayıldı.");
            }

            var currentBrowserIdentity =
                await WaitForBrowserIdentityAsync(
                    registration,
                    cancellationToken);
            if (currentBrowserIdentity is null ||
                currentBrowserIdentity.ProcessId != browserIdentity.ProcessId ||
                currentBrowserIdentity.StartTimeUtcTicks !=
                    browserIdentity.StartTimeUtcTicks ||
                !WindowsProcessTree.IsSameLiveProcess(browserIdentity))
            {
                throw new InvalidOperationException(
                    "Playwright browser instance smoke sırasında değişti; sonuç geçersiz sayıldı.");
            }

            await RecordBrowserSmokeAsync(
                probe,
                capturedGeneration,
                browserIdentity,
                tools.Count,
                cancellationToken);

            return new ManagedMcpProtocolProbeResult(
                true,
                tools.Count,
                [],
                true,
                true,
                true,
                $"MCP initialize, capability araçları ve current browser instance üzerinde gerçek navigate + ARIA snapshot smoke başarılı. Araç sayısı: {tools.Count}.");
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            InvalidOperationException or
            OperationCanceledException or
            IOException or
            JsonException)
        {
            return Failed(
                ex is OperationCanceledException
                    ? "MCP protokol sağlık kontrolü zaman aşımına uğradı."
                    : ex.Message,
                browserSmokeRan: runBrowserSmoke);
        }
    }

    public static async Task<ManagedMcpProtocolProbeResult>
        WaitUntilReadyAsync(
            ManagedMcpRegistration registration,
            bool runBrowserSmoke,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        ManagedMcpProtocolProbeResult? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last = await ProbeAsync(
                registration,
                runBrowserSmoke: false,
                cancellationToken);

            if (last.Ready)
            {
                break;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(1),
                cancellationToken);
        }

        if (last is not { Ready: true })
        {
            return last ?? Failed(
                "MCP protokol sağlık kontrolü zaman aşımına uğradı.",
                browserSmokeRan: false);
        }

        if (!runBrowserSmoke ||
            registration.ProtocolProbe?.BrowserSmokeRequired != true)
        {
            return last;
        }

        return await ProbeAsync(
            registration,
            runBrowserSmoke: true,
            cancellationToken);
    }

    private static async Task<WindowsProcessIdentity?>
        WaitForBrowserIdentityAsync(
            ManagedMcpRegistration registration,
            CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.Add(BrowserIdentityTimeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identity = TryReadBrowserIdentity(registration);
            if (identity is not null &&
                WindowsProcessTree.IsSameLiveProcess(identity))
            {
                return identity;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                cancellationToken);
        }

        return null;
    }

    private static WindowsProcessIdentity? TryReadBrowserIdentity(
        ManagedMcpRegistration registration)
    {
        var processStatePath =
            registration.ProtocolProbe?.RuntimeProcessStatePath;
        if (string.IsNullOrWhiteSpace(processStatePath) ||
            !File.Exists(processStatePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(processStatePath));
            if (!document.RootElement.TryGetProperty(
                    "backendPid",
                    out var backendPidElement) ||
                backendPidElement.ValueKind != JsonValueKind.Number ||
                !backendPidElement.TryGetInt32(out var backendPid) ||
                backendPid <= 0)
            {
                return null;
            }

            var executableNames =
                registration.BrowserChannel?.ToLowerInvariant() switch
                {
                    "msedge" => new[] { "msedge.exe" },
                    "firefox" => new[] { "firefox.exe" },
                    "chrome" => new[] { "chrome.exe" },
                    _ => new[]
                    {
                        "chrome.exe",
                        "msedge.exe",
                        "firefox.exe",
                    },
                };

            return WindowsProcessTree.FindClosestDescendant(
                backendPid,
                executableNames);
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                "Browser process identity could not be read",
                ex);
            return null;
        }
    }

    private static bool HasCurrentGenerationBrowserSmoke(
        ManagedMcpProtocolProbeRegistration probe)
    {
        var generation =
            ReadGeneration(probe.RuntimeGenerationStatePath);
        if (string.IsNullOrWhiteSpace(generation) ||
            string.IsNullOrWhiteSpace(
                probe.BrowserSmokeStatePath) ||
            !File.Exists(probe.BrowserSmokeStatePath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(
                    probe.BrowserSmokeStatePath));

            var root = document.RootElement;

            if (!root.TryGetProperty(
                    "generation",
                    out var markerGeneration) ||
                markerGeneration.ValueKind !=
                    JsonValueKind.String ||
                !string.Equals(
                    generation,
                    markerGeneration.GetString(),
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty(
                    "browserPid",
                    out var markerPid) ||
                !markerPid.TryGetInt32(
                    out var browserPid) ||
                browserPid <= 0 ||
                !root.TryGetProperty(
                    "browserStartTimeUtcTicks",
                    out var markerStartTime) ||
                !markerStartTime.TryGetInt64(
                    out var browserStartTimeUtcTicks) ||
                browserStartTimeUtcTicks <= 0)
            {
                return false;
            }

            if (!root.TryGetProperty(
                    "passedAtUtc",
                    out var passedAtElement) ||
                passedAtElement.ValueKind !=
                    JsonValueKind.String ||
                !DateTimeOffset.TryParse(
                    passedAtElement.GetString(),
                    out _))
            {
                return false;
            }

            return root.TryGetProperty(
                       "toolCount",
                       out var toolCountElement) &&
                   toolCountElement.TryGetInt32(
                       out var toolCount) &&
                   toolCount > 0;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                "Browser smoke state could not be read",
                ex);
            return false;
        }
    }

    private static void InvalidateBrowserSmoke(
        ManagedMcpProtocolProbeRegistration probe)
    {
        if (string.IsNullOrWhiteSpace(
                probe.BrowserSmokeStatePath))
        {
            return;
        }

        try
        {
            File.Delete(probe.BrowserSmokeStatePath);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Browser smoke state temizlenemedi.",
                ex);
        }
    }

    private static async Task RecordBrowserSmokeAsync(
        ManagedMcpProtocolProbeRegistration probe,
        string capturedGeneration,
        WindowsProcessIdentity browserIdentity,
        int toolCount,
        CancellationToken cancellationToken)
    {
        var currentGeneration =
            ReadGeneration(
                probe.RuntimeGenerationStatePath);
        if (!string.Equals(
                capturedGeneration,
                currentGeneration,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Playwright runtime generation smoke sonucu yazılmadan önce değişti.");
        }

        if (!WindowsProcessTree.IsSameLiveProcess(
                browserIdentity))
        {
            throw new InvalidOperationException(
                "Playwright browser instance smoke sonucu yazılmadan önce kapandı.");
        }

        if (string.IsNullOrWhiteSpace(
                probe.BrowserSmokeStatePath))
        {
            throw new InvalidOperationException(
                "Browser smoke state yolu tanımlı değil.");
        }

        var path = probe.BrowserSmokeStatePath;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Browser smoke state dizini çözümlenemedi.");
        Directory.CreateDirectory(directory);

        var payload = JsonSerializer.Serialize(
            new
            {
                generation = capturedGeneration,
                browserPid = browserIdentity.ProcessId,
                browserStartTimeUtcTicks =
                    browserIdentity.StartTimeUtcTicks,
                browserExecutable =
                    browserIdentity.ExecutableName,
                passedAtUtc =
                    DateTimeOffset.UtcNow.ToString("O"),
                toolCount,
            });

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(
            tempPath,
            payload,
            cancellationToken);
        File.Move(
            tempPath,
            path,
            overwrite: true);
    }

    private static string? ReadGeneration(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty(
                    "generation",
                    out var generation) ||
                generation.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = generation.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                "Runtime generation state could not be read",
                ex);
            return null;
        }
    }

    private sealed record ProtocolCacheEntry(
        DateTimeOffset ExpiresAtUtc,
        ManagedMcpProtocolProbeResult Result);

    private static ManagedMcpProtocolProbeResult Failed(
        string detail,
        bool browserSmokeRan) =>
        new(
            false,
            0,
            [],
            false,
            browserSmokeRan,
            false,
            detail);
}
