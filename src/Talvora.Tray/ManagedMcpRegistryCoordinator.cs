using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ManagedMcpRegistryCoordinator
{
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);

    private static readonly IManagedMcpRecoveryDiscovery[] RecoveryDiscoveries =
    [
        new TalvoraManagedMcpRecoveryDiscovery(),
        new GiteaManagedMcpRecoveryDiscovery(),
        new PlaywrightManagedMcpRecoveryDiscovery(),
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
        var backupAttempted = false;

        try
        {
            existing = await ManagedMcpRegistryStore.TryReadAsync(
                path,
                cancellationToken).ConfigureAwait(false);
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
                existing.Mcps.Where(entry => !discoveredIds.Contains(entry.Id)));
        }

        merged = merged
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existing is not null &&
            RegistrationsEqual(existing.Mcps, merged))
        {
            if (ManagedMcpOwnershipManifestStore.NeedsSeed(
                    path,
                    existing.Mcps))
            {
                await ManagedMcpOwnershipManifestStore.PersistAsync(
                    path,
                    existing.Mcps,
                    cancellationToken).ConfigureAwait(false);
            }

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