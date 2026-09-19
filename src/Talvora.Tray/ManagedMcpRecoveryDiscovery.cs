using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal interface IManagedMcpRecoveryDiscovery
{
    string Id { get; }
    ManagedMcpRegistration? Discover();
}

internal sealed class TalvoraManagedMcpRecoveryDiscovery : IManagedMcpRecoveryDiscovery
{
    public string Id => "talvora";

    public ManagedMcpRegistration Discover()
    {
        var tunnelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "TunnelClient",
            "business.json");
        var tunnel = ManagedMcpTunnelConfigReader.Read(
            tunnelPath,
            "talvora-business");

        return new ManagedMcpRegistration
        {
            Id = Id,
            DisplayName = "Talvora MCP",
            Description = "Talvora yerel geliştirme MCP servisi ve güvenli OpenAI tüneli.",
            Endpoint = TalvoraConstants.McpUrl,
            HealthEndpoint = TalvoraConstants.HealthUrl,
            AutoStart = true,
            Tunnel = tunnel,
            Components =
            [
                new ManagedMcpComponentRegistration
                {
                    Id = "service",
                    DisplayName = "Talvora servisi",
                    Kind = "windows-service",
                    Name = TalvoraConstants.ServiceName,
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "tunnel",
                    DisplayName = "Secure MCP Tunnel",
                    Kind = "tunnel",
                    Name = tunnel.Alias,
                },
            ],
            DiscoveryHints =
            [
                new ManagedMcpDiscoveryHint
                {
                    Kind = "windows-service",
                    Value = TalvoraConstants.ServiceName,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "config-file",
                    Value = tunnelPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "tunnel-alias",
                    Value = tunnel.Alias,
                },
            ],
        };
    }
}

internal sealed class GiteaManagedMcpRecoveryDiscovery : IManagedMcpRecoveryDiscovery
{
    public string Id => "gitea";

    public ManagedMcpRegistration? Discover()
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        var giteaExecutable = Path.Combine(programFiles, "Gitea", "gitea.exe");
        var giteaMcpExecutable = Path.Combine(
            programFiles,
            "Gitea MCP",
            "gitea-mcp.exe");
        var tunnelPath = Path.Combine(
            localAppData,
            "Gitea",
            "McpTunnel",
            "business.json");

        if (!File.Exists(giteaExecutable) &&
            !File.Exists(giteaMcpExecutable) &&
            !File.Exists(tunnelPath))
        {
            return null;
        }

        var tunnel = ManagedMcpTunnelConfigReader.Read(
            tunnelPath,
            "gitea-business");

        return new ManagedMcpRegistration
        {
            Id = Id,
            DisplayName = "Gitea MCP",
            Description = "Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri.",
            Endpoint = "http://127.0.0.1:8081/mcp",
            HealthEndpoint = "http://127.0.0.1:8081/healthz",
            AutoStart = true,
            Tunnel = tunnel,
            Components =
            [
                new ManagedMcpComponentRegistration
                {
                    Id = "gitea-service",
                    DisplayName = "Gitea",
                    Kind = "windows-service",
                    Name = "gitea",
                    HealthEndpoint = "http://127.0.0.1:3001/api/healthz",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "caddy-service",
                    DisplayName = "Caddy",
                    Kind = "windows-service",
                    Name = "caddy",
                    HealthEndpoint = "http://127.0.0.1:3000/api/healthz",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "mcp-server",
                    DisplayName = "Gitea MCP sunucusu",
                    Kind = "scheduled-task",
                    Name = "Gitea MCP Server",
                    HealthEndpoint = "http://127.0.0.1:8081/healthz",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "tunnel",
                    DisplayName = "Secure MCP Tunnel",
                    Kind = "scheduled-task",
                    Name = "Gitea MCP Tunnel",
                },
            ],
            DiscoveryHints =
            [
                new ManagedMcpDiscoveryHint
                {
                    Kind = "windows-service",
                    Value = "gitea",
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "scheduled-task",
                    Value = "Gitea MCP Server",
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "config-file",
                    Value = tunnelPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "tunnel-alias",
                    Value = tunnel.Alias,
                },
            ],
        };
    }
}

internal sealed class PlaywrightManagedMcpRecoveryDiscovery : IManagedMcpRecoveryDiscovery
{
    private const string TaskName = "Talvora Playwright MCP";
    private const string Endpoint = "http://127.0.0.1:8932/mcp";

    public string Id => "playwright";

    public ManagedMcpRegistration? Discover()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(
            localAppData,
            "Talvora",
            "PlaywrightMCP");
        var runtimeRoot = Path.Combine(root, "runtime");
        var launcherPath = Path.Combine(root, "Start-PlaywrightMcp.ps1");
        var packagePath = Path.Combine(
            runtimeRoot,
            "node_modules",
            "@playwright",
            "mcp",
            "package.json");
        var supervisorPath = Path.Combine(
            runtimeRoot,
            "supervisor.mjs");
        var profilePath = Path.Combine(root, "profile");
        var bootstrapLogPath = Path.Combine(root, "logs", "bootstrap.log");
        var serverLogPath = Path.Combine(root, "logs", "server.log");
        var tunnelPath = Path.Combine(root, "McpTunnel", "business.json");
        var tunnelLogPath = Path.Combine(root, "McpTunnel", "state", "logs", "playwright-business.log");
        var statePath = Path.Combine(root, "state", "server-start.json");
        var taskFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "Tasks",
            TaskName);

        if (!File.Exists(launcherPath) &&
            !File.Exists(packagePath) &&
            !Directory.Exists(profilePath) &&
            !File.Exists(tunnelPath) &&
            !File.Exists(statePath) &&
            !File.Exists(taskFilePath))
        {
            return null;
        }

        var tunnel = ManagedMcpTunnelConfigReader.Read(
            tunnelPath,
            "playwright-business");

        return new ManagedMcpRegistration
        {
            Id = Id,
            DisplayName = "Playwright MCP",
            Description = "Microsoft Playwright ile görünmez Chrome otomasyonu, MCP protokol sağlığı ve yönetilen yaşam döngüsü.",
            Endpoint = Endpoint,
            Transport = "streamable-http",
            AutoStart = true,
            PackageName = "@playwright/mcp",
            BrowserChannel = "chrome",
            ProfileMode = "Kalıcı headless profil",
            ProfilePath = profilePath,
            ProtocolProbe = new ManagedMcpProtocolProbeRegistration
            {
                RequiredTools =
                [
                    "browser_tabs",
                    "browser_navigate",
                    "browser_snapshot",
                    "browser_run_code_unsafe",
                    "browser_file_upload",
                    "browser_take_screenshot",
                    "browser_pdf_save",
                    "browser_network_requests",
                    "browser_start_tracing",
                    "browser_stop_tracing",
                ],
                BrowserSmokeRequired = true,
                BrowserSmokeUrl = "data:text/html,<html><head><title>Talvora Playwright Health</title></head><body><h1>Talvora Playwright Health</h1></body></html>",
                BrowserSmokeExpectedText = "Talvora Playwright Health",
                RuntimeGenerationStatePath = Path.Combine(root, "state", "server-start.json"),
                RuntimeProcessStatePath = Path.Combine(root, "state", "process-tree.json"),
                BackendEndpoint = "http://127.0.0.1:8931/mcp",
                BrowserSmokeStatePath = Path.Combine(root, "state", "browser-smoke.json"),
            },
            Tunnel = tunnel,
            Components =
            [
                new ManagedMcpComponentRegistration
                {
                    Id = "launcher-task",
                    DisplayName = "Playwright başlatıcı",
                    Kind = "scheduled-task",
                    Name = TaskName,
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "mcp-process",
                    DisplayName = "Playwright MCP işlemi",
                    Kind = "process",
                    Name = Path.Combine(
                        runtimeRoot,
                        "node_modules",
                        "@playwright",
                        "mcp",
                        "cli.js"),
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "mcp-protocol",
                    DisplayName = "Playwright MCP protokolü",
                    Kind = "mcp-protocol",
                    Name = Endpoint,
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "browser-runtime",
                    DisplayName = "Chrome otomasyon çalışma zamanı",
                    Kind = "browser-runtime",
                    Name = "chrome",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "browser-smoke",
                    DisplayName = "Gerçek browser doğrulaması",
                    Kind = "browser-smoke",
                    Name = "navigate + accessibility snapshot",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "tunnel",
                    DisplayName = "Secure MCP Tunnel",
                    Kind = "tunnel",
                    Name = tunnel.Alias,
                    Required = tunnel.Required,
                },
            ],
            DiscoveryHints =
            [
                new ManagedMcpDiscoveryHint
                {
                    Kind = "scheduled-task",
                    Value = TaskName,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "config-file",
                    Value = launcherPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "package-file",
                    Value = packagePath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "profile-path",
                    Value = profilePath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "process-match",
                    Value = supervisorPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "log-file",
                    Value = bootstrapLogPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "log-file",
                    Value = serverLogPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "log-file",
                    Value = tunnelLogPath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "tunnel-alias",
                    Value = tunnel.Alias,
                },
            ],
        };
    }
}

internal static class ManagedMcpTunnelConfigReader
{
    public static ManagedMcpTunnelRegistration Read(
        string configPath,
        string fallbackAlias)
    {
        if (!File.Exists(configPath))
        {
            return new ManagedMcpTunnelRegistration
            {
                Alias = fallbackAlias,
                ConfigPath = configPath,
            };
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;

            return new ManagedMcpTunnelRegistration
            {
                Alias = ReadString(root, "Alias") ?? fallbackAlias,
                TunnelId = ReadString(root, "TunnelId"),
                ConfigPath = configPath,
                StateRoot = ReadString(root, "StateRoot"),
            };
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                $"Managed MCP tunnel config could not be parsed: {configPath}",
                ex);
            return new ManagedMcpTunnelRegistration
            {
                Alias = fallbackAlias,
                ConfigPath = configPath,
            };
        }
    }

    private static string? ReadString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
