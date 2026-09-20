using System.Text;

namespace Talvora.Tools;

public static partial class JobTools
{
    internal static async Task AssertStorageContractAsync(
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-JobStorage-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logPath = Path.Combine(
                root,
                "probe.log");
            await File.WriteAllBytesAsync(
                logPath,
                [],
                cancellationToken);
            await WriteLogGenerationAsync(
                logPath,
                0,
                cancellationToken).ConfigureAwait(false);

            const int testLogLimit = 64 * 1024;
            const int testReadLimit = 16 * 1024;
            var payload = new string(
                'x',
                256 * 1024);
            await using (
                var stream = new MemoryStream(
                    Encoding.UTF8.GetBytes(payload)))
            using (var reader = new StreamReader(
                       stream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: false,
                       bufferSize: 16 * 1024,
                       leaveOpen: false))
            {
                await PumpReaderAsync(
                    reader,
                    logPath,
                    testLogLimit).ConfigureAwait(false);
            }

            var generation =
                await ReadLogGenerationAsync(
                    logPath,
                    cancellationToken).ConfigureAwait(false);
            var archivePath = logPath + ".1";
            if (generation <= 0 ||
                !File.Exists(archivePath) ||
                new FileInfo(logPath).Length >
                    testLogLimit ||
                new FileInfo(archivePath).Length >
                    testLogLimit)
            {
                throw new InvalidOperationException(
                    "Background job log rotation contract failed.");
            }

            var bounded = await ReadOutputFromPathAsync(
                "probe",
                "stdout",
                logPath,
                offset: 0,
                maxBytes: 0,
                generation,
                maximumResponseBytes: testReadLimit,
                cancellationToken).ConfigureAwait(false);
            if (!bounded.ResponseLimited ||
                bounded.NextOffset >
                    testReadLimit ||
                bounded.Generation != generation)
            {
                throw new InvalidOperationException(
                    "Background job bounded read contract failed.");
            }

            var stale = await ReadOutputFromPathAsync(
                "probe",
                "stdout",
                logPath,
                offset: 100,
                maxBytes: 1024,
                generation: generation - 1,
                maximumResponseBytes: testReadLimit,
                cancellationToken).ConfigureAwait(false);
            if (!stale.ResetRequired ||
                stale.Offset != 0 ||
                stale.Generation != generation)
            {
                throw new InvalidOperationException(
                    "Background job generation reset contract failed.");
            }

            var transientPath = Path.Combine(
                root,
                "transient.log");
            await WriteLogGenerationAsync(
                transientPath,
                0,
                cancellationToken).ConfigureAwait(false);
            var delayedCreate = Task.Run(
                async () =>
                {
                    await Task.Delay(
                        35,
                        cancellationToken).ConfigureAwait(false);
                    await File.WriteAllTextAsync(
                        transientPath,
                        "ready",
                        cancellationToken).ConfigureAwait(false);
                },
                cancellationToken);
            var transient = await ReadOutputFromPathAsync(
                "probe",
                "stdout",
                transientPath,
                offset: 0,
                maxBytes: 1024,
                generation: 0,
                maximumResponseBytes: testReadLimit,
                cancellationToken).ConfigureAwait(false);
            await delayedCreate.ConfigureAwait(false);
            if (!string.Equals(
                    transient.Text,
                    "ready",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Background job rotation-gap retry contract failed.");
            }

            var jobsRoot = Path.Combine(
                root,
                "jobs");
            Directory.CreateDirectory(jobsRoot);
            var now = DateTime.UtcNow;
            var newest = await CreateCompletedFixtureAsync(
                jobsRoot,
                "newest",
                now.AddMinutes(-1),
                cancellationToken).ConfigureAwait(false);
            var older = await CreateCompletedFixtureAsync(
                jobsRoot,
                "older",
                now.AddMinutes(-2),
                cancellationToken).ConfigureAwait(false);

            await CleanupCompletedJobsAsync(
                jobsRoot,
                maximumCompletedJobs: 1,
                maximumCompletedBytes:
                    1024 * 1024,
                retention: TimeSpan.FromDays(30),
                cancellationToken).ConfigureAwait(false);

            if (!Directory.Exists(newest) ||
                Directory.Exists(older))
            {
                throw new InvalidOperationException(
                    "Background job completed retention contract failed.");
            }

            var ageRoot = Path.Combine(
                root,
                "age");
            Directory.CreateDirectory(ageRoot);
            var aged = await CreateCompletedFixtureAsync(
                ageRoot,
                "aged",
                now.AddDays(-2),
                cancellationToken).ConfigureAwait(false);
            await CleanupCompletedJobsAsync(
                ageRoot,
                maximumCompletedJobs: 10,
                maximumCompletedBytes:
                    1024 * 1024,
                retention: TimeSpan.FromDays(1),
                cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(aged))
            {
                throw new InvalidOperationException(
                    "Background job age retention contract failed.");
            }

            var recoveredRoot = Path.Combine(
                root,
                "recovered-running");
            Directory.CreateDirectory(recoveredRoot);
            var recoveredRunning =
                await CreatePersistedRunningFixtureAsync(
                    recoveredRoot,
                    "recently-recovered",
                    now.AddDays(-10),
                    cancellationToken).ConfigureAwait(false);
            await CleanupCompletedJobsAsync(
                recoveredRoot,
                maximumCompletedJobs: 10,
                maximumCompletedBytes:
                    1024 * 1024,
                retention: TimeSpan.FromDays(1),
                cancellationToken).ConfigureAwait(false);
            if (!Directory.Exists(recoveredRunning))
            {
                throw new InvalidOperationException(
                    "Recovered completed job was expired from its start time instead of its refreshed exit time.");
            }

            var recoveredMetadata =
                await ReadMetadataFileAsync(
                    Path.Combine(
                        recoveredRunning,
                        "job.json"),
                    cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    recoveredMetadata.State,
                    "ExitedUnknown",
                    StringComparison.OrdinalIgnoreCase) ||
                recoveredMetadata.ExitedAtUtc is null ||
                recoveredMetadata.ExitedAtUtc < now)
            {
                throw new InvalidOperationException(
                    "Recovered completed job exit metadata contract failed.");
            }

            var quotaRoot = Path.Combine(
                root,
                "quota");
            Directory.CreateDirectory(quotaRoot);
            var quotaNewest = await CreateCompletedFixtureAsync(
                quotaRoot,
                "quota-newest",
                now.AddMinutes(-1),
                cancellationToken,
                stdoutCharacters: 8 * 1024)
                .ConfigureAwait(false);
            var quotaOlder = await CreateCompletedFixtureAsync(
                quotaRoot,
                "quota-older",
                now.AddMinutes(-2),
                cancellationToken,
                stdoutCharacters: 8 * 1024)
                .ConfigureAwait(false);
            var singleDirectoryBudget =
                GetDirectorySizeBytes(quotaNewest) +
                256;
            await CleanupCompletedJobsAsync(
                quotaRoot,
                maximumCompletedJobs: 10,
                maximumCompletedBytes:
                    singleDirectoryBudget,
                retention: TimeSpan.FromDays(30),
                cancellationToken).ConfigureAwait(false);
            if (!Directory.Exists(quotaNewest) ||
                Directory.Exists(quotaOlder))
            {
                throw new InvalidOperationException(
                    "Background job byte quota contract failed.");
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

    private static async Task<string> CreateCompletedFixtureAsync(
        string root,
        string jobId,
        DateTime exitedAtUtc,
        CancellationToken cancellationToken,
        int stdoutCharacters = 7)
    {
        var directory = Path.Combine(
            root,
            jobId);
        Directory.CreateDirectory(directory);
        var stdoutPath = Path.Combine(
            directory,
            "stdout.log");
        var stderrPath = Path.Combine(
            directory,
            "stderr.log");
        var metadataPath = Path.Combine(
            directory,
            "job.json");
        await File.WriteAllTextAsync(
            stdoutPath,
            new string(
                'x',
                stdoutCharacters),
            cancellationToken);
        await File.WriteAllTextAsync(
            stderrPath,
            string.Empty,
            cancellationToken);

        var metadata = new TalvoraJobMetadata(
            jobId,
            ProcessId: 0,
            State: "Exited",
            ExitCode: 0,
            Executable: "fixture.exe",
            Arguments: [],
            WorkingDirectory: directory,
            StartedAtUtc:
                exitedAtUtc.AddSeconds(-1),
            ExitedAtUtc: exitedAtUtc,
            StdoutPath: stdoutPath,
            StderrPath: stderrPath,
            MetadataPath: metadataPath);
        await WriteMetadataAsync(
            metadata,
            cancellationToken).ConfigureAwait(false);
        return directory;
    }

    private static async Task<string> CreatePersistedRunningFixtureAsync(
        string root,
        string jobId,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            root,
            jobId);
        Directory.CreateDirectory(directory);
        var stdoutPath = Path.Combine(
            directory,
            "stdout.log");
        var stderrPath = Path.Combine(
            directory,
            "stderr.log");
        var metadataPath = Path.Combine(
            directory,
            "job.json");
        await File.WriteAllTextAsync(
            stdoutPath,
            "fixture",
            cancellationToken);
        await File.WriteAllTextAsync(
            stderrPath,
            string.Empty,
            cancellationToken);

        var metadata = new TalvoraJobMetadata(
            jobId,
            ProcessId: int.MaxValue,
            State: "Running",
            ExitCode: null,
            Executable: "fixture.exe",
            Arguments: [],
            WorkingDirectory: directory,
            StartedAtUtc: startedAtUtc,
            ExitedAtUtc: null,
            StdoutPath: stdoutPath,
            StderrPath: stderrPath,
            MetadataPath: metadataPath);
        await WriteMetadataAsync(
            metadata,
            cancellationToken).ConfigureAwait(false);
        return directory;
    }
}
