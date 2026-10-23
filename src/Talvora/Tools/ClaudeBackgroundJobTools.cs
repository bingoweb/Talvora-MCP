using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraClaudeJobResponse(
    string JobId, string State, string WorkingDirectory, DateTime CreatedAtUtc,
    DateTime? FinishedAtUtc, long ElapsedMilliseconds, string? SessionId,
    int ToolCalls, int FailedTools, int PermissionDenials,
    IReadOnlyList<string> RecentActivities, DateTime? LastProgressAtUtc,
    string? Error, TalvoraClaudeExecResponse? Result);

public sealed record TalvoraClaudeJobListResponse(
    int Count, IReadOnlyList<TalvoraClaudeJobResponse> Jobs);

/// <summary>Claude MCP adapter for the shared persistent agent job registry.</summary>
[McpServerToolType]
public static class ClaudeBackgroundJobTools
{
    internal static TalvoraDesktopProgressNotifier? DesktopNotifier { get; set; }
    private static AgentJobRegistry Registry => AgentJobRegistry.Shared;

    private sealed class Runtime(AgentJobRegistry.Job job)
    {
        private readonly object _gate = new();
        private ClaudeCliProgress? _progress;
        private TalvoraClaudeExecResponse? _result;
        private string? _lastCodeResultId;

        internal void OnProgress(ClaudeCliProgress progress)
        {
            ClaudeAppliedCodeResult? applied = null;
            lock (_gate)
            {
                _progress = progress;
                if (progress.CodeChange is { } change && change.Id != _lastCodeResultId)
                {
                    _lastCodeResultId = change.Id;
                    applied = change;
                }
            }
            Registry.Progress(job, sessionId: progress.SessionId,
                toolCalls: progress.ToolCalls, failedTools: progress.FailedTools,
                permissionDenials: progress.PermissionDenials);
            if (applied is not null)
            {
                Notify(() => DesktopNotifier?.ObserveClaudeCodeResult(
                    job.Metadata.JobId, job.Metadata.WorkingDirectory,
                    applied, DateTime.UtcNow - job.Metadata.CreatedAtUtc));
            }
        }

        internal async Task Execute(
            Func<CancellationToken, Action<ClaudeCliProgress>, Task<TalvoraClaudeExecResponse>> run)
        {
            Registry.Start(job);
            if (job.Metadata.State != "running") return;
            using var heartbeatCancellation = new CancellationTokenSource();
            var heartbeat = Heartbeat(heartbeatCancellation.Token);
            try
            {
                var result = await run(job.CancellationToken, OnProgress).ConfigureAwait(false);
                lock (_gate) { _result = result; }
                Registry.Progress(job, sessionId: result.SessionId,
                    toolCalls: result.ToolCalls, failedTools: result.FailedTools,
                    permissionDenials: result.PermissionDenials);
                var state = job.CancellationToken.IsCancellationRequested ? "cancelled" :
                    result.TimedOut ? "timed_out" : result.Success ? "completed" : "failed";
                Registry.Finish(job, state, result.Success ? null : result.Error);
            }
            catch (Exception ex)
            {
                var cancelled = job.CancellationToken.IsCancellationRequested ||
                    ex is OperationCanceledException;
                Registry.Finish(job, cancelled ? "cancelled" : "failed", cancelled
                    ? "Claude Code task cancelled."
                    : "Claude Code task failed. Check installation, login, working directory and CLI options.");
            }
            finally
            {
                heartbeatCancellation.Cancel();
                try { await heartbeat.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        private async Task Heartbeat(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var snapshot = Snapshot(false);
                Notify(() => DesktopNotifier?.UpdateClaudeJob(
                    job.Metadata.JobId, snapshot.ToolCalls,
                    DateTime.UtcNow - job.Metadata.CreatedAtUtc,
                    snapshot.FailedTools, snapshot.PermissionDenials,
                    snapshot.RecentActivities.LastOrDefault()));
            }
        }

        internal void OnTerminal(AgentJobMetadata metadata)
        {
            try
            {
                ClaudeCliProgress? progress;
                lock (_gate) { progress = _progress; }
                DesktopNotifier?.EndClaudeJob(metadata.JobId, metadata.State,
                    (metadata.FinishedAtUtc ?? DateTime.UtcNow) - metadata.CreatedAtUtc,
                    metadata.ToolCalls, metadata.FailedTools,
                    metadata.PermissionDenials, progress?.FinalAnswer);
            }
            catch (Exception)
            {
                // Failure to deliver desktop progress must not affect job execution.
            }
        }

        internal TalvoraClaudeJobResponse Snapshot(bool includeResult)
        {
            lock (_gate)
            {
                return FromMetadata(job.Metadata, _progress?.RecentActivities ?? [],
                    includeResult ? _result : null);
            }
        }
    }

    private static TalvoraClaudeJobResponse FromMetadata(
        AgentJobMetadata metadata, IReadOnlyList<string> activities,
        TalvoraClaudeExecResponse? result) =>
        new(metadata.JobId, metadata.State, metadata.WorkingDirectory,
            metadata.CreatedAtUtc, metadata.FinishedAtUtc,
            (long)((metadata.FinishedAtUtc ?? DateTime.UtcNow) -
                metadata.CreatedAtUtc).TotalMilliseconds, metadata.SessionId,
            metadata.ToolCalls, metadata.FailedTools, metadata.PermissionDenials,
            activities, metadata.LastProgressAtUtc, metadata.Error, result);

    private static TalvoraClaudeJobResponse Snapshot(
        AgentJobRegistry.Job job, bool includeResult) =>
        job.AdapterState is Runtime live
            ? live.Snapshot(includeResult)
            : FromMetadata(job.Metadata, [], null);

    private static void Notify(Action action)
    {
        try { action(); }
        catch (Exception) { /* Desktop delivery must never affect work. */ }
    }

    [McpServerTool(Name = "talvora_claude_start", Destructive = true, Idempotent = false,
        OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Start an optional Claude Code coding job and return immediately. Runs as the logged-on user, survives MCP disconnects, supports saved session resume. auto is the native unattended default; bypassPermissions is explicit opt-in.")]
    public static TalvoraClaudeJobResponse Start(
        string workingDirectory, string prompt, string? model = null, string? effort = null,
        string permissionMode = "auto", int timeoutSeconds = 7200, string? resumeSessionId = null,
        string[]? additionalDirectories = null, string[]? extraArguments = null,
        Dictionary<string, string?>? environment = null, bool allowParallel = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        _ = ClaudeCliArguments.Create(prompt, model, effort, permissionMode,
            resumeSessionId, additionalDirectories, extraArguments);
        if (timeoutSeconds is < 1 or > 43200)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        var cwd = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(cwd)) throw new DirectoryNotFoundException(cwd);
        Runtime? runtime = null;
        var entry = Registry.Create("claude", cwd, allowParallel,
            sessionId: resumeSessionId, onTerminal: metadata => runtime?.OnTerminal(metadata));
        runtime = new Runtime(entry);
        entry.AdapterState = runtime;
        Notify(() => DesktopNotifier?.BeginClaudeJob(entry.Metadata.JobId, entry.Metadata.WorkingDirectory));
        entry.ExecutionTask = Task.Run(() => runtime.Execute((token, progress) =>
            ClaudeCliTools.ExecuteCore(cwd, prompt, model, effort, permissionMode,
                timeoutSeconds, resumeSessionId, additionalDirectories, extraArguments,
                environment, token, progress,
                (pid, started) => Registry.RecordProcess(entry, pid, started))));
        return Snapshot(entry, false);
    }

    [McpServerTool(Name = "talvora_claude_status", ReadOnly = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Get persisted Claude Code job state, session ID, counters and failure details. includeResult retrieves the result while the service is running.")]
    public static TalvoraClaudeJobResponse Status(string jobId, bool includeResult = false) =>
        Snapshot(Registry.Get("claude", jobId), includeResult);

    [McpServerTool(Name = "talvora_claude_list", ReadOnly = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobListResponse)),
     Description("List recent Claude Code jobs, including orphaned jobs after restart.")]
    public static TalvoraClaudeJobListResponse List(int maxResults = 50)
    {
        var jobs = Registry.List("claude", maxResults).Select(job =>
            Snapshot(job, false)).ToArray();
        return new(jobs.Length, jobs);
    }

    [McpServerTool(Name = "talvora_claude_cancel", Destructive = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Cancel Claude Code work and safely terminate a verified orphan process tree.")]
    public static TalvoraClaudeJobResponse Cancel(string jobId)
    {
        var job = Registry.Get("claude", jobId);
        Registry.Cancel(job);
        return Snapshot(job, false);
    }
}
