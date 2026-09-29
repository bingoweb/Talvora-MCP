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
    internal const long TunnelLogRotationBytes =
        32L * 1024 * 1024;

    internal static readonly TimeSpan TunnelQuietPeriod =
        TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan TemporaryArtifactRetention =
        TimeSpan.FromDays(7);

    internal static readonly TimeSpan TestArtifactRetention =
        TimeSpan.FromDays(2);

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
            RuntimeLifecycle: "ready",
            Components: [],
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
                foreach (var staging in Directory.EnumerateDirectories(
                             versionsRoot,
                             "*.stage.*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                            versionsRoot,
                            staging,
                            nowUtc - TimeSpan.FromDays(1),
                            out var bytes))
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

    private static CleanupResult PruneObsoleteTunnelClientVersions(
        string versionsRoot,
        IReadOnlyList<ManagedMcpRegistration> registrations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var retainedDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configPath = registration.Tunnel?.ConfigPath;
            if (string.IsNullOrWhiteSpace(configPath) ||
                !File.Exists(configPath))
            {
                continue;
            }

            try
            {
                var config = JsonSerializer.Deserialize<BusinessConfig>(
                    File.ReadAllText(configPath),
                    StorageJsonOptions);
                var executable = config?.TunnelClient;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(
                    Path.GetFullPath(executable));
                if (!string.IsNullOrWhiteSpace(directory) &&
                    TalvoraOwnedTempCleanup.IsPathUnderRoot(
                        directory,
                        versionsRoot))
                {
                    retainedDirectories.Add(
                        Path.GetFullPath(directory));
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException or
                ArgumentException or
                NotSupportedException)
            {
                TrayLog.Write(
                    $"Tunnel-client version retention skipped unreadable config. MCP={registration.Id}",
                    ex);
            }
        }

        var versionDirectories = Directory
            .EnumerateDirectories(
                versionsRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                !Path.GetFileName(path).Contains(
                    ".stage.",
                    StringComparison.OrdinalIgnoreCase))
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToArray();

        foreach (var recent in versionDirectories.Take(2))
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
                    out var bytes))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new CleanupResult(
            deleted,
            reclaimedBytes);
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

        var reconnectRequired = false;
        try
        {
            reconnectRequired = true;
            await ManagedMcpTunnelProvisioningService
                .DisconnectExistingAsync(
                    registration,
                    cancellationToken)
                .ConfigureAwait(false);

            if (File.Exists(logPath))
            {
                var archivePath = logPath + ".1";
                File.Move(
                    logPath,
                    archivePath,
                    overwrite: true);
            }
        }
        finally
        {
            if (reconnectRequired)
            {
                await ManagedMcpTunnelProvisioningService
                    .ConnectExistingAsync(
                        registration,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        ManagedMcpTunnelProvisioningService
            .InvalidateRuntimeStatusCache(
                registration.Id);
        var restored =
            await ManagedMcpTunnelProvisioningService
                .GetRuntimeStatusAsync(
                    registration,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!restored.Ready ||
            !restored.ProcessRunning)
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} log rotation sonrası hazır duruma dönemedi.");
        }

        TrayLog.Write(
            $"Tunnel log rotated after idle threshold. MCP={registration.Id}; LimitBytes={TunnelLogRotationBytes}");

        return true;
    }

    private sealed record CleanupResult(
        int DeletedEntries,
        long ReclaimedBytes);
}
