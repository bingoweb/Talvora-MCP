using System.Collections.Concurrent;
using System.ComponentModel;
using Talvora.Shared;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class JobTools
{
private static string GetJobsRoot()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var root = Path.Combine(programData, "Talvora", "Jobs");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetJobErrorLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Logs",
            "jobs.log");

    private static string GetLogGenerationPath(string path) =>
        path + ".generation";

    private static async Task<long> ReadLogGenerationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var generationPath = GetLogGenerationPath(path);
        if (!File.Exists(generationPath))
        {
            return 0;
        }

        var text = await File.ReadAllTextAsync(
            generationPath,
            cancellationToken).ConfigureAwait(false);
        if (!long.TryParse(text.Trim(), out var generation) ||
            generation < 0)
        {
            throw new InvalidDataException(
                $"Invalid job log generation metadata: {generationPath}");
        }

        return generation;
    }

    private static async Task WriteLogGenerationAsync(
        string path,
        long generation,
        CancellationToken cancellationToken)
    {
        _ = await AtomicFile.WriteAllTextAsync(
            GetLogGenerationPath(path),
            generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetMetadataPath(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) ||
            jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            jobId.Contains(Path.DirectorySeparatorChar) ||
            jobId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Invalid job ID.", nameof(jobId));
        }

        return Path.Combine(GetJobsRoot(), jobId, "job.json");
    }

    private static async Task PumpReaderAsync(
        StreamReader reader,
        string path,
        long maximumBytes = MaximumJobLogFileBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes));
        }

        var buffer = new char[8192];
        var encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false);
        var generation =
            await ReadLogGenerationAsync(
                path,
                CancellationToken.None).ConfigureAwait(false);
        FileStream? file = null;
        StreamWriter? writer = null;

        try
        {
            (file, writer) =
                OpenJobLogWriter(path, encoding);

            while (true)
            {
                var read = await reader.ReadAsync(
                    buffer.AsMemory());
                if (read == 0)
                {
                    break;
                }

                var encodedBytes =
                    encoding.GetByteCount(
                        buffer.AsSpan(0, read));
                if (file.Length > 0 &&
                    file.Length + encodedBytes >
                        maximumBytes)
                {
                    await writer.FlushAsync()
                        .ConfigureAwait(false);
                    await writer.DisposeAsync()
                        .ConfigureAwait(false);
                    writer = null;
                    file = null;

                    File.Move(
                        path,
                        path + ".1",
                        overwrite: true);
                    generation = checked(generation + 1);
                    await WriteLogGenerationAsync(
                        path,
                        generation,
                        CancellationToken.None)
                        .ConfigureAwait(false);
                    (file, writer) =
                        OpenJobLogWriter(path, encoding);
                }

                await writer.WriteAsync(
                        buffer.AsMemory(0, read))
                    .ConfigureAwait(false);
                await writer.FlushAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
    }

    private static (
        FileStream File,
        StreamWriter Writer) OpenJobLogWriter(
        string path,
        Encoding encoding)
    {
        var file = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        var writer = new StreamWriter(
            file,
            encoding,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        return (file, writer);
    }

    private static async Task ObserveExitAsync(
        string jobId,
        TalvoraJobRuntime runtime)
    {
        try
        {
            await runtime.Process.WaitForExitAsync();
            var exitCode = runtime.Process.ExitCode;

            try
            {
                await Task.WhenAll(runtime.StdoutPump, runtime.StderrPump);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // The process exit state is authoritative even if log pumping failed.
                // Persist terminal metadata so a logging error cannot leave a job marked Running.
            }

            var metadata = runtime.Metadata with
            {
                State = "Exited",
                ExitCode = exitCode,
                ExitedAtUtc = DateTime.UtcNow,
            };

            await WriteMetadataAsync(metadata, CancellationToken.None);
        }
        catch (Exception ex)
        {
            FileLog.Write(
                GetJobErrorLogPath(),
                $"Job exit observer failed. JobId={jobId}; ProcessId={runtime.Metadata.ProcessId}",
                ex);
        }
        finally
        {
            // Do not dispose the shared Process wrapper here. A concurrent job_stop/get
            // call may still be completing against the same exited process. Removing
            // the runtime drops Talvora's long-lived reference; SafeHandle/GC cleanup
            // can reclaim the wrapper after in-flight callers release their references.
            LiveJobs.TryRemove(jobId, out _);
            try
            {
                await CleanupCompletedJobsAsync(
                    GetJobsRoot(),
                    MaximumCompletedJobs,
                    MaximumCompletedJobBytes,
                    CompletedJobRetention,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException)
            {
                FileLog.Write(
                    GetJobErrorLogPath(),
                    "Completed job retention cleanup failed.",
                    ex);
            }
        }
    }

    private static async Task CleanupCompletedJobsAsync(
        string root,
        int maximumCompletedJobs,
        long maximumCompletedBytes,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        if (maximumCompletedJobs <= 0 ||
            maximumCompletedBytes <= 0 ||
            retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCompletedJobs));
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        var candidates = new List<(
            string Directory,
            TalvoraJobMetadata Metadata,
            long Bytes,
            DateTime SortTimeUtc)>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(
                directory,
                "job.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var metadata = await ReadMetadataFileAsync(
                    metadataPath,
                    cancellationToken).ConfigureAwait(false);
                var retentionSortTime =
                    metadata.ExitedAtUtc ??
                    metadata.StartedAtUtc;
                if (LiveJobs.ContainsKey(metadata.JobId))
                {
                    continue;
                }

                var refreshed = await RefreshStateAsync(
                    metadata,
                    cancellationToken).ConfigureAwait(false);
                if (string.Equals(
                        refreshed.State,
                        "Running",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                metadata = metadata with
                {
                    State = refreshed.State,
                    ExitCode = refreshed.ExitCode,
                    ExitedAtUtc = refreshed.ExitedAtUtc,
                };

                candidates.Add((
                    directory,
                    metadata,
                    GetDirectorySizeBytes(directory),
                    retentionSortTime));
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException or
                OverflowException)
            {
            }
        }

        var cutoff = DateTime.UtcNow - retention;
        var keptCount = 0;
        long keptBytes = 0;
        foreach (var candidate in candidates
                     .OrderByDescending(
                         item => item.SortTimeUtc)
                     .ThenBy(
                         item => item.Metadata.JobId,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expired =
                candidate.SortTimeUtc < cutoff;
            var exceedsCount =
                keptCount >= maximumCompletedJobs;
            var exceedsBytes =
                candidate.Bytes >
                maximumCompletedBytes - keptBytes;
            if (expired ||
                exceedsCount ||
                exceedsBytes)
            {
                TryDeleteJobDirectory(
                    candidate.Directory);
                continue;
            }

            keptCount++;
            keptBytes = checked(
                keptBytes + candidate.Bytes);
        }
    }

    private static long GetDirectorySizeBytes(
        string directory)
    {
        long total = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                total = checked(
                    total +
                    new FileInfo(path).Length);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                OverflowException)
        {
            return long.MaxValue;
        }

        return total;
    }

    private static void TryDeleteJobDirectory(
        string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
        }
    }

    private static async Task<TalvoraJobMetadata> ReadMetadataAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var path = GetMetadataPath(jobId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Talvora job was not found.", path);
        }

        return await ReadMetadataFileAsync(path, cancellationToken);
    }

    private static Task<TalvoraJobMetadata> ReadMetadataFileAsync(
        string path,
        CancellationToken cancellationToken) =>
        JsonFileStore.ReadAsync<TalvoraJobMetadata>(
            path,
            JsonOptions,
            cancellationToken);

    private static Task WriteMetadataAsync(
        TalvoraJobMetadata metadata,
        CancellationToken cancellationToken) =>
        JsonFileStore.WriteAsync(
            metadata.MetadataPath,
            metadata,
            JsonOptions,
            cancellationToken: cancellationToken);

    private static async Task<TalvoraJobInfoResponse> RefreshStateAsync(
        TalvoraJobMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (LiveJobs.TryGetValue(metadata.JobId, out var runtime))
        {
            if (!runtime.Process.HasExited)
            {
                return ToResponse(metadata with { State = "Running" });
            }

            var exited = metadata with
            {
                State = "Exited",
                ExitCode = runtime.Process.ExitCode,
                ExitedAtUtc = metadata.ExitedAtUtc ?? DateTime.UtcNow,
            };
            await WriteMetadataAsync(exited, cancellationToken);
            return ToResponse(exited);
        }

        if (string.Equals(metadata.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var process = Process.GetProcessById(metadata.ProcessId);
                if (!process.HasExited && MatchesOriginalProcess(process, metadata))
                {
                    return ToResponse(metadata);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }

            metadata = await MarkExitedUnknownAsync(metadata, cancellationToken);
        }

        return ToResponse(metadata);
    }

    private static bool MatchesOriginalProcess(
        Process process,
        TalvoraJobMetadata metadata)
    {
        try
        {
            var delta = (process.StartTime.ToUniversalTime() - metadata.StartedAtUtc).Duration();
            if (delta >= TimeSpan.FromSeconds(2))
            {
                return false;
            }

            if (!Path.IsPathRooted(metadata.Executable))
            {
                return true;
            }

            var actualExecutable = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(actualExecutable) &&
                   string.Equals(
                       Path.GetFullPath(actualExecutable),
                       Path.GetFullPath(metadata.Executable),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<TalvoraJobMetadata> MarkExitedUnknownAsync(
        TalvoraJobMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(metadata.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return metadata;
        }

        var updated = metadata with
        {
            State = "ExitedUnknown",
            ExitedAtUtc = metadata.ExitedAtUtc ?? DateTime.UtcNow,
        };
        await WriteMetadataAsync(updated, cancellationToken);
        return updated;
    }

    private static TalvoraJobInfoResponse ToResponse(TalvoraJobMetadata metadata) =>
        new(
            metadata.JobId,
            metadata.ProcessId,
            metadata.State,
            metadata.ExitCode,
            metadata.Executable,
            metadata.Arguments,
            metadata.WorkingDirectory,
            metadata.StartedAtUtc,
            metadata.ExitedAtUtc,
            metadata.StdoutPath,
            metadata.StderrPath,
            metadata.MetadataPath);
}
