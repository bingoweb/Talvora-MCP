using System.IO;
using System.Net.Http;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record TalvoraStorageMaintenanceResult(
    int DeletedEntries,
    long ReclaimedBytes,
    int RotatedTunnelLogs,
    IReadOnlyList<string> Notes);

internal static class TalvoraStorageMaintenanceService
{
    private const int MaximumVersionEntriesPerRun = 10_000;

    internal const long TunnelLogRotationBytes =
        32L * 1024 * 1024;

    internal static readonly TimeSpan TunnelQuietPeriod =
        TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan TemporaryArtifactRetention =
        TimeSpan.FromDays(7);

    internal static readonly TimeSpan TestArtifactRetention =
        TimeSpan.FromDays(2);

    private static readonly TimeSpan TunnelRotationStabilityDelay =
        TimeSpan.FromSeconds(2);

    private const string InPlaceRotationVerifiedRuntimeVersion =
        "0.0.15";

    private static readonly JsonSerializerOptions StorageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static void AssertPolicyContract()
    {
        if (TunnelLogRotationBytes != 32L * 1024 * 1024 ||
            TunnelQuietPeriod != TimeSpan.FromMinutes(5) ||
            TemporaryArtifactRetention != TimeSpan.FromDays(7) ||
            TestArtifactRetention != TimeSpan.FromDays(2))
        {
            throw new InvalidOperationException(
                "Storage maintenance retention/rotation contract failed.");
        }

        if (!TalvoraOwnedTempCleanup.IsOwnedTempName(
                "Talvora-Deploy-deadbeef") ||
            !TalvoraOwnedTempCleanup.IsOwnedTempName(
                "TalvoraReparseAudit") ||
            !TalvoraOwnedTempCleanup.IsOwnedTempName(
                "Talvora-Reset-And-Install.ps1") ||
            TalvoraOwnedTempCleanup.IsOwnedTempName(
                "unrelated-user-data"))
        {
            throw new InvalidOperationException(
                "Storage maintenance temporary-artifact allowlist contract failed.");
        }

        var now = DateTimeOffset.UtcNow;
        var quiet = new ManagedMcpTunnelHealthSnapshot(
            SchemaVersion: 1,
            Live: true,
            Ready: true,
            RuntimeVersion: "0.0.15",
            RuntimeLifecycle: "running",
            Components:
            [
                new ManagedMcpTunnelHealthComponentSnapshot(
                    "queue",
                    "ok",
                    "idle",
                    null,
                    false,
                    string.Empty,
                    false),
                new ManagedMcpTunnelHealthComponentSnapshot(
                    "dispatcher",
                    "ok",
                    "idle",
                    null,
                    false,
                    string.Empty,
                    false),
                new ManagedMcpTunnelHealthComponentSnapshot(
                    "response-delivery",
                    "ok",
                    "idle",
                    null,
                    false,
                    string.Empty,
                    false),
            ],
            QueueDepth: 0,
            DispatcherActive: 0,
            ResponseInProgress: 0,
            LastActivityUtc: now.Subtract(
                TunnelQuietPeriod + TimeSpan.FromSeconds(1)));
        var active = quiet with
        {
            QueueDepth = 1,
        };

        if (!quiet.IsQuietForMaintenance(
                now,
                TunnelQuietPeriod) ||
            active.IsQuietForMaintenance(
                now,
                TunnelQuietPeriod))
        {
            throw new InvalidOperationException(
                "Storage maintenance tunnel-idle contract failed.");
        }
    }

    public static async Task<TalvoraStorageMaintenanceResult> RunAsync(
        IReadOnlyList<ManagedMcpRegistration> registrations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        using var maintenanceLease =
            ManagedMcpOperationCoordinator.TryAcquire(
                "storage-maintenance");
        if (maintenanceLease is null)
        {
            return new TalvoraStorageMaintenanceResult(
                0,
                0,
                0,
                ["Bakım zaten çalışıyor."]);
        }

        var registrationSnapshot = registrations.ToArray();
        var notes = new List<string>();
        var cleanup = await Task.Run(
            () => CleanupStaleArtifacts(
                DateTimeOffset.UtcNow,
                registrationSnapshot,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var rotatedLogs = 0;
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (registration.Tunnel is not { Required: true } ||
                ManagedMcpSessionState.IsManuallyStopped(
                    registration.Id))
            {
                continue;
            }

            try
            {
                if (await TryRotateTunnelLogAsync(
                        registration,
                        cancellationToken).ConfigureAwait(false))
                {
                    rotatedLogs++;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                TrayLog.Write(
                    $"Storage maintenance tunnel health/rotation timed out and was deferred. MCP={registration.Id}",
                    ex);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                JsonException or
                HttpRequestException)
            {
                TrayLog.Write(
                    $"Storage maintenance tunnel log rotation deferred. MCP={registration.Id}",
                    ex);
            }
        }

        if (cleanup.DeletedEntries > 0 ||
            rotatedLogs > 0)
        {
            notes.Add(
                $"Deleted={cleanup.DeletedEntries}; " +
                $"ReclaimedBytes={cleanup.ReclaimedBytes}; " +
                $"RotatedTunnelLogs={rotatedLogs}");
        }

        return new TalvoraStorageMaintenanceResult(
            cleanup.DeletedEntries,
            cleanup.ReclaimedBytes,
            rotatedLogs,
            notes);
    }

    private static CleanupResult CleanupStaleArtifacts(
        DateTimeOffset nowUtc,
        IReadOnlyList<ManagedMcpRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        long reclaimedBytes = 0;

        var tempRoot = Path.GetFullPath(
            Path.GetTempPath());

        if (Directory.Exists(tempRoot))
        {
            var testResult =
                TalvoraOwnedTempCleanup.CleanupTopLevel(
                    tempRoot,
                    nowUtc - TestArtifactRetention,
                    TalvoraOwnedTempCleanup.TestPrefixes,
                    Array.Empty<string>(),
                    cancellationToken);
            deleted += testResult.DeletedEntries;
            reclaimedBytes += testResult.ReclaimedBytes;

            var result =
                TalvoraOwnedTempCleanup.CleanupTopLevel(
                    tempRoot,
                    nowUtc - TemporaryArtifactRetention,
                    TalvoraOwnedTempCleanup.DefaultPrefixes,
                    TalvoraOwnedTempCleanup.DefaultExactNames,
                    cancellationToken);
            deleted += result.DeletedEntries;
            reclaimedBytes += result.ReclaimedBytes;

            if (testResult.ScanLimitReached ||
                result.ScanLimitReached)
            {
                TrayLog.Write(
                    $"Storage maintenance reached its bounded temp scan limit. Root={tempRoot}");
            }

            var nestedCleanup =
                CleanupNestedTalvoraTempRoots(
                    tempRoot,
                    nowUtc,
                    cancellationToken);
            deleted += nestedCleanup.DeletedEntries;
            reclaimedBytes += nestedCleanup.ReclaimedBytes;
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var versionsRoot = Path.Combine(
                localAppData,
                "Talvora",
                "TunnelClient",
                "versions");

            if (Directory.Exists(versionsRoot))
            {
                using var versionPruneLeases =
                    TryAcquireVersionPruneLeases(
                        registrations);
                if (versionPruneLeases is null)
                {
                    TrayLog.Write(
                        "Tunnel-client version maintenance deferred because a lifecycle/update operation is active.");
                    return new CleanupResult(
                        deleted,
                        reclaimedBytes);
                }

                var stagingDirectories = Directory
                    .EnumerateDirectories(
                        versionsRoot,
                        "*.stage.*",
                        SearchOption.TopDirectoryOnly)
                    .Take(MaximumVersionEntriesPerRun + 1)
                    .ToArray();
                if (stagingDirectories.Length >
                    MaximumVersionEntriesPerRun)
                {
                    TrayLog.Write(
                        $"Tunnel-client staging cleanup reached its bounded scan limit. Root={versionsRoot}; Limit={MaximumVersionEntriesPerRun}");
                }

                foreach (var staging in stagingDirectories
                             .Take(MaximumVersionEntriesPerRun))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                            versionsRoot,
                            staging,
                            nowUtc - TimeSpan.FromDays(1),
                            out var bytes,
                            cancellationToken))
                    {
                        deleted++;
                        reclaimedBytes += bytes;
                    }
                }

                var versionCleanup =
                    PruneObsoleteTunnelClientVersions(
                        versionsRoot,
                        registrations,
                        nowUtc,
                        cancellationToken);
                deleted += versionCleanup.DeletedEntries;
                reclaimedBytes += versionCleanup.ReclaimedBytes;
            }
        }

        return new CleanupResult(
            deleted,
            reclaimedBytes);
    }

    private static CleanupResult CleanupNestedTalvoraTempRoots(
        string tempRoot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var talvoraTempRoot =
            Path.Combine(
                tempRoot,
                "Talvora");
        if (!Directory.Exists(talvoraTempRoot))
        {
            return new CleanupResult(0, 0);
        }

        var result =
            TalvoraOwnedTempCleanup.CleanupTopLevel(
                talvoraTempRoot,
                nowUtc - TemporaryArtifactRetention,
                Array.Empty<string>(),
                ["Structural", "semantic-worker"],
                cancellationToken);
        if (result.ScanLimitReached)
        {
            TrayLog.Write(
                $"Storage maintenance reached its nested Talvora temp scan limit. Root={talvoraTempRoot}");
        }

        return new CleanupResult(
            result.DeletedEntries,
            result.ReclaimedBytes);
    }

    private static CleanupResult PruneObsoleteTunnelClientVersions(
        string versionsRoot,
        IReadOnlyList<ManagedMcpRegistration> registrations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var retainedDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var clientRoot = Path.GetFullPath(
            Path.GetDirectoryName(versionsRoot)
            ?? throw new InvalidOperationException(
                "Tunnel-client versions root has no parent directory."));

        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configPath = registration.Tunnel?.ConfigPath;
            if (string.IsNullOrWhiteSpace(configPath))
            {
                continue;
            }

            string fullConfigPath;
            try
            {
                fullConfigPath = Path.GetFullPath(configPath);
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException)
            {
                TrayLog.Write(
                    $"Tunnel-client version pruning deferred because an active config path is invalid. MCP={registration.Id}",
                    ex);
                return new CleanupResult(0, 0);
            }

            if (!TalvoraOwnedTempCleanup.IsPathUnderRoot(
                    fullConfigPath,
                    clientRoot))
            {
                continue;
            }

            if (!File.Exists(fullConfigPath))
            {
                TrayLog.Write(
                    $"Tunnel-client version pruning deferred because an owned active config is missing. MCP={registration.Id}; Config={fullConfigPath}");
                return new CleanupResult(0, 0);
            }

            try
            {
                var config = JsonSerializer.Deserialize<BusinessConfig>(
                    File.ReadAllText(fullConfigPath),
                    StorageJsonOptions);
                var executable = config?.TunnelClient;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    TrayLog.Write(
                        $"Tunnel-client version pruning deferred because an owned active config has no client path. MCP={registration.Id}; Config={fullConfigPath}");
                    return new CleanupResult(0, 0);
                }

                var versionDirectory =
                    ResolveReferencedVersionDirectory(
                        executable,
                        versionsRoot);
                if (!string.IsNullOrWhiteSpace(versionDirectory))
                {
                    retainedDirectories.Add(
                        versionDirectory);
                }

                foreach (var protectedDirectory in
                         ManagedMcpTunnelProvisioningService
                             .GetClientUpdateProtectedVersionDirectories(
                                 fullConfigPath,
                                 versionsRoot))
                {
                    retainedDirectories.Add(
                        protectedDirectory);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException or
                ArgumentException or
                NotSupportedException)
            {
                TrayLog.Write(
                    $"Tunnel-client version pruning deferred because an owned active config is unreadable. MCP={registration.Id}",
                    ex);
                return new CleanupResult(0, 0);
            }
        }

        var enumeratedVersionEntries = Directory
            .EnumerateDirectories(
                versionsRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Take(MaximumVersionEntriesPerRun + 1)
            .ToArray();

        if (enumeratedVersionEntries.Length >
            MaximumVersionEntriesPerRun)
        {
            TrayLog.Write(
                $"Tunnel-client version pruning reached its bounded scan limit and was deferred. Root={versionsRoot}; Limit={MaximumVersionEntriesPerRun}");
            return new CleanupResult(0, 0);
        }

        var versionDirectories =
            enumeratedVersionEntries
            .Where(path =>
                !Path.GetFileName(path).Contains(
                    ".stage.",
                    StringComparison.OrdinalIgnoreCase))
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToArray();

        var rollbackCandidates =
            versionDirectories
                .Where(directory =>
                    !retainedDirectories.Contains(
                        Path.GetFullPath(
                            directory.FullName)))
                .Take(2)
                .ToArray();
        foreach (var recent in rollbackCandidates)
        {
            retainedDirectories.Add(
                Path.GetFullPath(recent.FullName));
        }

        var deleted = 0;
        long reclaimedBytes = 0;
        foreach (var directory in versionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(
                directory.FullName);
            if (retainedDirectories.Contains(fullPath))
            {
                continue;
            }

            if (TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                    versionsRoot,
                    fullPath,
                    nowUtc - TimeSpan.FromDays(1),
                    out var bytes,
                    cancellationToken))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new CleanupResult(
            deleted,
            reclaimedBytes);
    }

    private static string? ResolveReferencedVersionDirectory(
        string executable,
        string versionsRoot)
    {
        var fullExecutable = Path.GetFullPath(executable);
        var fullVersionsRoot = Path.GetFullPath(versionsRoot);
        if (!TalvoraOwnedTempCleanup.IsPathUnderRoot(
                fullExecutable,
                fullVersionsRoot))
        {
            return null;
        }

        var relative = Path.GetRelativePath(
            fullVersionsRoot,
            fullExecutable);
        var segments = relative.Split(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar,
            ],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2 ||
            string.Equals(
                segments[0],
                "..",
                StringComparison.Ordinal))
        {
            return null;
        }

        return Path.GetFullPath(
            Path.Combine(
                fullVersionsRoot,
                segments[0]));
    }

    private static async Task<bool> TryRotateTunnelLogAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var tunnel = registration.Tunnel;
        if (tunnel is null ||
            string.IsNullOrWhiteSpace(tunnel.StateRoot) ||
            string.IsNullOrWhiteSpace(tunnel.Alias))
        {
            return false;
        }

        if (tunnel.Alias.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var logRoot = Path.GetFullPath(
            Path.Combine(
                tunnel.StateRoot,
                "logs"));
        var logPath = Path.GetFullPath(
            Path.Combine(
                logRoot,
                tunnel.Alias + ".log"));

        if (!TalvoraOwnedTempCleanup.IsPathUnderRoot(
                logPath,
                logRoot) ||
            !File.Exists(logPath))
        {
            return false;
        }

        var logInfo = new FileInfo(logPath);
        if (logInfo.Length <= TunnelLogRotationBytes)
        {
            return false;
        }

        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire(
                registration.Id);
        if (operationLease is null)
        {
            return false;
        }

        ManagedMcpTunnelProvisioningService
            .InvalidateRuntimeStatusCache(
                registration.Id);

        var runtime =
            await ManagedMcpTunnelProvisioningService
                .GetRuntimeStatusAsync(
                    registration,
                    cancellationToken)
                .ConfigureAwait(false);
        if (!runtime.Ready ||
            !runtime.ProcessRunning)
        {
            return false;
        }

        var health =
            await ManagedMcpTunnelHealthService
                .GetSnapshotAsync(
                    registration,
                    cancellationToken)
                .ConfigureAwait(false);

        if (health is null ||
            !health.Live ||
            !health.Ready ||
            health.HasCriticalDegradation ||
            !health.IsQuietForMaintenance(
                DateTimeOffset.UtcNow,
                TunnelQuietPeriod))
        {
            return false;
        }

        if (!await TryRotateTunnelLogWithoutStoppingRuntimeAsync(
                registration,
                logPath,
                health,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        TrayLog.Write(
            $"Tunnel log rotated in-place after stable idle threshold. MCP={registration.Id}; LimitBytes={TunnelLogRotationBytes}");

        return true;
    }

    private static async Task<bool>
        TryRotateTunnelLogWithoutStoppingRuntimeAsync(
            ManagedMcpRegistration registration,
            string logPath,
            ManagedMcpTunnelHealthSnapshot initialHealth,
            CancellationToken cancellationToken)
    {
        if (!string.Equals(
                initialHealth.RuntimeVersion.TrimStart('v', 'V'),
                InPlaceRotationVerifiedRuntimeVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var activityMarker =
            initialHealth.LastActivityUtc;

        await Task.Delay(
                TunnelRotationStabilityDelay,
                cancellationToken)
            .ConfigureAwait(false);

        var stableHealth =
            await ManagedMcpTunnelHealthService
                .GetSnapshotAsync(
                    registration,
                    cancellationToken)
                .ConfigureAwait(false);
        if (!IsStableQuietSnapshot(
                initialHealth,
                stableHealth,
                activityMarker))
        {
            return false;
        }

        var archivePath = logPath + ".1";
        var temporaryArchivePath =
            archivePath + ".tmp";

        try
        {
            await using var source = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

            var snapshotLength = source.Length;
            if (snapshotLength <=
                TunnelLogRotationBytes)
            {
                return false;
            }

            await using (var archive = new FileStream(
                             temporaryArchivePath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous |
                             FileOptions.SequentialScan))
            {
                source.Position = 0;
                var remaining = snapshotLength;
                var buffer = new byte[64 * 1024];

                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await source.ReadAsync(
                            buffer.AsMemory(
                                0,
                                (int)Math.Min(
                                    buffer.Length,
                                    remaining)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        return false;
                    }

                    await archive.WriteAsync(
                            buffer.AsMemory(0, read),
                            cancellationToken)
                        .ConfigureAwait(false);
                    remaining -= read;
                }

                await archive.FlushAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (source.Length != snapshotLength)
            {
                return false;
            }

            var finalHealth =
                await ManagedMcpTunnelHealthService
                    .GetSnapshotAsync(
                        registration,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!IsStableQuietSnapshot(
                    stableHealth!,
                    finalHealth,
                    activityMarker) ||
                source.Length != snapshotLength)
            {
                return false;
            }

            File.Move(
                temporaryArchivePath,
                archivePath,
                overwrite: true);

            // The v0.0.15 Windows runtime keeps this log open with append
            // semantics and read/write sharing. Truncating the same file
            // preserves the live writer handle and therefore never needs the
            // hard process termination performed by "runtimes stop".
            if (source.Length != snapshotLength)
            {
                return false;
            }

            source.SetLength(0);
            source.Flush(
                flushToDisk: true);
            return true;
        }
        finally
        {
            if (File.Exists(
                    temporaryArchivePath))
            {
                try
                {
                    File.Delete(
                        temporaryArchivePath);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException)
                {
                    TrayLog.Write(
                        $"Tunnel log rotation temporary archive cleanup deferred. MCP={registration.Id}",
                        ex);
                }
            }
        }
    }

    private static bool IsStableQuietSnapshot(
        ManagedMcpTunnelHealthSnapshot previous,
        ManagedMcpTunnelHealthSnapshot? current,
        DateTimeOffset? activityMarker)
    {
        if (current is null ||
            !current.Live ||
            !current.Ready ||
            current.HasCriticalDegradation ||
            !current.IsQuietForMaintenance(
                DateTimeOffset.UtcNow,
                TunnelQuietPeriod))
        {
            return false;
        }

        return previous.QueueDepth ==
                   current.QueueDepth &&
               previous.DispatcherActive ==
                   current.DispatcherActive &&
               previous.ResponseInProgress ==
                   current.ResponseInProgress &&
               previous.LastActivityUtc ==
                   current.LastActivityUtc &&
               current.LastActivityUtc ==
                   activityMarker;
    }

    private static IDisposable? TryAcquireVersionPruneLeases(
        IReadOnlyList<ManagedMcpRegistration> registrations)
    {
        var leases = new List<IDisposable>();
        try
        {
            foreach (var registrationId in registrations
                         .Where(registration =>
                             registration.Tunnel is { Required: true })
                         .Select(registration => registration.Id)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             id => id,
                             StringComparer.OrdinalIgnoreCase))
            {
                var lease =
                    ManagedMcpOperationCoordinator.TryAcquire(
                        registrationId);
                if (lease is null)
                {
                    foreach (var acquired in leases)
                    {
                        acquired.Dispose();
                    }

                    return null;
                }

                leases.Add(lease);
            }

            return new CompositeLease(leases);
        }
        catch
        {
            foreach (var acquired in leases)
            {
                acquired.Dispose();
            }

            throw;
        }
    }

    private sealed class CompositeLease(
        IReadOnlyList<IDisposable> leases)
        : IDisposable
    {
        private IReadOnlyList<IDisposable>? _leases =
            leases;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(
                ref _leases,
                null);
            if (owned is null)
            {
                return;
            }

            for (var index = owned.Count - 1;
                 index >= 0;
                 index--)
            {
                owned[index].Dispose();
            }
        }
    }

    private sealed record CleanupResult(
        int DeletedEntries,
        long ReclaimedBytes);
}
