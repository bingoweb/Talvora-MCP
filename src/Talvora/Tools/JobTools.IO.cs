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
}
