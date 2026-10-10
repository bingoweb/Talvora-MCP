using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record HostingerMcpEntrypoint(
    string Name,
    string? Title,
    string? Description,
    string InputSchemaJson,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent);

public sealed record HostingerMcpStatusResponse(
    bool Ready,
    string Version,
    bool CredentialConfigured,
    int ToolCount,
    IReadOnlyList<HostingerMcpEntrypoint> Entrypoints,
    string? Error);

[McpServerToolType]
public static class HostingerMcpTools
{
    private const int MaxArgumentsJsonCharacters = 4 * 1024 * 1024;
    private const int MaximumCredentialBytes = 64 * 1024;
    private const int MaximumPackageMetadataBytes = 256 * 1024;

    [McpServerTool(
        Name = "talvora_hostinger_mcp_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(HostingerMcpStatusResponse)),
     Description("Start Talvora's managed official Hostinger API MCP server, verify the locally configured credential without exposing it, and return the live Hostinger MCP tool schemas. Use this before Hostinger VPS, domain, DNS, hosting, billing, or other Hostinger operations.")]
    public static async Task<HostingerMcpStatusResponse> Status(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runtime = ResolveRuntime();
            var token = ReadToken(runtime.TokenPath);
            var transport = CreateTransport(runtime.Node, runtime.ServerScript, runtime.WorkingDirectory, token);
            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(
                cancellationToken: cancellationToken);

            return new HostingerMcpStatusResponse(
                true,
                ReadPackageVersion(runtime.PackageJson),
                true,
                tools.Count,
                tools.Select(tool => new HostingerMcpEntrypoint(
                        tool.Name,
                        tool.ProtocolTool.Title,
                        tool.Description,
                        tool.ProtocolTool.InputSchema.GetRawText(),
                        tool.ProtocolTool.Annotations?.ReadOnlyHint,
                        tool.ProtocolTool.Annotations?.DestructiveHint,
                        tool.ProtocolTool.Annotations?.IdempotentHint))
                    .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                    .ToArray(),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is McpException or
            InvalidDataException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            TimeoutException or
            JsonException)
        {
            return new HostingerMcpStatusResponse(
                false,
                "unknown",
                CredentialConfigured(),
                0,
                [],
                FileLog.RedactSensitiveData(ex.Message));
        }
    }

    [McpServerTool(
        Name = "talvora_hostinger_mcp_call_tool",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Call a live tool exposed by the official Hostinger API MCP server. Discover the current Hostinger tool names and JSON schemas with talvora_hostinger_mcp_status first. This provides direct Hostinger VPS, Domains/DNS, hosting, billing, ecommerce, and related capabilities while keeping the API token outside MCP responses.")]
    public static async Task<CallToolResult> CallTool(
        [Description("Hostinger MCP tool name discovered from talvora_hostinger_mcp_status.")] string name,
        [Description("JSON object matching the live Hostinger MCP tool input schema. Defaults to {}.")] string argumentsJson = "{}",
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var arguments = ParseArguments(argumentsJson);
        var runtime = ResolveRuntime();
        var token = ReadToken(runtime.TokenPath);
        var transport = CreateTransport(runtime.Node, runtime.ServerScript, runtime.WorkingDirectory, token);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);
        if (!tools.Any(tool =>
                string.Equals(
                    tool.Name,
                    name.Trim(),
                    StringComparison.Ordinal)))
        {
            throw new McpProtocolException(
                $"Unknown Hostinger MCP tool: {name.Trim()}",
                McpErrorCode.InvalidParams);
        }

        return await client.CallToolAsync(
            name.Trim(),
            arguments,
            cancellationToken: cancellationToken);
    }

    private static StdioClientTransport CreateTransport(
        string node,
        string serverScript,
        string workingDirectory,
        string token)
    {
        var env = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        env["API_TOKEN"] = token;
        env["DEBUG"] = "false";
        env["NO_COLOR"] = "1";
        env["FORCE_COLOR"] = "0";

        return new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "Hostinger API",
                Command = node,
                Arguments = [serverScript],
                WorkingDirectory = workingDirectory,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = env,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            });
    }

    private static HostingerRuntime ResolveRuntime()
    {
        var node = CommandResolver.Resolve(
            ["node.exe", "node"],
            [@"C:\Program Files\nodejs\node.exe"])
            ?? throw new FileNotFoundException(
                "Node.js was not found for Hostinger MCP.");

        var commonData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var root = Path.Combine(
            commonData,
            "Talvora",
            "Tools",
            "Hostinger");
        var packageRoot = Path.Combine(
            root,
            "node_modules",
            "hostinger-api-mcp");
        var serverScript = Path.Combine(
            packageRoot,
            "src",
            "servers",
            "all.js");
        var packageJson = Path.Combine(packageRoot, "package.json");
        if (!File.Exists(serverScript) || !File.Exists(packageJson))
        {
            throw new FileNotFoundException(
                "Hostinger API MCP was not found in Talvora's managed tools directory. Install hostinger-api-mcp into C:\\ProgramData\\Talvora\\Tools\\Hostinger.");
        }

        var tokenPath = Path.Combine(
            commonData,
            "Talvora",
            "Secrets",
            "hostinger-api-token.txt");
        return new HostingerRuntime(
            node,
            serverScript,
            packageJson,
            root,
            tokenPath);
    }

    private static bool CredentialConfigured()
    {
        try
        {
            var configured = Environment.GetEnvironmentVariable(
                "TALVORA_HOSTINGER_API_TOKEN");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return true;
            }

            var commonData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            var path = Path.Combine(
                commonData,
                "Talvora",
                "Secrets",
                "hostinger-api-token.txt");
            return File.Exists(path) &&
                !string.IsNullOrWhiteSpace(
                    TextFileStore.ReadBounded(path, MaximumCredentialBytes));
        }
        catch
        {
            return false;
        }
    }

    private static string ReadToken(string tokenPath)
    {
        var configured = Environment.GetEnvironmentVariable(
            "TALVORA_HOSTINGER_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (!File.Exists(tokenPath))
        {
            throw new FileNotFoundException(
                "Hostinger API credential is not configured for Talvora.",
                tokenPath);
        }

        var token = TextFileStore.ReadBounded(
            tokenPath, MaximumCredentialBytes).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Hostinger API credential file is empty.");
        }

        return token;
    }

    private static string ReadPackageVersion(string packageJson)
    {
        using var document = JsonFileStore.ReadBounded<JsonDocument>(
            packageJson, MaximumPackageMetadataBytes);
        return document.RootElement.TryGetProperty(
                "version",
                out var version) &&
            version.ValueKind == JsonValueKind.String
                ? version.GetString() ?? "unknown"
                : "unknown";
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpProtocolException(
                "name must not be empty.",
                McpErrorCode.InvalidParams);
        }

        if (value.Length > 256)
        {
            throw new McpProtocolException(
                "name is too long.",
                McpErrorCode.InvalidParams);
        }
    }

    private static Dictionary<string, object?> ParseArguments(
        string argumentsJson)
    {
        if (argumentsJson is null)
        {
            throw new McpProtocolException(
                "argumentsJson must not be null.",
                McpErrorCode.InvalidParams);
        }

        if (argumentsJson.Length > MaxArgumentsJsonCharacters)
        {
            throw new McpProtocolException(
                $"argumentsJson exceeds the {MaxArgumentsJsonCharacters} character limit.",
                McpErrorCode.InvalidParams);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    argumentsJson)
                ?? [];
        }
        catch (JsonException ex)
        {
            throw new McpProtocolException(
                $"argumentsJson must be a valid JSON object: {ex.Message}",
                McpErrorCode.InvalidParams);
        }
    }

    private sealed record HostingerRuntime(
        string Node,
        string ServerScript,
        string PackageJson,
        string WorkingDirectory,
        string TokenPath);
}
