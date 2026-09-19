using System.Text.Json.Serialization;

namespace Talvora.Shared;

public static class ManagedMcpRegistryContract
{
    public const int CurrentSchemaVersion = 1;
    public const string ManagedBy = "Talvora";
    public const string OwnershipMarker = "talvora-managed-mcp/v1";
}

public sealed record ManagedMcpRegistryDocument
{
    public int SchemaVersion { get; init; } = ManagedMcpRegistryContract.CurrentSchemaVersion;
    public string ManagedBy { get; init; } = ManagedMcpRegistryContract.ManagedBy;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<ManagedMcpRegistration> Mcps { get; init; } = [];
}

public sealed record ManagedMcpRegistration
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public string OwnershipMarker { get; init; } = ManagedMcpRegistryContract.OwnershipMarker;
    public required string Endpoint { get; init; }
    public string? HealthEndpoint { get; init; }
    public bool AutoStart { get; init; } = true;
    public ManagedMcpTunnelRegistration? Tunnel { get; init; }
    public List<ManagedMcpComponentRegistration> Components { get; init; } = [];
    public List<ManagedMcpDiscoveryHint> DiscoveryHints { get; init; } = [];
}

public sealed record ManagedMcpTunnelRegistration
{
    public required string Alias { get; init; }
    public string? TunnelId { get; init; }
    public string? ConfigPath { get; init; }
    public string? StateRoot { get; init; }
    public bool Required { get; init; } = true;
}

public sealed record ManagedMcpComponentRegistration
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
    public string? Name { get; init; }
    public string? HealthEndpoint { get; init; }
    public bool Required { get; init; } = true;
}

public sealed record ManagedMcpDiscoveryHint
{
    public required string Kind { get; init; }
    public required string Value { get; init; }
}
