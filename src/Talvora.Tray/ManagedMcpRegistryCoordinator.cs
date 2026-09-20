using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ManagedMcpRegistryCoordinator
{
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);

    private static readonly HashSet<string> RetiredCanonicalMcpIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "playwright",
        };

    private static readonly IManagedMcpRecoveryDiscovery[] RecoveryDiscoveries =
    [
        new TalvoraManagedMcpRecoveryDiscovery(),
        new TalvoraFocusedManagedMcpRecoveryDiscovery(
            "talvora-dev",
            "Talvora Dev MCP",
            "Talvora'nın yazılım geliştirmeye odaklı MCP yüzeyi.",
            TalvoraConstants.McpDevUrl,
            "talvora-dev-business",
            "dev-business.json",
            ["talvora_apply_patch", "talvora_dotnet_build"]),
        new TalvoraFocusedManagedMcpRecoveryDiscovery(
            "talvora-admin",
            "Talvora Admin MCP",
            "Talvora'nın Windows ve sistem yönetimine odaklı MCP yüzeyi.",
            TalvoraConstants.McpAdminUrl,
            "talvora-admin-business",
            "admin-business.json",
            ["talvora_service_get", "talvora_registry_get"]),
        new GiteaManagedMcpRecoveryDiscovery(),

    ];

    public static async Task<ManagedMcpRegistryDocument> LoadOrRecoverAsync(
        CancellationToken cancellationToken)
    {
        await RecoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadOrRecoverCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            RecoveryGate.Release();
        }
    }

    public static async Task<ManagedMcpRegistryDocument> UpsertAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        await RecoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadOrRecoverCoreAsync(cancellationToken)
                .ConfigureAwait(false);

            var registrations = current.Mcps
                .Where(entry => !string.Equals(
                    entry.Id,
                    registration.Id,
                    StringComparison.OrdinalIgnoreCase))
                .Append(registration)
                .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var updated = current with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Mcps = registrations,
            };

            var path = ManagedMcpRegistryStore.GetCurrentUserPath();
            await ManagedMcpRegistryStore.WriteAsync(
                path,
                updated,
                createBackup: true,
                cancellationToken).ConfigureAwait(false);
            await ManagedMcpOwnershipManifestStore.PersistAsync(
                path,
                updated.Mcps,
                cancellationToken).ConfigureAwait(false);

            return updated;
        }
        finally
        {
            RecoveryGate.Release();
        }
    }

    private static async Task<ManagedMcpRegistryDocument> LoadOrRecoverCoreAsync(
        CancellationToken cancellationToken)
    {
        var path = ManagedMcpRegistryStore.GetCurrentUserPath();
        ManagedMcpRegistryDocument? existing = null;
        var primaryInvalid = false;
        var primaryAvailable = false;
        var backupAttempted = false;

        try
        {
            existing = await ManagedMcpRegistryStore.TryReadAsync(
                path,
                cancellationToken).ConfigureAwait(false);
            primaryAvailable = existing is not null;
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            JsonException or
            UnauthorizedAccessException)
        {
            primaryInvalid = true;
            backupAttempted = true;
            TrayLog.Write(
                "Managed MCP primary registry could not be read; validated backup will be attempted.",
                ex);

            try
            {
                existing = await ManagedMcpRegistryStore.TryReadBackupAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);

                if (existing is not null)
                {
                    TrayLog.Write(
                        $"Managed MCP registry backup recovered. Path={ManagedMcpRegistryStore.GetBackupPath(path)}");
                }
            }
            catch (Exception backupEx) when (
                backupEx is IOException or
                InvalidDataException or
                JsonException or
                UnauthorizedAccessException)
            {
                TrayLog.Write(
                    "Managed MCP registry backup could not be read; recovery discovery will rebuild canonical entries.",
                    backupEx);
            }
        }

        if (existing is null &&
            !backupAttempted)
        {
            backupAttempted = true;
            try
            {
                existing =
                    await ManagedMcpRegistryStore.TryReadBackupAsync(
                        path,
                        cancellationToken).ConfigureAwait(false);

                if (existing is not null)
                {
                    TrayLog.Write(
                        $"Managed MCP registry backup recovered after primary was missing. Path={ManagedMcpRegistryStore.GetBackupPath(path)}");
                }
            }
            catch (Exception backupEx) when (
                backupEx is IOException or
                InvalidDataException or
                JsonException or
                UnauthorizedAccessException)
            {
                TrayLog.Write(
                    "Managed MCP registry backup could not be read.",
                    backupEx);
            }
        }

        if (existing is null)
        {
            var manifests =
                await ManagedMcpRegistryStore
                    .TryReadRecoveryManifestsAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (manifests.Count > 0)
            {
                existing = new ManagedMcpRegistryDocument
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Mcps = manifests.ToList(),
                };

                TrayLog.Write(
                    $"Managed MCP per-entry recovery manifests loaded. Count={manifests.Count}");
            }
        }

        if (existing is null)
        {
            var ownershipEntries =
                await ManagedMcpOwnershipManifestStore.ReadAllAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
            if (ownershipEntries.Count > 0)
            {
                existing = new ManagedMcpRegistryDocument
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Mcps = ownershipEntries.ToList(),
                };
                TrayLog.Write(
                    $"Managed MCP ownership manifests recovered. Count={ownershipEntries.Count}");
            }
        }

        var discovered = RecoveryDiscoveries
            .Select(discovery => discovery.Discover())
            .Where(registration => registration is not null)
            .Cast<ManagedMcpRegistration>()
            .ToList();

        var discoveredIds = discovered
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var merged = new List<ManagedMcpRegistration>(discovered);
        if (existing is not null)
        {
            merged.AddRange(
                existing.Mcps.Where(entry =>
                    !discoveredIds.Contains(entry.Id) &&
                    !RetiredCanonicalMcpIds.Contains(entry.Id)));
        }

        merged = merged
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existing is not null &&
            RegistrationsEqual(existing.Mcps, merged))
        {
            await EnsurePrimaryAndCopiesAsync(
                path,
                existing,
                primaryAvailable,
                cancellationToken).ConfigureAwait(false);

            return existing;
        }

        var recovered = new ManagedMcpRegistryDocument
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Mcps = merged,
        };

        await ManagedMcpRegistryStore.WriteAsync(
            path,
            recovered,
            createBackup: !primaryInvalid && File.Exists(path),
            cancellationToken).ConfigureAwait(false);
        await ManagedMcpOwnershipManifestStore.PersistAsync(
            path,
            recovered.Mcps,
            cancellationToken).ConfigureAwait(false);

        TrayLog.Write(
            $"Managed MCP registry recovered. Count={recovered.Mcps.Count}; Path={path}");

        return recovered;
    }

    internal static async Task AssertPrimaryRefreshContractAsync(
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-ManagedMcpPrimary-" +
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "managed-mcps.json");
        var document = new ManagedMcpRegistryDocument
        {
            Mcps =
            [
                new ManagedMcpRegistration
                {
                    Id = "primary/probe",
                    DisplayName = "Primary Probe",
                    Description = "Registry primary refresh contract.",
                    Endpoint = "http://127.0.0.1:65532/mcp",
                    AutoStart = false,
                },
            ],
        };

        try
        {
            await EnsurePrimaryAndCopiesAsync(
                path,
                document,
                primaryAvailable: false,
                cancellationToken).ConfigureAwait(false);
            await AssertPrimaryMatchesAsync(
                path,
                document,
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                path,
                "{\"schemaVersion\":999}",
                cancellationToken);
            await EnsurePrimaryAndCopiesAsync(
                path,
                document,
                primaryAvailable: false,
                cancellationToken).ConfigureAwait(false);
            await AssertPrimaryMatchesAsync(
                path,
                document,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task EnsurePrimaryAndCopiesAsync(
        string path,
        ManagedMcpRegistryDocument document,
        bool primaryAvailable,
        CancellationToken cancellationToken)
    {
        if (!primaryAvailable)
        {
            await ManagedMcpRegistryStore.WriteAsync(
                path,
                document,
                createBackup: false,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ManagedMcpRegistryStore.EnsureRecoveryManifestsAsync(
                path,
                document.Mcps,
                cancellationToken).ConfigureAwait(false);
        }

        if (ManagedMcpOwnershipManifestStore.NeedsSeed(
                path,
                document.Mcps))
        {
            await ManagedMcpOwnershipManifestStore.PersistAsync(
                path,
                document.Mcps,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AssertPrimaryMatchesAsync(
        string path,
        ManagedMcpRegistryDocument expected,
        CancellationToken cancellationToken)
    {
        var actual = await ManagedMcpRegistryStore.TryReadAsync(
            path,
            cancellationToken).ConfigureAwait(false);
        if (actual is null ||
            !RegistrationsEqual(expected.Mcps, actual.Mcps))
        {
            throw new InvalidOperationException(
                "Managed MCP primary registry refresh contract failed.");
        }
    }

    private static bool RegistrationsEqual(
        IReadOnlyList<ManagedMcpRegistration> left,
        IReadOnlyList<ManagedMcpRegistration> right)
    {
        var options = ManagedMcpRegistryStore.JsonOptions;
        return string.Equals(
            JsonSerializer.Serialize(left, options),
            JsonSerializer.Serialize(right, options),
            StringComparison.Ordinal);
    }
}