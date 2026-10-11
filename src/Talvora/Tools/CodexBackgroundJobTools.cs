using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora;

namespace Talvora.Tools;

public sealed record TalvoraCodexJobResponse(
    string JobId,
    string State,
    string WorkingDirectory,
    DateTime CreatedAtUtc,
    DateTime? FinishedAtUtc,
    long ElapsedMilliseconds,
    string? SessionId,
    int CompletedCommands,
    int FailedCommands,
    int McpToolCalls,
    IReadOnlyList<string> RecentActivities,
    string? Error,
    TalvoraCodexCliExecResponse? Result,
    DateTime? LastProgressAtUtc = null);

public sealed record TalvoraCodexJobListResponse(
    int Count,
    IReadOnlyList<TalvoraCodexJobResponse> Jobs);

/// <summary>
/// Asynchronous Codex job supervision independent of individual MCP requests.
/// Jobs keep running through a client/tunnel disconnect while the Talvora
/// Windows service remains alive. Cancellation terminates the helper tree.
/// </summary>
[McpServerToolType]
public static class CodexBackgroundJobTools
{
    internal static TalvoraDesktopProgressNotifier? DesktopNotifier
    {
        get;
        set;
    }

    private static readonly ConcurrentDictionary<string, Job> Jobs =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class Job
    {
        private readonly object _gate = new();
        private readonly string _prompt;
        public Job(string workingDirectory, string prompt)
        {
            JobId = Guid.NewGuid().ToString("N");
            WorkingDirectory = workingDirectory;
            _prompt = prompt;
            CreatedAtUtc = DateTime.UtcNow;
        }
        public string JobId { get; }
        public string WorkingDirectory { get; }
        public DateTime CreatedAtUtc { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? ExecutionTask { get; set; }

        private string _state = "queued";
        private DateTime? _finishedAt;
        private string? _sessionId;
        private int _commands;
        private int _failedCommands;
        private int _mcpCalls;
        private string[] _activities = [];
        private string? _error;
        private TalvoraCodexCliExecResponse? _result;
        private DateTime? _lastProgressAt;
        private DateTime _lastDesktopUpdate = DateTime.MinValue;

        public bool SetRunning()
        {
            lock (_gate)
            {
                if (_state != "queued") { return false; }
                _state = "running";
                return true;
            }
        }

        public void OnProgress(CodexCliEventResult progress)
        {
            int commands;
            int mcpCalls;
            string? activity;
            bool notify;
            lock (_gate)
            {
                _sessionId = progress.SessionId ?? _sessionId;
                _commands = Math.Max(_commands, progress.CompletedCommands);
                _failedCommands = Math.Max(
                    _failedCommands, progress.FailedCommands);
                _mcpCalls = Math.Max(_mcpCalls, progress.McpToolCalls);
                _activities = progress.RecentActivities
                    .Select(text => CodexCliInvocationPolicy.SanitizeFinalAnswer(
                        text, _prompt))
                    .ToArray();
                _lastProgressAt = DateTime.UtcNow;
                commands = _commands;
                mcpCalls = _mcpCalls;
                activity = _activities.LastOrDefault();
                notify = DateTime.UtcNow - _lastDesktopUpdate >
                    TimeSpan.FromSeconds(4);
                if (notify) { _lastDesktopUpdate = DateTime.UtcNow; }
            }
            if (notify)
            {
                DesktopNotifier?.UpdateCodexJob(
                    JobId, commands, mcpCalls, activity,
                    DateTime.UtcNow - CreatedAtUtc);
            }
        }

        public void Complete(TalvoraCodexCliExecResponse result)
        {
            string state;
            lock (_gate)
            {
                _result = result;
                _sessionId = result.SessionId ?? _sessionId;
                _commands = Math.Max(_commands, result.CompletedCommands);
                _failedCommands = Math.Max(
                    _failedCommands, result.FailedCommands);
                _mcpCalls = Math.Max(_mcpCalls, result.McpToolCalls);
                if (result.RecentActivities is { Count: > 0 } &&
                    !result.StandardOutputTruncated)
                {
                    _activities = result.RecentActivities.ToArray();
                }
                _lastProgressAt = DateTime.UtcNow;
                _state = Cancellation.IsCancellationRequested
                    ? "cancelled"
                    : result.TimedOut
                        ? "timed_out"
                        : result.ExitCode == 0
                            ? "completed"
                            : "failed";
                _finishedAt = DateTime.UtcNow;
                _error = result.ExitCode == 0
                    ? null
                    : result.ErrorDetail ?? result.StandardError;
                state = _state;
            }
            DesktopNotifier?.EndCodexJob(
                JobId, state, DateTime.UtcNow - CreatedAtUtc);
        }

        public void Fail(Exception exception)
        {
            string state;
            lock (_gate)
            {
                _state = exception is OperationCanceledException ||
                    Cancellation.IsCancellationRequested
                    ? "cancelled"
                    : "failed";
                _finishedAt = DateTime.UtcNow;
                _error = _state == "cancelled"
                    ? "The Codex task was cancelled."
                    : CodexCliInvocationPolicy.SanitizeFinalAnswer(
                        exception.Message, _prompt);
                state = _state;
            }
            DesktopNotifier?.EndCodexJob(
                JobId, state, DateTime.UtcNow - CreatedAtUtc);
        }

        public void Cancel()
        {
            var shouldCancel = false;
            lock (_gate)
            {
                if (_state is "queued" or "running")
                {
                    _state = "cancelling";
                    shouldCancel = true;
                }
            }
            if (shouldCancel) { Cancellation.Cancel(); }
        }

        public TalvoraCodexJobResponse Snapshot(bool includeResult)
        {
            lock (_gate)
            {
                var end = _finishedAt ?? DateTime.UtcNow;
                return new TalvoraCodexJobResponse(
                    JobId, _state, WorkingDirectory, CreatedAtUtc,
                    _finishedAt, (long)(end - CreatedAtUtc).TotalMilliseconds,
                    _sessionId, _commands, _failedCommands, _mcpCalls,
                    _activities.ToArray(), _error,
                    includeResult ? _result : null,
                    _lastProgressAt);
            }
        }
    }

    [McpServerTool(
        Name = "talvora_codex_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobResponse)),
     Description("Start a Codex coding job in the background and return immediately. The selected normal-user developer profile controls workspace or trusted access. Poll with talvora_codex_status; cancel with talvora_codex_cancel.")]
    public static TalvoraCodexJobResponse Start(
        string workingDirectory,
        string prompt,
        string model = "gpt-6.1-sol",
        string reasoningEffort = "medium",
        string sandbox = "auto",
        int timeoutSeconds = 7200,
        string? resumeSessionId = null,
        bool preserveSession = true,
        bool networkAccess = true,
        string approvalMode = "automatic",
        string[]? additionalWritableDirectories = null,
        bool allowNonGitWorkspace = true,
        bool allowFullAccess = false,
        string windowsSandboxMode = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var job = new Job(Path.GetFullPath(workingDirectory), prompt);
        if (!Jobs.TryAdd(job.JobId, job))
        {
            throw new InvalidOperationException("Could not register Codex job.");
        }
        DesktopNotifier?.BeginCodexJob(job.JobId, job.WorkingDirectory);
        job.ExecutionTask = Task.Run(async () =>
        {
            if (!job.SetRunning())
            {
                job.Fail(new OperationCanceledException());
                return;
            }
            try
            {
                var result = await CodexCliTools.ExecuteCore(
                    job.WorkingDirectory, prompt, model, reasoningEffort,
                    sandbox, timeoutSeconds, resumeSessionId, preserveSession,
                    networkAccess, approvalMode, additionalWritableDirectories,
                    allowNonGitWorkspace, allowFullAccess, windowsSandboxMode,
                    job.Cancellation.Token, job.OnProgress).ConfigureAwait(false);
                job.Complete(result);
            }
            catch (Exception ex)
            {
                job.Fail(ex);
            }
        });
        PruneCompleted();
        return job.Snapshot(includeResult: false);
    }

    [McpServerTool(
        Name = "talvora_codex_status",
        ReadOnly = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobResponse)),
     Description("Read live Codex job status, session ID, elapsed time, completed shell/MCP calls and recent activities. Set includeResult=true to retrieve its completed final answer.")]
    public static TalvoraCodexJobResponse Status(
        string jobId, bool includeResult = false) =>
        GetJob(jobId).Snapshot(includeResult);

    [McpServerTool(
        Name = "talvora_codex_list",
        ReadOnly = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobListResponse)),
     Description("List recent Codex background jobs and their running/completed/cancelled states.")]
    public static TalvoraCodexJobListResponse List(int maxResults = 50)
    {
        if (maxResults is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }
        var jobs = Jobs.Values
            .OrderByDescending(job => job.CreatedAtUtc)
            .Take(maxResults)
            .Select(job => job.Snapshot(includeResult: false))
            .ToArray();
        return new TalvoraCodexJobListResponse(jobs.Length, jobs);
    }

    [McpServerTool(
        Name = "talvora_codex_cancel",
        Destructive = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobResponse)),
     Description("Request cancellation of a running Codex background job; the user process runner terminates its child process tree. Poll status until cancelled.")]
    public static TalvoraCodexJobResponse Cancel(string jobId)
    {
        var job = GetJob(jobId);
        job.Cancel();
        return job.Snapshot(includeResult: false);
    }

    private static Job GetJob(string jobId) =>
        Jobs.TryGetValue(jobId, out var job)
            ? job
            : throw new KeyNotFoundException($"Codex job not found: {jobId}");

    private static void PruneCompleted()
    {
        if (Jobs.Count <= 120) { return; }
        foreach (var job in Jobs.Values
            .Where(item => item.Snapshot(false).FinishedAtUtc is not null)
            .OrderBy(item => item.CreatedAtUtc)
            .Take(Math.Max(0, Jobs.Count - 100)))
        {
            if (Jobs.TryRemove(job.JobId, out var removed))
            {
                removed.Cancellation.Dispose();
            }
        }
    }
}
