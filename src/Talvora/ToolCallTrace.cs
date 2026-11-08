using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

/// <summary>Short, best-effort tool-call trace; arguments and results never reach disk.</summary>
internal sealed class ToolCallTrace : IAsyncDisposable
{
    private readonly string _directory;
    private readonly object _fileGate = new();
    private readonly Channel<TraceWork> _queue = Channel.CreateUnbounded<TraceWork>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _writer;
    private readonly Task _retention;

    public ToolCallTrace(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "CallTrace");
        _writer = Task.Run(WriteLoopAsync);
        _retention = Task.Run(RetentionLoopAsync);
    }

    public void Record(
        DateTimeOffset startedAtUtc,
        string? caller,
        string tool,
        IDictionary<string, JsonElement>? arguments,
        long elapsedMilliseconds,
        CallToolResult? result)
    {
        if (TalvoraLineTrafficMeter.IsOwnClient(caller))
            return;
        try
        {
            // Clone before the request ends; the queued item owns no tool output.
            var safeArguments = arguments?.ToDictionary(
                pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
            var detail = result?.StructuredContent;
            var outcome = detail is { ValueKind: JsonValueKind.Object } structured &&
                StructuredResultFailure.Describe(structured, tool) == StructuredResultFailure.TimedOut
                    ? "timedOut"
                    : result is null || result.IsError == true ||
                      detail is { ValueKind: JsonValueKind.Object } failure &&
                      StructuredResultFailure.IsFailure(failure, tool)
                        ? "error"
                        : "ok";
            int? exit = null;
            if (detail is { ValueKind: JsonValueKind.Object } output &&
                output.TryGetProperty("exitCode", out var exitCode) &&
                exitCode.ValueKind == JsonValueKind.Number &&
                exitCode.TryGetInt32(out var number))
                exit = number;

            _queue.Writer.TryWrite(new TraceWork(
                new TraceEntry(startedAtUtc.ToUniversalTime(), caller, tool,
                    Math.Max(0, elapsedMilliseconds), outcome, exit, safeArguments),
                null));
        }
        catch (Exception)
        {
            // Trace capture is never allowed to fail a client call.
        }
    }

    internal Task FlushAsync()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new TraceWork(null, ready)))
            ready.TrySetResult(true);
        return ready.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _lifetime.Cancel();
        try { await Task.WhenAll(_writer, _retention); }
        catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            if (work.Barrier is { } barrier)
            {
                barrier.TrySetResult(true);
                continue;
            }

            if (work.Entry is not { } entry)
                continue;

            try
            {
                var file = Path.Combine(_directory,
                    $"calls-{entry.StartedAtUtc.UtcDateTime:yyyy-MM-dd}.jsonl");
                var command = BuildCommand(entry.Tool, entry.Arguments);
                var cwd = FirstString(entry.Arguments,
                    "workingDirectory", "repositoryPath", "workspaceRoot", "root");
                var fingerprint = Fingerprint(entry.Arguments);

                lock (_fileGate)
                {
                    Directory.CreateDirectory(_directory);
                    using var stream = new FileStream(file, FileMode.Append,
                        FileAccess.Write, FileShare.Read);
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("ts", entry.StartedAtUtc.UtcDateTime.ToString(
                            "O", CultureInfo.InvariantCulture));
                        writer.WriteString("caller", entry.Caller);
                        writer.WriteString("tool", entry.Tool);
                        writer.WriteNumber("ms", entry.ElapsedMilliseconds);
                        writer.WriteString("outcome", entry.Outcome);
                        if (entry.Exit is { } exit)
                            writer.WriteNumber("exit", exit);
                        else
                            writer.WriteNull("exit");
                        writer.WriteString("cwd", cwd);
                        writer.WriteString("cmd", command);
                        writer.WriteString("args", fingerprint);
                        writer.WriteEndObject();
                        writer.Flush();
                    }
                    stream.WriteByte((byte)'\n');
                }
            }
            catch (Exception)
            {
                // A disk error drops only this trace record.
            }
        }
    }

    private async Task RetentionLoopAsync()
    {
        RemoveExpiredFiles();
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                RemoveExpiredFiles();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RemoveExpiredFiles()
    {
        try
        {
            lock (_fileGate)
            {
                Directory.CreateDirectory(_directory);
                var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);
                foreach (var path in Directory.EnumerateFiles(_directory, "calls-*.jsonl"))
                {
                    var name = Path.GetFileName(path);
                    if (name.Length != 22 || !name.StartsWith("calls-", StringComparison.Ordinal) ||
                        !name.EndsWith(".jsonl", StringComparison.Ordinal) ||
                        !DateOnly.TryParseExact(name.Substring(6, 10), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                        day >= cutoff)
                        continue;
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception)
        {
            // Retention must not affect the service or calls.
        }
    }

    private static string? FirstString(
        IReadOnlyDictionary<string, JsonElement>? arguments, params string[] names)
    {
        if (arguments is null)
            return null;
        foreach (var name in names)
        {
            if (arguments.TryGetValue(name, out var json) &&
                json.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(json.GetString()))
                return json.GetString();
        }
        return null;
    }

    private static string? BuildCommand(
        string tool, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null ||
            !arguments.ContainsKey("executable") &&
            !arguments.ContainsKey("script") &&
            !arguments.ContainsKey("arguments"))
            return null;

        var description = new StringBuilder(FirstString(arguments, "executable") ?? tool);
        if (FirstString(arguments, "script") is { } script)
            description.Append(' ').Append(script);
        if (arguments.TryGetValue("arguments", out var args))
        {
            if (args.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in args.EnumerateArray())
                    description.Append(' ').Append(value.ValueKind == JsonValueKind.String
                        ? value.GetString() : value.GetRawText());
            }
            else if (args.ValueKind == JsonValueKind.String)
                description.Append(' ').Append(args.GetString());
        }
        var redacted = FileLog.RedactSensitiveData(description.ToString())
            .Replace('\r', ' ').Replace('\n', ' ');
        return redacted.Length > 160 ? redacted[..160] : redacted;
    }

    private static string Fingerprint(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (arguments is not null)
            {
                foreach (var item in arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(item.Key);
                    WriteCanonical(writer, item.Value);
                }
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))[..16].ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in json.EnumerateObject().OrderBy(
                item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in json.EnumerateArray())
                WriteCanonical(writer, value);
            writer.WriteEndArray();
        }
        else
        {
            json.WriteTo(writer);
        }
    }

    private sealed record TraceEntry(
        DateTimeOffset StartedAtUtc, string? Caller, string Tool, long ElapsedMilliseconds,
        string Outcome, int? Exit, IReadOnlyDictionary<string, JsonElement>? Arguments);

    private sealed record TraceWork(TraceEntry? Entry, TaskCompletionSource<bool>? Barrier);
}
