using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Talvora;

internal sealed record Context7LookupRecord(
    DateTimeOffset Ts, string? Caller, string LibraryId, string Query,
    int Status, int Bytes, string Sha);

/// <summary>Append-only, best-effort Context7 evidence. No response body is stored.</summary>
internal sealed class Context7Ledger : IAsyncDisposable
{
    private readonly string _directory;
    private readonly object _fileGate = new();
    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _writer;
    private readonly Task _retention;

    internal Context7Ledger(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "Context7");
        RemoveExpiredFiles();
        _writer = Task.Run(WriteLoopAsync);
        _retention = Task.Run(RetentionLoopAsync);
    }

    internal void Record(Context7LookupRecord record)
    {
        try { _queue.Writer.TryWrite(new Work(record, null)); }
        catch (Exception) { /* An evidence failure must not fail the lookup. */ }
    }

    internal Task FlushAsync()
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new Work(null, done)))
            done.TrySetResult(true);
        return done.Task;
    }

    internal IReadOnlyList<Context7LookupRecord> ReadSince(DateTimeOffset since)
    {
        var records = new List<Context7LookupRecord>();
        try
        {
            lock (_fileGate)
            {
                if (!Directory.Exists(_directory))
                    return records;
                foreach (var path in Directory.EnumerateFiles(_directory, "lookups-*.jsonl")
                             .OrderBy(path => path, StringComparer.Ordinal))
                {
                    if (!TryGetDay(path, out var day) || day < DateOnly.FromDateTime(since.UtcDateTime))
                        continue;
                    foreach (var line in File.ReadLines(path))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var json = doc.RootElement;
                            var timestamp = json.GetProperty("ts").GetDateTimeOffset();
                            if (timestamp < since)
                                continue;
                            records.Add(new Context7LookupRecord(
                                timestamp,
                                json.GetProperty("caller").ValueKind == JsonValueKind.Null
                                    ? null : json.GetProperty("caller").GetString(),
                                json.GetProperty("libraryId").GetString()!,
                                json.GetProperty("query").GetString()!,
                                json.GetProperty("status").GetInt32(),
                                json.GetProperty("bytes").GetInt32(),
                                json.GetProperty("sha").GetString()!));
                        }
                        catch (Exception)
                        {
                            // A corrupt line cannot hide otherwise valid evidence.
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // A failed file read yields the records already read.
        }
        return records;
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
            if (work.Record is not { } record)
                continue;
            try
            {
                var path = Path.Combine(_directory, $"lookups-{record.Ts.UtcDateTime:yyyy-MM-dd}.jsonl");
                lock (_fileGate)
                {
                    Directory.CreateDirectory(_directory);
                    using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using (var writer = new Utf8JsonWriter(file))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("ts", record.Ts.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                        writer.WriteString("caller", record.Caller);
                        writer.WriteString("libraryId", record.LibraryId);
                        writer.WriteString("query", record.Query);
                        writer.WriteNumber("status", record.Status);
                        writer.WriteNumber("bytes", record.Bytes);
                        writer.WriteString("sha", record.Sha);
                        writer.WriteEndObject();
                        writer.Flush();
                    }
                    file.WriteByte((byte)'\n');
                }
            }
            catch (Exception)
            {
                // A disk error drops only the current record.
            }
        }
    }

    private async Task RetentionLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                RemoveExpiredFiles();
        }
        catch (OperationCanceledException) { }
    }

    private void RemoveExpiredFiles()
    {
        try
        {
            lock (_fileGate)
            {
                Directory.CreateDirectory(_directory);
                var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);
                foreach (var path in Directory.EnumerateFiles(_directory, "lookups-*.jsonl"))
                {
                    if (!TryGetDay(path, out var day) || day >= cutoff)
                        continue;
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception)
        {
            // Retention is always best effort.
        }
    }

    private static bool TryGetDay(string path, out DateOnly day)
    {
        var name = Path.GetFileName(path);
        day = default;
        return name.Length == 24 &&
               name.StartsWith("lookups-", StringComparison.Ordinal) &&
               name.EndsWith(".jsonl", StringComparison.Ordinal) &&
               DateOnly.TryParseExact(name.Substring(8, 10), "yyyy-MM-dd",
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    private sealed record Work(Context7LookupRecord? Record, TaskCompletionSource<bool>? Barrier);
}
