using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.AccessControl;
using System.Security.Principal;
using Talvora.Shared;

namespace Talvora;

internal sealed class TalvoraSystemStorageMaintenanceService(
    ILogger<TalvoraSystemStorageMaintenanceService> logger)
    : BackgroundService
{
    private static readonly TimeSpan StartupDelay =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan MaintenanceInterval =
        TimeSpan.FromHours(6);

    private static readonly TimeSpan Retention =
        TimeSpan.FromDays(7);

    private static readonly TimeSpan TestArtifactRetention =
        TimeSpan.FromDays(2);

    private static readonly SecurityIdentifier LocalSystemSid =
        new(
            WellKnownSidType.LocalSystemSid,
            domainSid: null);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                StartupDelay,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await RunMaintenancePassSafelyAsync(
                stoppingToken);

            using var timer =
                new PeriodicTimer(
                    MaintenanceInterval);
            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await RunMaintenancePassSafelyAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunMaintenancePassSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await RunMaintenanceAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Storage retention is best-effort housekeeping. A malformed
            // diagnostic file, transient ACL failure, or Penpot maintenance
            // error must never stop the core Talvora Windows service.
            logger.LogWarning(
                ex,
                "Talvora system storage maintenance pass was deferred.");
        }
    }

    private Task RunMaintenanceAsync(
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var roots = GetSystemTempRoots();
                var deleted = 0;
                long reclaimedBytes = 0;
                var cutoffUtc =
                    DateTimeOffset.UtcNow - Retention;

                foreach (var root in roots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var testResult =
                            TalvoraOwnedTempCleanup.CleanupTopLevel(
                                root,
                                DateTimeOffset.UtcNow -
                                TestArtifactRetention,
                                TalvoraOwnedTempCleanup.TestPrefixes,
                                Array.Empty<string>(),
                                cancellationToken,
                                IsSystemOwnedCleanupCandidate);
                        deleted += testResult.DeletedEntries;
                        reclaimedBytes += testResult.ReclaimedBytes;

                        var result =
                            TalvoraOwnedTempCleanup.CleanupTopLevel(
                                root,
                                cutoffUtc,
                                TalvoraOwnedTempCleanup.DefaultPrefixes,
                                TalvoraOwnedTempCleanup.DefaultExactNames,
                                cancellationToken,
                                IsSystemOwnedCleanupCandidate);
                        deleted += result.DeletedEntries;
                        reclaimedBytes += result.ReclaimedBytes;

                        if (testResult.ScanLimitReached ||
                            result.ScanLimitReached)
                        {
                            logger.LogWarning(
                                "Talvora system storage maintenance reached its bounded scan limit for root {Root}; remaining entries are deferred.",
                                root);
                        }

                        var nestedResult =
                            CleanupSystemOwnedNestedTalvoraTemp(
                                root,
                                DateTimeOffset.UtcNow,
                                cancellationToken);
                        deleted += nestedResult.DeletedEntries;
                        reclaimedBytes += nestedResult.ReclaimedBytes;
                        if (nestedResult.ScanLimitReached)
                        {
                            logger.LogWarning(
                                "Talvora system nested temp maintenance reached its scan limit for root {Root}; remaining entries are deferred.",
                                root);
                        }
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException or
                        ArgumentException or
                        NotSupportedException)
                    {
                        logger.LogWarning(
                            ex,
                            "Talvora system storage maintenance deferred for root {Root}.",
                            root);
                    }
                }

                var testCleanup =
                    CleanupSystemOwnedTestArtifacts(
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                deleted += testCleanup.DeletedEntries;
                reclaimedBytes += testCleanup.ReclaimedBytes;

                var penpotCleanup =
                    TalvoraPenpotSupervisorMaintenance.Maintain(
                        cancellationToken);
                deleted += penpotCleanup.DeletedEntries;
                reclaimedBytes += penpotCleanup.ReclaimedBytes;
                if (penpotCleanup.ScanLimitReached)
                {
                    logger.LogWarning(
                        "Talvora Penpot legacy-log maintenance reached its scan limit; remaining entries are deferred.");
                }

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Talvora system storage maintenance completed. DeletedEntries={DeletedEntries}; ReclaimedBytes={ReclaimedBytes}",
                        deleted,
                        reclaimedBytes);
                }
            },
            cancellationToken);

    private static TalvoraOwnedTempCleanupResult
        CleanupSystemOwnedNestedTalvoraTemp(
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
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var cutoffUtc = nowUtc - Retention;
        var structuralRoot = Path.Combine(
            talvoraTempRoot,
            "Structural");
        var semanticWorkerRoot = Path.Combine(
            talvoraTempRoot,
            "semantic-worker");

        var structural =
            IsSystemOwnedCleanupCandidate(structuralRoot)
                ? TalvoraOwnedTempCleanup.CleanupGuidDirectories(
                    structuralRoot,
                    cutoffUtc,
                    cancellationToken,
                    IsSystemOwnedCleanupCandidate)
                : new TalvoraOwnedTempCleanupResult(0, 0);
        var semanticWorker =
            IsSystemOwnedCleanupCandidate(semanticWorkerRoot)
                ? TalvoraOwnedTempCleanup.CleanupGuidJsonFiles(
                    semanticWorkerRoot,
                    cutoffUtc,
                    cancellationToken,
                    IsSystemOwnedCleanupCandidate)
                : new TalvoraOwnedTempCleanupResult(0, 0);

        return new TalvoraOwnedTempCleanupResult(
            structural.DeletedEntries +
            semanticWorker.DeletedEntries,
            structural.ReclaimedBytes +
            semanticWorker.ReclaimedBytes,
            structural.ScanLimitReached ||
            semanticWorker.ScanLimitReached);
    }

    private static TalvoraOwnedTempCleanupResult
        CleanupSystemOwnedTestArtifacts(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
    {
        var commonData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonData))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var talvoraRoot = Path.Combine(
            commonData,
            "Talvora");
        var candidates = new[]
        {
            Path.Combine(
                talvoraRoot,
                "PenpotSmoke"),
            Path.Combine(
                talvoraRoot,
                "Penpot",
                "TestBrowser"),
            Path.Combine(
                talvoraRoot,
                "Penpot",
                "TestBrowserVisible"),
        };

        var deleted = 0;
        long reclaimedBytes = 0;
        var cutoffUtc =
            nowUtc - TestArtifactRetention;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSystemOwnedCleanupCandidate(candidate))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                    parent,
                    candidate,
                    cutoffUtc,
                    out var bytes,
                    cancellationToken))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new TalvoraOwnedTempCleanupResult(
            deleted,
            reclaimedBytes);
    }

    private static bool IsSystemOwnedCleanupCandidate(
        string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Ownership queries can resolve through a reparse point.
                // Never use a target ACL as evidence that the link itself is
                // a Talvora/SYSTEM-owned cleanup candidate.
                return false;
            }

            FileSystemSecurity security =
                (attributes & FileAttributes.Directory) != 0
                    ? FileSystemAclExtensions.GetAccessControl(
                        new DirectoryInfo(path),
                        AccessControlSections.Owner)
                    : FileSystemAclExtensions.GetAccessControl(
                        new FileInfo(path),
                        AccessControlSections.Owner);

            return security.GetOwner(
                    typeof(SecurityIdentifier)) is SecurityIdentifier owner &&
                owner.Equals(LocalSystemSid);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            SystemException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetSystemTempRoots()
    {
        var roots = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddRoot(
            roots,
            Path.GetTempPath());

        var systemRoot =
            Environment.GetEnvironmentVariable(
                "SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            AddRoot(
                roots,
                Path.Combine(
                    systemRoot,
                    "Temp"));
            AddRoot(
                roots,
                Path.Combine(
                    systemRoot,
                    "System32",
                    "config",
                    "systemprofile",
                    "AppData",
                    "Local",
                    "Temp"));
        }

        return roots.ToArray();
    }

    private static void AddRoot(
        ISet<string> roots,
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            roots.Add(
                Path.GetFullPath(path));
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException)
        {
        }
    }
}
