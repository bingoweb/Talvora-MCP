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
[McpServerTool(
        Name = "talvora_job_stop",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobStopResponse)),
     Description("Stop a Talvora background job by job ID. By default terminates the complete child process tree. The underlying unrestricted process tools remain available for arbitrary PID control.")]
    public static async Task<TalvoraJobStopResponse> Stop(
        string jobId,
        bool entireProcessTree = true,
        int timeoutSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var metadata = await ReadMetadataAsync(jobId, cancellationToken);
        Process? process = null;
        var ownsProcess = false;

        if (LiveJobs.TryGetValue(jobId, out var runtime))
        {
            process = runtime.Process;
        }
        else
        {
            try
            {
                process = Process.GetProcessById(metadata.ProcessId);
                ownsProcess = true;
            }
            catch (ArgumentException)
            {
                var final = await MarkExitedUnknownAsync(metadata, cancellationToken);
                return new TalvoraJobStopResponse(
                    jobId,
                    metadata.ProcessId,
                    true,
                    false,
                    true,
                    final.State,
                    final.ExitCode);
            }
        }

        try
        {
            if (process.HasExited)
            {
                var currentState = await RefreshStateAsync(metadata, cancellationToken);
                return new TalvoraJobStopResponse(
                    jobId,
                    metadata.ProcessId,
                    true,
                    false,
                    true,
                    currentState.State,
                    currentState.ExitCode);
            }

            if (ownsProcess && !MatchesOriginalProcess(process, metadata))
            {
                var final = await MarkExitedUnknownAsync(metadata, cancellationToken);
                return new TalvoraJobStopResponse(
                    jobId,
                    metadata.ProcessId,
                    true,
                    false,
                    true,
                    final.State,
                    final.ExitCode);
            }

            process.Kill(entireProcessTree);
            var exited = true;

            if (timeoutSeconds > 0)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    exited = process.HasExited;
                }
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            TalvoraJobInfoResponse refreshed;
            if (LiveJobs.ContainsKey(jobId))
            {
                refreshed = await RefreshStateAsync(metadata, cancellationToken);
            }
            else
            {
                var updated = metadata with
                {
                    State = exited ? "Stopped" : "Stopping",
                    ExitedAtUtc = exited ? DateTime.UtcNow : null,
                };
                await WriteMetadataAsync(updated, cancellationToken);
                refreshed = ToResponse(updated);
            }

            return new TalvoraJobStopResponse(
                jobId,
                metadata.ProcessId,
                true,
                true,
                exited,
                refreshed.State,
                refreshed.ExitCode);
        }
        finally
        {
            if (ownsProcess)
            {
                process.Dispose();
            }
        }
    }

    [McpServerTool(
        Name = "talvora_job_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobDeleteResponse)),
     Description("Delete persisted metadata and stdout/stderr logs for a Talvora background job. If the job is still running, stopIfRunning=true first terminates its process tree; otherwise deletion is rejected so live output is not orphaned.")]
    public static async Task<TalvoraJobDeleteResponse> Delete(
        string jobId,
        bool stopIfRunning = false,
        int stopTimeoutSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = GetMetadataPath(jobId);
        var directory = Path.GetDirectoryName(metadataPath)
            ?? throw new InvalidOperationException("Job directory could not be resolved.");

        if (!File.Exists(metadataPath) && !Directory.Exists(directory))
        {
            return new TalvoraJobDeleteResponse(
                jobId,
                false,
                false,
                false);
        }

        var stopped = false;

        if (File.Exists(metadataPath))
        {
            var metadata = await ReadMetadataFileAsync(metadataPath, cancellationToken);
            var state = await RefreshStateAsync(metadata, cancellationToken);

            if (string.Equals(state.State, "Running", StringComparison.OrdinalIgnoreCase))
            {
                if (!stopIfRunning)
                {
                    throw new InvalidOperationException(
                        "The Talvora job is still running. Set stopIfRunning=true to stop and delete it.");
                }

                var stop = await Stop(
                    jobId,
                    entireProcessTree: true,
                    timeoutSeconds: stopTimeoutSeconds,
                    cancellationToken);
                stopped = stop.Exited;

                if (!stop.Exited)
                {
                    throw new TimeoutException(
                        $"Talvora job did not exit before cleanup: {jobId}");
                }
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (LiveJobs.ContainsKey(jobId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        if (LiveJobs.ContainsKey(jobId))
        {
            throw new InvalidOperationException(
                $"Talvora job is still attached to the service and cannot be deleted yet: {jobId}");
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return new TalvoraJobDeleteResponse(
            jobId,
            true,
            stopped,
            !Directory.Exists(directory));
    }
}
