using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private static readonly TimeSpan SessionIdleInterval = TimeSpan.FromSeconds(90);

    private sealed record ActiveWorklogItem(
        string ToolName, OperationNarrative Narrative,
        DesktopProgressEvidence? Evidence, long Sequence,
        WorklogSession Session);

    private sealed record PendingFailure(
        string ToolName, string Title, string Message,
        DesktopProgressEvidence? Evidence);

    private sealed record PendingCancellation(
        string ToolName, string Title, string Message,
        DesktopProgressEvidence? Evidence);

    private sealed class WorklogSession(
        string id, string actor, string? projectName)
    {
        public string Id { get; } = id;
        public string Actor { get; } = actor;
        public string? ProjectName { get; } = projectName;
        public DateTimeOffset BeganUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastActivityUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<WorklogTimelineEntry> Timeline { get; } = [];
        public int Steps { get; set; }
        public OperationCategory Category { get; set; } = OperationCategory.GenericExecution;
        public string ToolName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DesktopProgressEvidence? Evidence { get; set; }
        public DesktopProgressKind Kind { get; set; } = DesktopProgressKind.Running;
        public TimeSpan Elapsed { get; set; }
        public bool Closed { get; set; }
        public CancellationTokenSource? CloseDelay { get; set; }
        public PendingFailure? Failure { get; set; }
        public PendingCancellation? Cancellation { get; set; }
    }

    private readonly object _worklogGate = new();
    private readonly Dictionary<string, ActiveWorklogItem> _activeWorklog =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorklogSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private long _worklogSequence;

    private void TryBeginWorklog(
        string operationId, string toolName, OperationNarrative narrative,
        DesktopProgressEvidence? evidence, TimeSpan elapsed,
        IDictionary<string, JsonElement>? arguments = null,
        string? surfacePath = null, string? clientName = null,
        string? projectOverride = null)
    {
        try
        {
            BeginWorklog(operationId, toolName, narrative,
                evidence, elapsed, arguments, surfacePath,
                clientName, projectOverride);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Desktop worklog start failed without affecting the tool operation.");
        }
    }

    private void TryUpdateWorklogProgress(
        string operationId, string displayName, TimeSpan elapsed)
    {
        try
        {
            UpdateWorklogProgress(operationId, displayName, elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Desktop worklog progress update failed without affecting the tool operation.");
        }
    }

    private void TryEndWorklog(
        string operationId, DesktopProgressKind kind,
        string title, string message,
        DesktopProgressEvidence? evidence, TimeSpan elapsed)
    {
        try
        {
            EndWorklog(operationId, kind, title, message, evidence, elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Desktop worklog terminal update failed without affecting the tool operation.");
        }
    }

    private void BeginWorklog(
        string operationId, string toolName, OperationNarrative narrative,
        DesktopProgressEvidence? evidence, TimeSpan elapsed,
        IDictionary<string, JsonElement>? arguments,
        string? surfacePath, string? clientName, string? projectOverride)
    {
        lock (_worklogGate)
        {
            var session = GetOrCreateSession(
                arguments, surfacePath, clientName, projectOverride);
            session.CloseDelay?.Cancel();
            session.CloseDelay = null;
            session.Closed = false;
            session.LastActivityUtc = DateTimeOffset.UtcNow;
            session.Category = narrative.Category;
            _activeWorklog[operationId] = new ActiveWorklogItem(
                toolName, narrative, evidence, ++_worklogSequence, session);
            PublishSession(session, toolName, narrative.Titles.Running,
                WorklogCopy.RunningFallback, DesktopProgressKind.Started,
                elapsed, evidence);
        }
    }

    private void UpdateWorklogProgress(
        string operationId, string displayName, TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue(operationId, out var current))
            {
                return;
            }
            var latest = _activeWorklog.Values
                .Where(item => ReferenceEquals(item.Session, current.Session))
                .MaxBy(static item => item.Sequence);
            if (latest?.Sequence != current.Sequence)
            {
                return;
            }
            PublishSession(current.Session, current.ToolName,
                current.Narrative.Titles.Running,
                WorklogCopy.RunningFallback,
                DesktopProgressKind.Running, elapsed, current.Evidence);
        }
    }

    private void EndWorklog(
        string operationId, DesktopProgressKind kind,
        string title, string message,
        DesktopProgressEvidence? evidence, TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.Remove(operationId, out var ended))
            {
                return;
            }
            var session = ended.Session;
            session.LastActivityUtc = DateTimeOffset.UtcNow;
            session.Steps++;
            session.Timeline.Insert(0, new WorklogTimelineEntry(
                session.LastActivityUtc, ToWorklogState(kind), title));
            if (session.Timeline.Count > DesktopProgressProtocol.MaximumTimelineEntries)
            {
                session.Timeline.RemoveAt(session.Timeline.Count - 1);
            }
            if (kind == DesktopProgressKind.Failed)
            {
                session.Failure = new PendingFailure(
                    ended.ToolName, title, message, evidence);
            }
            else if (kind == DesktopProgressKind.Cancelled)
            {
                session.Cancellation = new PendingCancellation(
                    ended.ToolName, title, message, evidence);
            }

            var latest = _activeWorklog.Values
                .Where(item => ReferenceEquals(item.Session, session))
                .MaxBy(static item => item.Sequence);
            if (latest is not null)
            {
                session.Category = latest.Narrative.Category;
                PublishSession(session, latest.ToolName,
                    latest.Narrative.Titles.Running,
                    WorklogCopy.RunningFallback,
                    DesktopProgressKind.Running, elapsed, latest.Evidence);
                return;
            }

            session.Category = ended.Narrative.Category;
            if (session.Failure is { } failed)
            {
                PublishSession(session, failed.ToolName, failed.Title,
                    failed.Message, DesktopProgressKind.Failed,
                    elapsed, failed.Evidence);
            }
            else if (session.Cancellation is { } cancelled)
            {
                PublishSession(session, cancelled.ToolName, cancelled.Title,
                    cancelled.Message, DesktopProgressKind.Cancelled,
                    elapsed, cancelled.Evidence);
            }
            else
            {
                PublishSession(session, ended.ToolName, title,
                    message, kind, elapsed, evidence);
            }
            session.Failure = null;
            session.Cancellation = null;
            ScheduleClosure(session);
        }
    }

    private void PublishSession(
        WorklogSession session, string toolName, string title,
        string message, DesktopProgressKind kind,
        TimeSpan elapsed, DesktopProgressEvidence? evidence)
    {
        session.ToolName = toolName;
        session.Title = title;
        session.Message = ComposeWorklogMessage(message, evidence);
        session.Kind = kind;
        session.Elapsed = elapsed;
        session.Evidence = evidence;
        Enqueue(session.Id, toolName, title, session.Message,
            kind, elapsed, evidence);
    }

    private void ScheduleClosure(WorklogSession session)
    {
        session.CloseDelay?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(_deliveryStopCts.Token);
        session.CloseDelay = source;
        _ = CloseLaterAsync(session, source);
    }

    private async Task CloseLaterAsync(WorklogSession session, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(SessionIdleInterval, source.Token);
            lock (_worklogGate)
            {
                if (!ReferenceEquals(session.CloseDelay, source) ||
                    _activeWorklog.Values.Any(item => ReferenceEquals(item.Session, session)))
                {
                    return;
                }
                session.Closed = true;
                session.CloseDelay = null;
                Enqueue(session.Id, session.ToolName, session.Title,
                    session.Message, session.Kind, session.Elapsed, session.Evidence);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            source.Dispose();
        }
    }

    private WorklogSession GetOrCreateSession(
        IDictionary<string, JsonElement>? arguments, string? surfacePath,
        string? clientName, string? projectOverride)
    {
        var (projectRoot, projectName) = ResolveProject(arguments, projectOverride);
        var actor = ResolveActor(surfacePath, clientName);
        var key = string.Concat(projectRoot ?? "global", "\u0000", actor);
        if (!_sessions.TryGetValue(key, out var session))
        {
            var id = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            session = new WorklogSession(id, actor, projectName);
            _sessions[key] = session;
        }
        else if (session.Closed ||
                 (!_activeWorklog.Values.Any(item => ReferenceEquals(item.Session, session)) &&
                  DateTimeOffset.UtcNow - session.LastActivityUtc > SessionIdleInterval))
        {
            session.CloseDelay?.Cancel();
            session.CloseDelay = null;
            session.Closed = false;
            session.BeganUtc = DateTimeOffset.UtcNow;
            session.Timeline.Clear();
            session.Steps = 0;
            session.Failure = null;
            session.Cancellation = null;
        }
        return session;
    }

    private static string ResolveActor(string? surfacePath, string? clientName)
    {
        if (clientName?.Contains("codex", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Codex";
        }
        if (clientName?.Contains("claude", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Claude";
        }
        if (surfacePath?.EndsWith("/mcp/dev", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "ChatGPT · Dev";
        }
        if (surfacePath?.EndsWith("/mcp/admin", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "ChatGPT · Yönetim";
        }
        return "Talvora";
    }

    private static (string? Root, string? Name) ResolveProject(
        IDictionary<string, JsonElement>? arguments, string? projectOverride)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectOverride))
        {
            candidates.Add(projectOverride);
        }
        if (arguments is not null)
        {
            foreach (var key in new[]
                     {
                         "workspaceRoot", "workingDirectory", "repositoryPath", "root", "path",
                     })
            {
                if (arguments.TryGetValue(key, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    candidates.Add(value.GetString()!);
                }
            }
        }
        foreach (var value in candidates)
        {
            try
            {
                var absolute = Path.GetFullPath(value);
                var origin = Directory.Exists(absolute)
                    ? new DirectoryInfo(absolute)
                    : new FileInfo(absolute).Directory;
                if (origin is null)
                {
                    continue;
                }
                for (var folder = origin; folder is not null; folder = folder.Parent)
                {
                    if (Directory.Exists(Path.Combine(folder.FullName, ".git")) ||
                        File.Exists(Path.Combine(folder.FullName, ".git")) ||
                        File.Exists(Path.Combine(folder.FullName, "package.json")) ||
                        File.Exists(Path.Combine(folder.FullName, "Cargo.toml")) ||
                        File.Exists(Path.Combine(folder.FullName, "global.json")) ||
                        Directory.EnumerateFiles(folder.FullName, "*.slnx").Any() ||
                        Directory.EnumerateFiles(folder.FullName, "*.sln").Any())
                    {
                        return (folder.FullName, folder.Name);
                    }
                }
                return (origin.FullName, origin.Name);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
        return (null, null);
    }

    private DesktopWorklogFrame? CreateWorklogFrame(
        string sessionId, DesktopProgressKind kind, string title,
        string message, TimeSpan elapsed, DesktopProgressEvidence? evidence,
        long sequence)
    {
        lock (_worklogGate)
        {
            var session = _sessions.Values.FirstOrDefault(
                item => string.Equals(item.Id, sessionId, StringComparison.Ordinal));
            if (session is null)
            {
                return null;
            }
            var isTerminal = kind is DesktopProgressKind.Completed or
                DesktopProgressKind.Failed or DesktopProgressKind.Cancelled;
            var outcome = isTerminal
                ? new WorklogOutcome(null,
                    kind == DesktopProgressKind.Completed ? string.Empty : message, null)
                : null;
            return new DesktopWorklogFrame(
                session.Id, sequence, DateTimeOffset.UtcNow,
                new WorklogSessionHeader(session.ProjectName, session.Actor),
                new WorklogStep((WorklogStepCategory)session.Category,
                    ToWorklogState(kind), title, null, outcome,
                    Math.Max(0, elapsed.TotalSeconds)),
                session.Timeline.ToArray(),
                new WorklogTotals(session.Steps,
                    Math.Max(0, (DateTimeOffset.UtcNow - session.BeganUtc).TotalSeconds),
                    0, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                evidence,
                session.Actor switch
                {
                    "Codex" => 20,
                    "Claude" => 60,
                    _ => 15,
                });
        }
    }

    private bool IsWorklogSessionClosed(string sessionId)
    {
        lock (_worklogGate)
        {
            return _sessions.Values.Any(session =>
                string.Equals(session.Id, sessionId, StringComparison.Ordinal) &&
                session.Closed);
        }
    }

    private static WorklogStepState ToWorklogState(DesktopProgressKind kind) =>
        kind switch
        {
            DesktopProgressKind.Completed or DesktopProgressKind.Info => WorklogStepState.Succeeded,
            DesktopProgressKind.Failed or DesktopProgressKind.Warning => WorklogStepState.Failed,
            DesktopProgressKind.Cancelled => WorklogStepState.Cancelled,
            _ => WorklogStepState.Running,
        };

    private string ComposeWorklogMessage(
        string current, DesktopProgressEvidence? evidence) => current.Trim();
}
