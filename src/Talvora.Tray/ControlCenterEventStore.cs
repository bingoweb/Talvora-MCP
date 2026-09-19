using System.IO;
using System.Text;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal enum ControlCenterEventSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

internal sealed record ControlCenterEventRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset FirstOccurredAtUtc { get; init; }
    public DateTimeOffset LastOccurredAtUtc { get; init; }
    public ControlCenterEventSeverity Severity { get; init; }
    public string Category { get; init; } = "general";
    public string? McpId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string DedupKey { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
}

internal sealed record ControlCenterEventDocument
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ControlCenterEventRecord> Events { get; init; } =
        Array.Empty<ControlCenterEventRecord>();
}

internal static class ControlCenterEventStore
{
    private const int MaxRecords = 500;
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(6);
    private static readonly Mutex CrossProcessGate =
        new(false, @"Local\Talvora.ControlCenter.EventStore");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string PathName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "events.json");

    public static void Record(
        ControlCenterEventSeverity severity,
        string category,
        string title,
        string detail,
        string? mcpId = null,
        string? dedupKey = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var now = occurredAtUtc ?? DateTimeOffset.UtcNow;
        var normalizedTitle = Normalize(title, 180);
        var normalizedDetail = Normalize(detail, 1200);
        var normalizedKey = string.IsNullOrWhiteSpace(dedupKey)
            ? BuildDefaultDedupKey(category, mcpId, normalizedTitle)
            : Normalize(dedupKey, 240);

        var gateTaken = false;
        try
        {
            try
            {
                gateTaken = CrossProcessGate.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                gateTaken = true;
            }

            if (!gateTaken)
            {
                return;
            }

            var document = ReadUnsafe();
            var updated = ApplyEvent(
                document,
                new ControlCenterEventRecord
                {
                    FirstOccurredAtUtc = now,
                    LastOccurredAtUtc = now,
                    Severity = severity,
                    Category = Normalize(category, 80),
                    McpId = string.IsNullOrWhiteSpace(mcpId)
                        ? null
                        : Normalize(mcpId, 80),
                    Title = normalizedTitle,
                    Detail = normalizedDetail,
                    DedupKey = normalizedKey,
                    Count = 1,
                },
                now);

            SaveUnsafe(updated);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException)
        {
            FileLog.Write(
                TrayLog.PathName,
                "Structured Control Center event could not be persisted",
                ex);
        }
        finally
        {
            if (gateTaken)
            {
                try
                {
                    CrossProcessGate.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
        }
    }

    public static IReadOnlyList<ControlCenterEventRecord> ReadRecent(
        TimeSpan window,
        int maxRecords = 12,
        ControlCenterEventSeverity? minimumSeverity = null)
    {
        maxRecords = Math.Clamp(maxRecords, 1, 100);

        var gateTaken = false;
        try
        {
            try
            {
                gateTaken = CrossProcessGate.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                gateTaken = true;
            }

            if (!gateTaken)
            {
                return Array.Empty<ControlCenterEventRecord>();
            }

            var now = DateTimeOffset.UtcNow;
            var source = ReadUnsafe();
            var document = Prune(source, now);

            if (document.Events.Count != source.Events.Count)
            {
                SaveUnsafe(document);
            }

            return document.Events
                .Where(entry => entry.LastOccurredAtUtc >= now.Subtract(window))
                .Where(entry =>
                    minimumSeverity is null ||
                    entry.Severity >= minimumSeverity.Value)
                .OrderByDescending(entry => entry.LastOccurredAtUtc)
                .Take(maxRecords)
                .ToArray();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            FileLog.Write(
                TrayLog.PathName,
                "Structured Control Center events could not be read",
                ex);
            return Array.Empty<ControlCenterEventRecord>();
        }
        finally
        {
            if (gateTaken)
            {
                try
                {
                    CrossProcessGate.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
        }
    }

    internal static void AssertPolicyContract()
    {
        var now = new DateTimeOffset(
            2026,
            9,
            19,
            12,
            0,
            0,
            TimeSpan.Zero);

        var empty = new ControlCenterEventDocument();
        var first = new ControlCenterEventRecord
        {
            FirstOccurredAtUtc = now,
            LastOccurredAtUtc = now,
            Severity = ControlCenterEventSeverity.Warning,
            Category = "recovery",
            McpId = "gitea",
            Title = "Gitea kurtarma denemesi başarısız",
            Detail = "İlk hata",
            DedupKey = "recovery:gitea",
            Count = 1,
        };

        var once = ApplyEvent(empty, first, now);
        var twice = ApplyEvent(
            once,
            first with
            {
                Id = Guid.NewGuid().ToString("N"),
                FirstOccurredAtUtc = now.AddMinutes(2),
                LastOccurredAtUtc = now.AddMinutes(2),
                Detail = "İkinci hata",
            },
            now.AddMinutes(2));

        if (twice.Events.Count != 1 ||
            twice.Events[0].Count != 2 ||
            twice.Events[0].Detail != "İkinci hata")
        {
            throw new InvalidOperationException(
                "Structured event dedup contract failed.");
        }

        var oldInfo = new ControlCenterEventRecord
        {
            FirstOccurredAtUtc = now.AddDays(-5),
            LastOccurredAtUtc = now.AddDays(-5),
            Severity = ControlCenterEventSeverity.Info,
            Category = "operation",
            Title = "Eski bilgi",
            Detail = string.Empty,
            DedupKey = "old-info",
            Count = 1,
        };

        var pruned = Prune(
            new ControlCenterEventDocument
            {
                Events = twice.Events.Concat([oldInfo]).ToArray(),
            },
            now);

        if (pruned.Events.Any(entry => entry.DedupKey == "old-info"))
        {
            throw new InvalidOperationException(
                "Structured event retention contract failed.");
        }
    }

    private static ControlCenterEventDocument ApplyEvent(
        ControlCenterEventDocument source,
        ControlCenterEventRecord incoming,
        DateTimeOffset now)
    {
        var events = Prune(source, now).Events.ToList();

        var duplicateIndex = events.FindIndex(entry =>
            string.Equals(
                entry.DedupKey,
                incoming.DedupKey,
                StringComparison.OrdinalIgnoreCase) &&
            now - entry.LastOccurredAtUtc <= DedupWindow);

        if (duplicateIndex >= 0)
        {
            var existing = events[duplicateIndex];
            events[duplicateIndex] = existing with
            {
                LastOccurredAtUtc = incoming.LastOccurredAtUtc,
                Severity = incoming.Severity > existing.Severity
                    ? incoming.Severity
                    : existing.Severity,
                Title = incoming.Title,
                Detail = incoming.Detail,
                Count = existing.Count + 1,
            };
        }
        else
        {
            events.Add(incoming);
        }

        events = events
            .OrderByDescending(entry => entry.LastOccurredAtUtc)
            .Take(MaxRecords)
            .ToList();

        return new ControlCenterEventDocument
        {
            UpdatedAtUtc = now,
            Events = events,
        };
    }

    private static ControlCenterEventDocument Prune(
        ControlCenterEventDocument source,
        DateTimeOffset now)
    {
        var retained = source.Events
            .Where(entry =>
                entry.LastOccurredAtUtc >= now.Subtract(
                    GetRetention(entry.Severity)))
            .OrderByDescending(entry => entry.LastOccurredAtUtc)
            .Take(MaxRecords)
            .ToArray();

        return source with
        {
            UpdatedAtUtc = now,
            Events = retained,
        };
    }

    private static TimeSpan GetRetention(ControlCenterEventSeverity severity) =>
        severity switch
        {
            ControlCenterEventSeverity.Info => TimeSpan.FromDays(3),
            ControlCenterEventSeverity.Warning => TimeSpan.FromDays(14),
            _ => TimeSpan.FromDays(30),
        };

    private static ControlCenterEventDocument ReadUnsafe()
    {
        if (!File.Exists(PathName))
        {
            return new ControlCenterEventDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<ControlCenterEventDocument>(
                       File.ReadAllText(PathName, Encoding.UTF8),
                       JsonOptions)
                   ?? new ControlCenterEventDocument();
        }
        catch (JsonException)
        {
            return new ControlCenterEventDocument();
        }
    }

    private static void SaveUnsafe(ControlCenterEventDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        AtomicFile.WriteAllTextAsync(
                PathName,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                createBackup: false,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static string BuildDefaultDedupKey(
        string category,
        string? mcpId,
        string title) =>
        $"{category}:{mcpId ?? "global"}:{title}".ToLowerInvariant();

    private static string Normalize(string value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength];
    }
}