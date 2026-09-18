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
    string? Message);

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
     Description("Query any local Windows Event Log with an XPath expression and return structured records. newestFirst=true reads newest-to-oldest. No log/provider/event-id allowlist is applied.")]
    public static TalvoraEventLogQueryResponse Query(
        string logName,
        string xpath = "*",
        int maxEvents = 100,
        bool newestFirst = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
        if (maxEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEvents), "maxEvents must be greater than zero.");
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
                TryFormatDescription(record)));
        }

        return new TalvoraEventLogQueryResponse(
            logName,
            xpath,
            newestFirst,
            maxEvents,
            events);
    }

    private static string? TryFormatDescription(EventRecord record)
    {
        try
        {
            return record.FormatDescription();
        }
        catch (EventLogException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
