using System.Collections.Concurrent;
using System.Diagnostics;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraWatchEvent(
    long Sequence,
    string ChangeType,
    string FullPath,
    string? OldFullPath,
    string Name,
    string? OldName,
    DateTime OccurredAtUtc,
    string? Error);

public sealed record TalvoraWatchInfoResponse(
    string WatchId,
    string Path,
    string Filter,
    bool IncludeSubdirectories,
    string NotifyFilter,
    int InternalBufferSize,
    int MaxQueuedEvents,
    DateTime StartedAtUtc,
    int QueuedEvents,
    long DroppedEvents,
    bool Enabled);

public sealed record TalvoraWatchStartResponse(
    string WatchId,
    string Path,
    string Filter,
    bool IncludeSubdirectories,
    string NotifyFilter,
    int InternalBufferSize,
    int MaxQueuedEvents,
    DateTime StartedAtUtc);

public sealed record TalvoraWatchListResponse(
    int Count,
    IReadOnlyList<TalvoraWatchInfoResponse> Watches);

public sealed record TalvoraWatchReadResponse(
    string WatchId,
    int Count,
    int Remaining,
    long DroppedEvents,
    IReadOnlyList<TalvoraWatchEvent> Events);

public sealed record TalvoraWatchWaitResponse(
    string WatchId,
    bool Signaled,
    long AfterSequence,
    long ElapsedMilliseconds,
    TalvoraWatchEvent? Event);

public sealed record TalvoraWatchStopResponse(
    string WatchId,
    bool Found,
    bool Stopped);

internal sealed class TalvoraWatchRuntime : IDisposable
{
    public required string WatchId { get; init; }
    public required string Path { get; init; }
    public required string Filter { get; init; }
    public required bool IncludeSubdirectories { get; init; }
    public required NotifyFilters NotifyFilter { get; init; }
    public required int MaxQueuedEvents { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required FileSystemWatcher Watcher { get; init; }

    public ConcurrentQueue<TalvoraWatchEvent> Events { get; } = new();
    public long Sequence;
    public int QueuedEvents;
    public long DroppedEvents;

    public void Enqueue(
        string changeType,
        string fullPath,
        string name,
        string? oldFullPath = null,
        string? oldName = null,
        string? error = null)
    {
        var item = new TalvoraWatchEvent(
            Interlocked.Increment(ref Sequence),
            changeType,
            fullPath,
            oldFullPath,
            name,
            oldName,
            DateTime.UtcNow,
            error);

        Events.Enqueue(item);
        Interlocked.Increment(ref QueuedEvents);

        if (MaxQueuedEvents <= 0)
        {
            return;
        }

        while (Volatile.Read(ref QueuedEvents) > MaxQueuedEvents &&
               Events.TryDequeue(out _))
        {
            Interlocked.Decrement(ref QueuedEvents);
            Interlocked.Increment(ref DroppedEvents);
        }
    }

    public TalvoraWatchInfoResponse ToInfo() =>
        new(
            WatchId,
            Path,
            Filter,
            IncludeSubdirectories,
            NotifyFilter.ToString(),
            Watcher.InternalBufferSize,
            MaxQueuedEvents,
            StartedAtUtc,
            Math.Max(0, Volatile.Read(ref QueuedEvents)),
            Math.Max(0, Interlocked.Read(ref DroppedEvents)),
            Watcher.EnableRaisingEvents);

    public void Dispose()
    {
        try { Watcher.EnableRaisingEvents = false; } catch { }
        Watcher.Dispose();
    }
}

[McpServerToolType]
public static class WatchTools
{
    private static readonly ConcurrentDictionary<string, TalvoraWatchRuntime> Watches =
        new(StringComparer.OrdinalIgnoreCase);

    [McpServerTool(
        Name = "talvora_watch_start",
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchStartResponse)),
     Description("Start a live FileSystemWatcher for any accessible directory. Supports recursive watching, arbitrary wildcard filters, selectable NotifyFilters, configurable OS buffer size, and bounded or unlimited event queues. No path allowlist is applied.")]
    public static TalvoraWatchStartResponse Start(
        string path,
        string filter = "*",
        bool includeSubdirectories = true,
        string[]? notifyFilters = null,
        int internalBufferSize = 32768,
        int maxQueuedEvents = 10000)
    {
        if (maxQueuedEvents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueuedEvents));
        }
        if (internalBufferSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(internalBufferSize));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Watch path was not found: {fullPath}");
        }

        var effectiveFilter = string.IsNullOrWhiteSpace(filter) ? "*" : filter;
        var parsedNotifyFilters = ParseNotifyFilters(notifyFilters);

        var watcher = new FileSystemWatcher(fullPath, effectiveFilter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = parsedNotifyFilters,
            EnableRaisingEvents = false,
        };

        if (internalBufferSize > 0)
        {
            watcher.InternalBufferSize = internalBufferSize;
        }

        var watchId = Guid.NewGuid().ToString("N");
        var runtime = new TalvoraWatchRuntime
        {
            WatchId = watchId,
            Path = fullPath,
            Filter = effectiveFilter,
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = parsedNotifyFilters,
            MaxQueuedEvents = maxQueuedEvents,
            StartedAtUtc = DateTime.UtcNow,
            Watcher = watcher,
        };

        watcher.Created += (_, e) =>
            runtime.Enqueue("Created", e.FullPath, e.Name ?? Path.GetFileName(e.FullPath));
        watcher.Changed += (_, e) =>
            runtime.Enqueue("Changed", e.FullPath, e.Name ?? Path.GetFileName(e.FullPath));
        watcher.Deleted += (_, e) =>
            runtime.Enqueue("Deleted", e.FullPath, e.Name ?? Path.GetFileName(e.FullPath));
        watcher.Renamed += (_, e) =>
            runtime.Enqueue(
                "Renamed",
                e.FullPath,
                e.Name ?? Path.GetFileName(e.FullPath),
                e.OldFullPath,
                e.OldName);
        watcher.Error += (_, e) =>
            runtime.Enqueue(
                "Error",
                fullPath,
                string.Empty,
                error: FormatException(e.GetException()));

        if (!Watches.TryAdd(watchId, runtime))
        {
            runtime.Dispose();
            throw new InvalidOperationException($"Failed to register watcher: {watchId}");
        }

        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            Watches.TryRemove(watchId, out _);
            runtime.Dispose();
            throw;
        }

        return new TalvoraWatchStartResponse(
            watchId,
            fullPath,
            effectiveFilter,
            includeSubdirectories,
            parsedNotifyFilters.ToString(),
            watcher.InternalBufferSize,
            maxQueuedEvents,
            runtime.StartedAtUtc);
    }

    [McpServerTool(
        Name = "talvora_watch_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchInfoResponse)),
     Description("Return current state and queue statistics for one active Talvora filesystem watcher.")]
    public static TalvoraWatchInfoResponse Get(string watchId) =>
        GetRuntime(watchId).ToInfo();

    [McpServerTool(
        Name = "talvora_watch_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchListResponse)),
     Description("List all active Talvora filesystem watchers in the current service instance.")]
    public static TalvoraWatchListResponse List()
    {
        var watches = Watches.Values
            .OrderBy(value => value.StartedAtUtc)
            .ThenBy(value => value.WatchId, StringComparer.OrdinalIgnoreCase)
            .Select(value => value.ToInfo())
            .ToArray();

        return new TalvoraWatchListResponse(watches.Length, watches);
    }

    [McpServerTool(
        Name = "talvora_watch_read",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchReadResponse)),
     Description("Read queued filesystem change events. consume=true removes returned events. maxEvents=0 means all currently queued events. afterSequence filters events by monotonically increasing sequence number.")]
    public static TalvoraWatchReadResponse Read(
        string watchId,
        long afterSequence = 0,
        int maxEvents = 200,
        bool consume = true)
    {
        if (afterSequence < 0 || maxEvents < 0)
        {
            throw new ArgumentOutOfRangeException("afterSequence and maxEvents cannot be negative.");
        }

        var runtime = GetRuntime(watchId);
        List<TalvoraWatchEvent> result;

        if (consume)
        {
            result = new List<TalvoraWatchEvent>();

            while (runtime.Events.TryPeek(out var head) &&
                   head.Sequence <= afterSequence)
            {
                if (runtime.Events.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref runtime.QueuedEvents);
                }
            }

            while ((maxEvents == 0 || result.Count < maxEvents) &&
                   runtime.Events.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref runtime.QueuedEvents);
                if (item.Sequence > afterSequence)
                {
                    result.Add(item);
                }
            }
        }
        else
        {
            IEnumerable<TalvoraWatchEvent> query = runtime.Events
                .ToArray()
                .Where(item => item.Sequence > afterSequence)
                .OrderBy(item => item.Sequence);

            if (maxEvents > 0)
            {
                query = query.Take(maxEvents);
            }

            result = query.ToList();
        }

        return new TalvoraWatchReadResponse(
            watchId,
            result.Count,
            Math.Max(0, Volatile.Read(ref runtime.QueuedEvents)),
            Math.Max(0, Interlocked.Read(ref runtime.DroppedEvents)),
            result);
    }

    [McpServerTool(
        Name = "talvora_watch_wait",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchWaitResponse)),
     Description("Wait for the first queued filesystem event whose sequence is greater than afterSequence. timeoutSeconds=0 waits until cancellation.")]
    public static async Task<TalvoraWatchWaitResponse> Wait(
        string watchId,
        long afterSequence = 0,
        int timeoutSeconds = 30,
        int pollIntervalMilliseconds = 50,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || timeoutSeconds < 0 || pollIntervalMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                "afterSequence/timeoutSeconds cannot be negative and pollIntervalMilliseconds must be positive.");
        }

        var runtime = GetRuntime(watchId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();

        while (!timeout.IsCancellationRequested)
        {
            var item = runtime.Events
                .ToArray()
                .Where(value => value.Sequence > afterSequence)
                .OrderBy(value => value.Sequence)
                .FirstOrDefault();

            if (item is not null)
            {
                stopwatch.Stop();
                return new TalvoraWatchWaitResponse(
                    watchId,
                    true,
                    afterSequence,
                    stopwatch.ElapsedMilliseconds,
                    item);
            }

            try
            {
                await Task.Delay(pollIntervalMilliseconds, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        return new TalvoraWatchWaitResponse(
            watchId,
            false,
            afterSequence,
            stopwatch.ElapsedMilliseconds,
            null);
    }

    [McpServerTool(
        Name = "talvora_watch_stop",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWatchStopResponse)),
     Description("Stop and dispose a Talvora filesystem watcher. Missing watch IDs are handled idempotently.")]
    public static TalvoraWatchStopResponse Stop(string watchId)
    {
        if (!Watches.TryRemove(watchId, out var runtime))
        {
            return new TalvoraWatchStopResponse(watchId, false, false);
        }

        runtime.Dispose();
        return new TalvoraWatchStopResponse(watchId, true, true);
    }

    private static TalvoraWatchRuntime GetRuntime(string watchId)
    {
        if (string.IsNullOrWhiteSpace(watchId))
        {
            throw new ArgumentException("watchId is required.", nameof(watchId));
        }

        return Watches.TryGetValue(watchId, out var runtime)
            ? runtime
            : throw new KeyNotFoundException($"Talvora watcher was not found: {watchId}");
    }

    private static NotifyFilters ParseNotifyFilters(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return NotifyFilters.FileName |
                   NotifyFilters.DirectoryName |
                   NotifyFilters.LastWrite |
                   NotifyFilters.Size |
                   NotifyFilters.CreationTime;
        }

        var result = (NotifyFilters)0;
        foreach (var raw in values)
        {
            if (!Enum.TryParse<NotifyFilters>(raw, ignoreCase: true, out var parsed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(values),
                    $"Unknown FileSystemWatcher NotifyFilters value: {raw}");
            }

            result |= parsed;
        }

        return result;
    }

    private static string FormatException(Exception? exception)
    {
        if (exception is null)
        {
            return "Unknown FileSystemWatcher error.";
        }

        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" --> ", parts);
    }
}