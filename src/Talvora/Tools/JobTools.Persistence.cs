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

    private static async Task PumpReaderAsync(StreamReader reader, string path)
    {
        await using var file = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: false)
        {
            AutoFlush = true,
        };

        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                break;
            }

            await writer.WriteAsync(buffer.AsMemory(0, read));
            await writer.FlushAsync();
        }
    }

    private static async Task ObserveExitAsync(
        string jobId,
        TalvoraJobRuntime runtime)
    {
        try
        {
            await runtime.Process.WaitForExitAsync();
            await Task.WhenAll(runtime.StdoutPump, runtime.StderrPump);

            var metadata = runtime.Metadata with
            {
                State = "Exited",
                ExitCode = runtime.Process.ExitCode,
                ExitedAtUtc = DateTime.UtcNow,
            };

            await WriteMetadataAsync(metadata, CancellationToken.None);
        }
        catch
        {
        }
        finally
        {
            // Do not dispose the shared Process wrapper here. A concurrent job_stop/get
            // call may still be completing against the same exited process. Removing
            // the runtime drops Talvora's long-lived reference; SafeHandle/GC cleanup
            // can reclaim the wrapper after in-flight callers release their references.
            LiveJobs.TryRemove(jobId, out _);
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
            return delta < TimeSpan.FromSeconds(10);
        }
        catch
        {
            return true;
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
