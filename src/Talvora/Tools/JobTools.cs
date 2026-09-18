using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraJobStartResponse(
    string JobId,
    int ProcessId,
    string State,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

public sealed record TalvoraJobInfoResponse(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    DateTime? ExitedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

public sealed record TalvoraJobListResponse(
    int Count,
    IReadOnlyList<TalvoraJobInfoResponse> Jobs);

public sealed record TalvoraJobOutputResponse(
    string JobId,
    string Stream,
    long Offset,
    long NextOffset,
    long Length,
    bool EndOfStream,
    string Text);

public sealed record TalvoraJobStdinResponse(
    string JobId,
    int ProcessId,
    int CharactersWritten,
    bool NewLineAppended);

public sealed record TalvoraJobStopResponse(
    string JobId,
    int ProcessId,
    bool Found,
    bool KillIssued,
    bool Exited,
    string State,
    int? ExitCode);

internal sealed record TalvoraJobMetadata(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    DateTime? ExitedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

internal sealed class TalvoraJobRuntime : IDisposable
{
    public required Process Process { get; init; }
    public required StreamWriter StandardInput { get; init; }
    public required TalvoraJobMetadata Metadata { get; init; }
    public required Task StdoutPump { get; init; }
    public required Task StderrPump { get; init; }

    public void Dispose()
    {
        try { StandardInput.Dispose(); } catch { }
        try { Process.Dispose(); } catch { }
    }
}

[McpServerToolType]
public static class JobTools
{
    private static readonly ConcurrentDictionary<string, TalvoraJobRuntime> LiveJobs =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
                try { process.Kill(entireProcessTree: true); } catch { }
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

    [McpServerTool(
        Name = "talvora_job_read_output",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobOutputResponse)),
     Description("Read stdout or stderr from a Talvora background job using a byte offset for incremental tailing. maxBytes=0 reads all currently available bytes from the offset.")]
    public static async Task<TalvoraJobOutputResponse> ReadOutput(
        string jobId,
        string stream = "stdout",
        long offset = 0,
        int maxBytes = 256 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException("offset and maxBytes cannot be negative.");
        }

        var metadata = await ReadMetadataAsync(jobId, cancellationToken);
        var normalizedStream = stream.Trim().ToLowerInvariant();
        var path = normalizedStream switch
        {
            "stdout" => metadata.StdoutPath,
            "stderr" => metadata.StderrPath,
            _ => throw new ArgumentOutOfRangeException(nameof(stream), "stream must be stdout or stderr."),
        };

        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        var length = file.Length;
        var start = Math.Min(offset, length);
        file.Position = start;

        var available = length - start;
        var requested = maxBytes == 0
            ? checked((int)Math.Min(int.MaxValue, available))
            : checked((int)Math.Min(maxBytes, available));

        var buffer = new byte[requested];
        var read = 0;
        while (read < requested)
        {
            var current = await file.ReadAsync(
                buffer.AsMemory(read, requested - read),
                cancellationToken);
            if (current == 0)
            {
                break;
            }
            read += current;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var nextOffset = start + read;

        return new TalvoraJobOutputResponse(
            jobId,
            normalizedStream,
            start,
            nextOffset,
            length,
            nextOffset >= length,
            text);
    }

    [McpServerTool(
        Name = "talvora_job_write_stdin",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobStdinResponse)),
     Description("Write text to stdin of a live Talvora background job started by the current service instance. Useful for interactive development servers and REPL-like processes.")]
    public static async Task<TalvoraJobStdinResponse> WriteStdin(
        string jobId,
        string text,
        bool appendNewLine = false,
        CancellationToken cancellationToken = default)
    {
        if (!LiveJobs.TryGetValue(jobId, out var runtime))
        {
            throw new InvalidOperationException(
                "The job is not attached to the current Talvora service instance, so stdin is unavailable.");
        }

        if (runtime.Process.HasExited)
        {
            throw new InvalidOperationException("The job has already exited.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (appendNewLine)
        {
            await runtime.StandardInput.WriteLineAsync(text.AsMemory(), cancellationToken);
        }
        else
        {
            await runtime.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
        }

        await runtime.StandardInput.FlushAsync(cancellationToken);

        return new TalvoraJobStdinResponse(
            jobId,
            runtime.Process.Id,
            text.Length + (appendNewLine ? Environment.NewLine.Length : 0),
            appendNewLine);
    }

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
            LiveJobs.TryRemove(jobId, out _);
            runtime.Dispose();
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

    private static async Task<TalvoraJobMetadata> ReadMetadataFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<TalvoraJobMetadata>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException($"Invalid Talvora job metadata: {path}");
    }

    private static async Task WriteMetadataAsync(
        TalvoraJobMetadata metadata,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(metadata.MetadataPath)
            ?? throw new InvalidOperationException("Job metadata directory could not be resolved.");
        Directory.CreateDirectory(parent);

        var temp = metadata.MetadataPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(
            temp,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                metadata,
                JsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temp, metadata.MetadataPath, overwrite: true);
    }

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