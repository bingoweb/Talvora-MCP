using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraEventLogDescriptor(
    string LogName);

public sealed record TalvoraEventLogListResponse(
    IReadOnlyList<TalvoraEventLogDescriptor> Logs);

public sealed record TalvoraEventLogRecordData(
    string? LogName,
    string? ProviderName,
    int Id,
    long? RecordId,
    DateTime? TimeCreated,
    byte? Level,
    int? ProcessId,
    int? ThreadId,
    string? MachineName,
    string? UserId,
    string? Message,
    bool MessageTruncated);

public sealed record TalvoraEventLogQueryResponse(
    string LogName,
    string XPath,
    bool NewestFirst,
    int MaxEvents,
    IReadOnlyList<TalvoraEventLogRecordData> Events);

[SupportedOSPlatform("windows")]
[McpServerToolType]
public static class EventLogTools
{
    private const int MaximumEventsPerQuery = 500;
    private const int MaximumMessageCharacters = 16 * 1024;

    [McpServerTool(
        Name = "talvora_eventlog_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEventLogListResponse)),
     Description("List local Windows Event Log names. Optional query filters log names case-insensitively. No log-name allowlist is applied.")]
    public static TalvoraEventLogListResponse List(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logs = EventLogSession.GlobalSession
            .GetLogNames()
            .Where(name =>
                string.IsNullOrWhiteSpace(query) ||
                name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .Select(name => new TalvoraEventLogDescriptor(name))
            .ToArray();

        return new TalvoraEventLogListResponse(logs);
    }

    [McpServerTool(
        Name = "talvora_eventlog_query",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEventLogQueryResponse)),
     Description("Query any local Windows Event Log with an XPath expression and return structured records. newestFirst=true reads newest-to-oldest. maxEvents must be between 1 and 500. Formatted event messages are capped at 16 KiB and report messageTruncated=true when shortened. No log/provider/event-id allowlist is applied.")]
    public static TalvoraEventLogQueryResponse Query(
        string logName,
        string xpath = "*",
        int maxEvents = 100,
        bool newestFirst = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
        if (maxEvents <= 0 ||
            maxEvents > MaximumEventsPerQuery)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEvents),
                $"maxEvents must be between 1 and {MaximumEventsPerQuery}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var query = new EventLogQuery(logName, PathType.LogName, xpath)
        {
            ReverseDirection = newestFirst,
            TolerateQueryErrors = false,
            Session = EventLogSession.GlobalSession,
        };

        using var reader = new EventLogReader(query);
        var events = new List<TalvoraEventLogRecordData>(Math.Min(maxEvents, 1024));

        while (events.Count < maxEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            var (message, messageTruncated) =
                TryFormatDescription(record);

            events.Add(new TalvoraEventLogRecordData(
                record.LogName,
                record.ProviderName,
                record.Id,
                record.RecordId,
                record.TimeCreated,
                record.Level,
                record.ProcessId,
                record.ThreadId,
                record.MachineName,
                record.UserId?.Value,
                message,
                messageTruncated));
        }

        return new TalvoraEventLogQueryResponse(
            logName,
            xpath,
            newestFirst,
            maxEvents,
            events);
    }

    private static (string? Message, bool Truncated) TryFormatDescription(
        EventRecord record)
    {
        try
        {
            var message =
                record.FormatDescription();
            if (message is null ||
                message.Length <= MaximumMessageCharacters)
            {
                return (message, false);
            }

            return (
                message[..MaximumMessageCharacters],
                true);
        }
        catch (EventLogException)
        {
            return (null, false);
        }
        catch (InvalidOperationException)
        {
            return (null, false);
        }
    }
}
