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
        Name = "talvora_job_read_output",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobOutputResponse)),
     Description("Read stdout or stderr from a Talvora background job using byte offset + optional rotation generation for incremental tailing. maxBytes=0 selects Talvora's bounded maximum chunk; use nextOffset/generation until endOfStream. resetRequired means rotation or a stale offset required restarting at byte 0.")]
    public static async Task<TalvoraJobOutputResponse> ReadOutput(
        string jobId,
        string stream = "stdout",
        long offset = 0,
        int maxBytes = 256 * 1024,
        long? generation = null,
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

        return await ReadOutputFromPathAsync(
            jobId,
            normalizedStream,
            path,
            offset,
            maxBytes,
            generation,
            MaximumJobReadResponseBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TalvoraJobOutputResponse> ReadOutputFromPathAsync(
        string jobId,
        string normalizedStream,
        string path,
        long offset,
        int maxBytes,
        long? generation,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        if (maximumResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResponseBytes));
        }

        var opened =
            await OpenStableJobLogSnapshotAsync(
                path,
                cancellationToken).ConfigureAwait(false);
        await using var file = opened.File;
        var currentGeneration = opened.Generation;

        var length = file.Length;
        var resetRequired =
            (generation.HasValue &&
             generation.Value != currentGeneration) ||
            offset > length;
        var start = resetRequired ? 0 : offset;
        file.Position = start;

        var available = length - start;
        var responseBudget = maxBytes == 0
            ? maximumResponseBytes
            : Math.Min(
                maxBytes,
                maximumResponseBytes);
        var requested = checked(
            (int)Math.Min(
                responseBudget,
                available));
        var responseLimited =
            available > requested;

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
            text,
            currentGeneration,
            resetRequired,
            responseLimited);
    }

    private static async Task<(
        FileStream File,
        long Generation)> OpenStableJobLogSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var before = await ReadLogGenerationAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
                var file = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                var after = await ReadLogGenerationAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
                if (before == after)
                {
                    return (file, before);
                }

                await file.DisposeAsync().ConfigureAwait(false);
            }
            catch (FileNotFoundException) when (attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(20),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"Job output rotated repeatedly while opening a stable snapshot: {path}");
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
}
