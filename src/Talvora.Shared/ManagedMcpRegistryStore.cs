using System.Text.Json;

namespace Talvora.Shared;

public static class ManagedMcpRegistryStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string GetCurrentUserPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "managed-mcps.json");

    public static async Task<ManagedMcpRegistryDocument?> TryReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var document = await JsonFileStore.ReadAsync<ManagedMcpRegistryDocument>(
            path,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        ValidateDocument(document);
        return document;
    }

    public static async Task WriteAsync(
        string path,
        ManagedMcpRegistryDocument document,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        ValidateDocument(document);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonFileStore.WriteAsync(
                path,
                document,
                JsonOptions,
                createBackup,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<ManagedMcpRegistryDocument> UpsertAsync(
        string path,
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ValidateRegistration(registration);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ManagedMcpRegistryDocument document;
            if (File.Exists(path))
            {
                document = await JsonFileStore.ReadAsync<ManagedMcpRegistryDocument>(
                    path,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                ValidateDocument(document);
            }
            else
            {
                document = new ManagedMcpRegistryDocument();
            }

            var entries = document.Mcps
                .Where(entry => !string.Equals(
                    entry.Id,
                    registration.Id,
                    StringComparison.OrdinalIgnoreCase))
                .Append(registration)
                .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var updated = document with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Mcps = entries,
            };

            await JsonFileStore.WriteAsync(
                path,
                updated,
                JsonOptions,
                createBackup: File.Exists(path),
                cancellationToken).ConfigureAwait(false);

            return updated;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void ValidateDocument(ManagedMcpRegistryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != ManagedMcpRegistryContract.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported managed MCP registry schema: {document.SchemaVersion}.");
        }

        if (!string.Equals(
                document.ManagedBy,
                ManagedMcpRegistryContract.ManagedBy,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected managed MCP registry owner: {document.ManagedBy}.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in document.Mcps)
        {
            ValidateRegistration(registration);
            if (!seenIds.Add(registration.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate managed MCP id: {registration.Id}.");
            }
        }
    }

    public static void ValidateRegistration(ManagedMcpRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Endpoint);

        if (!string.Equals(
                registration.OwnershipMarker,
                ManagedMcpRegistryContract.OwnershipMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' has an invalid ownership marker.");
        }

        if (!Uri.TryCreate(registration.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' endpoint is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(registration.HealthEndpoint) &&
            !Uri.TryCreate(registration.HealthEndpoint, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' health endpoint is invalid.");
        }
    }
}
