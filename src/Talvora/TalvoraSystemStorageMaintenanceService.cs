using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            await RunMaintenanceAsync(
                stoppingToken);

            using var timer =
                new PeriodicTimer(
                    MaintenanceInterval);
            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await RunMaintenanceAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
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
                        var result =
                            TalvoraOwnedTempCleanup.CleanupTopLevel(
                                root,
                                cutoffUtc,
                                TalvoraOwnedTempCleanup.DefaultPrefixes,
                                TalvoraOwnedTempCleanup.DefaultExactNames,
                                cancellationToken);
                        deleted += result.DeletedEntries;
                        reclaimedBytes += result.ReclaimedBytes;
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

            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                    parent,
                    candidate,
                    cutoffUtc,
                    out var bytes))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new TalvoraOwnedTempCleanupResult(
            deleted,
            reclaimedBytes);
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
