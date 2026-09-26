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

internal sealed class TalvoraFocusedManagedMcpRecoveryDiscovery(
    string id,
    string displayName,
    string description,
    string endpoint,
    string tunnelAlias,
    string configFileName,
    IReadOnlyList<string> requiredTools)
    : IManagedMcpRecoveryDiscovery
{
    public string Id => id;

    public ManagedMcpRegistration Discover()
    {
        var tunnelPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "TunnelClient",
            configFileName);
        var tunnel = ManagedMcpTunnelConfigReader.Read(
            tunnelPath,
            tunnelAlias);

        return new ManagedMcpRegistration
        {
            Id = Id,
            DisplayName = displayName,
            Description = description,
            Endpoint = endpoint,
            HealthEndpoint = TalvoraConstants.HealthUrl,
            AutoStart = true,
            ProtocolProbe = new ManagedMcpProtocolProbeRegistration
            {
                RequiredTools = requiredTools.ToList(),
            },
            Tunnel = tunnel,
            Components =
            [
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
            ProtocolProbe = new ManagedMcpProtocolProbeRegistration
            {
                RequiredTools =
                [
                    "get_gitea_mcp_server_version",
                    "get_me",
                ],
            },
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

internal sealed class PenpotManagedMcpRecoveryDiscovery : IManagedMcpRecoveryDiscovery
{
    public string Id => "penpot";

    public ManagedMcpRegistration? Discover()
    {
        var programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var composePath = Path.Combine(
            programData,
            "Talvora",
            "Penpot",
            "docker-compose.yaml");
        var localMcpRoot = Path.Combine(
            programData,
            "Talvora",
            "Penpot",
            "penpot-2.18.0",
            "mcp");

        if (!File.Exists(composePath))
        {
            return null;
        }

        return new ManagedMcpRegistration
        {
            Id = Id,
            DisplayName = "Penpot",
            Description = "Yerel Penpot tasarım platformu ve resmi Penpot MCP sunucusu.",
            Endpoint = "http://127.0.0.1:4401/mcp",
            HealthEndpoint = "http://127.0.0.1:9001/",
            AutoStart = true,
            ProtocolProbe = new ManagedMcpProtocolProbeRegistration
            {
                RequiredTools =
                [
                    "high_level_overview",
                    "execute_code",
                    "penpot_api_info",
                    "export_shape",
                    "import_image",
                ],
            },
            Components =
            [
                new ManagedMcpComponentRegistration
                {
                    Id = "local-mcp-task",
                    DisplayName = "Penpot local MCP",
                    Kind = "scheduled-task",
                    Name = "Talvora Penpot MCP",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "mcp",
                    DisplayName = "Penpot MCP",
                    Kind = "mcp-protocol",
                    HealthEndpoint = "http://127.0.0.1:4401/mcp",
                },
                new ManagedMcpComponentRegistration
                {
                    Id = "web",
                    DisplayName = "Penpot arayüzü",
                    Kind = "http",
                    HealthEndpoint = "http://127.0.0.1:9001/",
                },
            ],
            DiscoveryHints =
            [
                new ManagedMcpDiscoveryHint
                {
                    Kind = "config-file",
                    Value = composePath,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "scheduled-task",
                    Value = "Talvora Penpot MCP",
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "process-match",
                    Value = localMcpRoot,
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "tcp-endpoint",
                    Value = "127.0.0.1:4401",
                },
                new ManagedMcpDiscoveryHint
                {
                    Kind = "web-endpoint",
                    Value = "http://127.0.0.1:9001/",
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
