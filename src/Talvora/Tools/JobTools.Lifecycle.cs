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
        Name = "talvora_job_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobStartResponse)),
     Description("Start any executable as a long-running background development job under the Talvora LocalSystem service. Arguments, working directory, and environment overrides are unrestricted. stdout/stderr are persisted under ProgramData so they can be read incrementally.")]
    public static async Task<TalvoraJobStartResponse> Start(
        [Description("Executable path or command resolvable by Windows.")] string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool clearEnvironment = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException("Executable is required.", nameof(executable));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var jobId = Guid.NewGuid().ToString("N");
        var jobDirectory = Path.Combine(GetJobsRoot(), jobId);
        Directory.CreateDirectory(jobDirectory);

        var stdoutPath = Path.Combine(jobDirectory, "stdout.log");
        var stderrPath = Path.Combine(jobDirectory, "stderr.log");
        var metadataPath = Path.Combine(jobDirectory, "job.json");

        await File.WriteAllBytesAsync(stdoutPath, [], cancellationToken);
        await File.WriteAllBytesAsync(stderrPath, [], cancellationToken);

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        if (clearEnvironment)
        {
            startInfo.Environment.Clear();
        }

        foreach (var pair in environment ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        foreach (var argument in arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {executable}");
            }

            var metadata = new TalvoraJobMetadata(
                jobId,
                process.Id,
                "Running",
                null,
                executable,
                arguments ?? [],
                cwd,
                DateTime.UtcNow,
                null,
                stdoutPath,
                stderrPath,
                metadataPath);

            await WriteMetadataAsync(metadata, cancellationToken);

            var stdoutPump = PumpReaderAsync(process.StandardOutput, stdoutPath);
            var stderrPump = PumpReaderAsync(process.StandardError, stderrPath);

            var runtime = new TalvoraJobRuntime
            {
                Process = process,
                StandardInput = process.StandardInput,
                Metadata = metadata,
                StdoutPump = stdoutPump,
                StderrPump = stderrPump,
            };

            if (!LiveJobs.TryAdd(jobId, runtime))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { }
                runtime.Dispose();
                throw new InvalidOperationException($"Failed to register Talvora job: {jobId}");
            }

            _ = ObserveExitAsync(jobId, runtime);

            return new TalvoraJobStartResponse(
                metadata.JobId,
                metadata.ProcessId,
                metadata.State,
                metadata.Executable,
                metadata.Arguments,
                metadata.WorkingDirectory,
                metadata.StartedAtUtc,
                metadata.StdoutPath,
                metadata.StderrPath,
                metadata.MetadataPath);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    [McpServerTool(
        Name = "talvora_job_get",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobInfoResponse)),
     Description("Return current metadata and state for a Talvora background job. Jobs are identified by Talvora job ID and persisted under ProgramData.")]
    public static async Task<TalvoraJobInfoResponse> Get(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(jobId, cancellationToken);
        return await RefreshStateAsync(metadata, cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_job_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobListResponse)),
     Description("List Talvora background jobs. includeExited=false returns only jobs that are still running. maxResults=0 means unlimited.")]
    public static async Task<TalvoraJobListResponse> List(
        bool includeExited = true,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var root = GetJobsRoot();
        if (!Directory.Exists(root))
        {
            return new TalvoraJobListResponse(0, []);
        }

        var jobs = new List<TalvoraJobInfoResponse>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "job.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var metadata = await ReadMetadataFileAsync(metadataPath, cancellationToken);
                var job = await RefreshStateAsync(metadata, cancellationToken);

                if (!includeExited &&
                    !string.Equals(job.State, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                jobs.Add(job);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        IEnumerable<TalvoraJobInfoResponse> ordered = jobs
            .OrderByDescending(job => job.StartedAtUtc)
            .ThenBy(job => job.JobId, StringComparer.OrdinalIgnoreCase);

        if (maxResults > 0)
        {
            ordered = ordered.Take(maxResults);
        }

        var result = ordered.ToArray();
        return new TalvoraJobListResponse(result.Length, result);
    }
}
