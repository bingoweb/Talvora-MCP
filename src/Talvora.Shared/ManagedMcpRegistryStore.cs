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
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "managed-mcps.json");

    public static string GetBackupPath(string path) =>
        Path.GetFullPath(path) + ".bak";

    public static string GetRecoveryManifestDirectory(
        string path) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(path))
                ?? throw new InvalidOperationException(
                    "Managed MCP registry directory could not be resolved."),
            "managed-mcp-recovery");

    public static async Task<ManagedMcpRegistryDocument?> TryReadBackupAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await TryReadAsync(
            GetBackupPath(path),
            cancellationToken).ConfigureAwait(false);

    public static async Task EnsureRecoveryManifestsAsync(
        string path,
        IReadOnlyCollection<ManagedMcpRegistration> registrations,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteRecoveryManifestsAsync(
                path,
                registrations,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<IReadOnlyList<ManagedMcpRegistration>>
        TryReadRecoveryManifestsAsync(
            string path,
            CancellationToken cancellationToken = default)
    {
        var directory = GetRecoveryManifestDirectory(path);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ManagedMcpRegistration>();
        }

        var registrations = new List<ManagedMcpRegistration>();

        foreach (var manifest in Directory.EnumerateFiles(
            directory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var registration =
                    await JsonFileStore.ReadAsync<ManagedMcpRegistration>(
                        manifest,
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);

                ValidateRegistration(registration);
                registrations.Add(registration);
            }
            catch (Exception ex) when (
                ex is IOException or
                JsonException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                // Per-entry recovery is intentionally independent: one broken
                // manifest must not discard the remaining valid registrations.
            }
        }

        return registrations
            .GroupBy(
                entry => entry.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(
                entry => entry.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<ManagedMcpRegistryDocument?> TryReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var document =
            await JsonFileStore.ReadAsync<ManagedMcpRegistryDocument>(
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

            await WriteRecoveryManifestsAsync(
                path,
                document.Mcps,
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
                document =
                    await JsonFileStore.ReadAsync<ManagedMcpRegistryDocument>(
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
                .OrderBy(
                    entry => entry.Id,
                    StringComparer.OrdinalIgnoreCase)
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

            await WriteRecoveryManifestsAsync(
                path,
                updated.Mcps,
                cancellationToken).ConfigureAwait(false);

            return updated;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task AssertRecoveryContractAsync(
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-ManagedMcpRegistry-" +
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(
            root,
            "managed-mcps.json");

        try
        {
            var first = new ManagedMcpRegistration
            {
                Id = "self-test-generic",
                DisplayName = "Self Test Generic",
                Description = "Registry recovery contract probe.",
                Endpoint = "http://127.0.0.1:65530/mcp",
                AutoStart = false,
            };

            var second = first with
            {
                Description =
                    "Registry recovery contract probe updated.",
            };

            await WriteAsync(
                path,
                new ManagedMcpRegistryDocument
                {
                    Mcps = [first],
                },
                createBackup: false,
                cancellationToken).ConfigureAwait(false);

            await WriteAsync(
                path,
                new ManagedMcpRegistryDocument
                {
                    Mcps = [second],
                },
                createBackup: true,
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                path,
                "{broken",
                cancellationToken);

            var backup = await TryReadBackupAsync(
                path,
                cancellationToken).ConfigureAwait(false);
            if (backup is null ||
                backup.Mcps.Count != 1 ||
                !string.Equals(
                    backup.Mcps[0].Id,
                    first.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Managed MCP registry validated backup recovery contract failed.");
            }

            File.Delete(path);
            File.Delete(GetBackupPath(path));

            var manifests =
                await TryReadRecoveryManifestsAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);

            var recovered = manifests.SingleOrDefault(entry =>
                string.Equals(
                    entry.Id,
                    second.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (recovered is null ||
                !string.Equals(
                    recovered.Description,
                    second.Description,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Managed MCP per-entry recovery manifest contract failed.");
            }

            var collisionOne = first with
            {
                Id = "foo/bar",
                DisplayName = "Collision One",
            };
            var collisionTwo = first with
            {
                Id = "foo?bar",
                DisplayName = "Collision Two",
            };
            await WriteAsync(
                path,
                new ManagedMcpRegistryDocument
                {
                    Mcps = [collisionOne, collisionTwo],
                },
                createBackup: false,
                cancellationToken).ConfigureAwait(false);
            File.Delete(path);
            if (File.Exists(GetBackupPath(path)))
            {
                File.Delete(GetBackupPath(path));
            }

            var collisionRecovered =
                await TryReadRecoveryManifestsAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
            if (collisionRecovered.Count != 2 ||
                !collisionRecovered.Any(entry =>
                    string.Equals(
                        entry.Id,
                        collisionOne.Id,
                        StringComparison.Ordinal)) ||
                !collisionRecovered.Any(entry =>
                    string.Equals(
                        entry.Id,
                        collisionTwo.Id,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Managed MCP collision-proof recovery manifest identity contract failed.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(
                        root,
                        recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
            }
        }
    }

    public static void ValidateDocument(
        ManagedMcpRegistryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion !=
            ManagedMcpRegistryContract.CurrentSchemaVersion)
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

        var seenIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

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

    public static void ValidateRegistration(
        ManagedMcpRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            registration.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            registration.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            registration.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            registration.Endpoint);

        if (!string.Equals(
                registration.OwnershipMarker,
                ManagedMcpRegistryContract.OwnershipMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' has an invalid ownership marker.");
        }

        if (!Uri.TryCreate(
                registration.Endpoint,
                UriKind.Absolute,
                out _))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' endpoint is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(
                registration.HealthEndpoint) &&
            !Uri.TryCreate(
                registration.HealthEndpoint,
                UriKind.Absolute,
                out _))
        {
            throw new InvalidDataException(
                $"Managed MCP '{registration.Id}' health endpoint is invalid.");
        }
    }

    private static async Task WriteRecoveryManifestsAsync(
        string registryPath,
        IReadOnlyCollection<ManagedMcpRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var directory =
            GetRecoveryManifestDirectory(registryPath);
        Directory.CreateDirectory(directory);

        var expected =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRegistration(registration);

            var storageKey =
                ManagedMcpIdentityKey.Create(
                    registration.Id);

            var manifestPath =
                Path.Combine(
                    directory,
                    storageKey + ".json");

            expected.Add(
                Path.GetFullPath(manifestPath));

            await JsonFileStore.WriteAsync(
                manifestPath,
                registration,
                JsonOptions,
                createBackup: false,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var stale in Directory.EnumerateFiles(
            directory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            if (expected.Contains(Path.GetFullPath(stale)))
            {
                continue;
            }

            try
            {
                File.Delete(stale);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
            }
        }
    }
}
