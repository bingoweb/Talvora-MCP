using System.Collections.Concurrent;
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
public sealed record TalvoraClaudeJobListResponse(int Count, IReadOnlyList<TalvoraClaudeJobResponse> Jobs);

/// <summary>In-memory service-lifetime jobs, independent of an MCP connection.</summary>
[McpServerToolType]
public static class ClaudeBackgroundJobTools
{
    internal static TalvoraDesktopProgressNotifier? DesktopNotifier { get; set; }
    private static readonly ConcurrentDictionary<string, Job> Jobs = new(StringComparer.Ordinal);

    private sealed class Job(string workingDirectory, string? resumeSessionId)
    {
        private readonly object _gate = new();
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal string WorkingDirectory { get; } = workingDirectory;
        internal DateTime Created { get; } = DateTime.UtcNow;
        internal CancellationTokenSource Cancellation { get; } = new();
        private string _state = "queued";
        private DateTime? _finished;
        private ClaudeCliProgress? _progress;
        private string? _session = resumeSessionId;
        private string? _error;
        private TalvoraClaudeExecResponse? _result;

        internal void Progress(ClaudeCliProgress progress)
        {
            lock (_gate) { _progress = progress; _session = progress.SessionId ?? _session; }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                if (_state is not ("queued" or "running")) { return; }
                _state = "cancelling";
                Cancellation.Cancel();
            }
        }

        internal async Task Execute(Func<CancellationToken, Action<ClaudeCliProgress>, Task<TalvoraClaudeExecResponse>> run)
        {
            using var heartbeatCancellation = new CancellationTokenSource();
            var heartbeat = Heartbeat(heartbeatCancellation.Token);
            try
            {
                lock (_gate)
                {
                    Cancellation.Token.ThrowIfCancellationRequested();
                    _state = "running";
                }
                var result = await run(Cancellation.Token, Progress).ConfigureAwait(false);
                lock (_gate)
                {
                    _result = result;
                    _session = result.SessionId ?? _session;
                    _state = Cancellation.IsCancellationRequested ? "cancelled" :
                        result.TimedOut ? "timed_out" : result.Success ? "completed" : "failed";
                    _error = result.Success ? null : result.Error;
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _state = Cancellation.IsCancellationRequested || ex is OperationCanceledException
                        ? "cancelled" : "failed";
                    // Exception details can include user-provided args/environment. Keep them out of logs.
                    _error = _state == "cancelled" ? "Claude Code task cancelled." :
                        "Claude Code task failed. Check installation, login, working directory and CLI options.";
                }
            }
            finally
            {
                lock (_gate) { _finished = DateTime.UtcNow; }
                heartbeatCancellation.Cancel();
                try { await heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }
                Notify(() => DesktopNotifier?.EndClaudeJob(Id, Snapshot(false).State, DateTime.UtcNow - Created));
            }
        }

        private async Task Heartbeat(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var snapshot = Snapshot(false);
                Notify(() => DesktopNotifier?.UpdateClaudeJob(Id, snapshot.ToolCalls, DateTime.UtcNow - Created));
            }
        }

        internal TalvoraClaudeJobResponse Snapshot(bool includeResult)
        {
            lock (_gate)
            {
                return new(Id, _state, WorkingDirectory, Created, _finished,
                    (long)((_finished ?? DateTime.UtcNow) - Created).TotalMilliseconds,
                    _session, _progress?.ToolCalls ?? 0, _progress?.FailedTools ?? 0,
                    _progress?.PermissionDenials ?? 0, _progress?.RecentActivities ?? [],
                    _progress?.LastProgressAtUtc, _error, includeResult ? _result : null);
            }
        }
    }

    [McpServerTool(Name = "talvora_claude_start", Destructive = true, Idempotent = false,
        OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Start an optional Claude Code coding job and return immediately. Runs as the logged-on user, survives MCP disconnects within this service lifetime, supports saved session resume. Poll status or cancel. auto is the native unattended default; bypassPermissions is explicit opt-in.")]
    public static TalvoraClaudeJobResponse Start(
        string workingDirectory, string prompt, string? model = null, string? effort = null,
        string permissionMode = "auto", int timeoutSeconds = 7200, string? resumeSessionId = null,
        string[]? additionalDirectories = null, string[]? extraArguments = null,
        Dictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        // Validate synchronously so invalid jobs are never presented as accepted work.
        _ = ClaudeCliArguments.Create(prompt, model, effort, permissionMode,
            resumeSessionId, additionalDirectories, extraArguments);
        if (timeoutSeconds is < 1 or > 43200) { throw new ArgumentOutOfRangeException(nameof(timeoutSeconds)); }
        var cwd = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(cwd)) { throw new DirectoryNotFoundException(cwd); }
        var job = new Job(cwd, resumeSessionId);
        Jobs[job.Id] = job;
        Notify(() => DesktopNotifier?.BeginClaudeJob(job.Id));
        _ = Task.Run(() => job.Execute((token, progress) =>
            ClaudeCliTools.ExecuteCore(cwd, prompt, model, effort, permissionMode, timeoutSeconds,
                resumeSessionId, additionalDirectories, extraArguments, environment, token, progress)));
        Prune();
        return job.Snapshot(false);
    }

    [McpServerTool(Name = "talvora_claude_status", ReadOnly = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Get Claude Code job state, session ID, real activity counters and failure details. includeResult retrieves the final answer. Jobs are retained only during this service lifetime.")]
    public static TalvoraClaudeJobResponse Status(string jobId, bool includeResult = false) =>
        Get(jobId).Snapshot(includeResult);

    [McpServerTool(Name = "talvora_claude_list", ReadOnly = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobListResponse)),
     Description("List recent service-lifetime Claude Code jobs, newest first.")]
    public static TalvoraClaudeJobListResponse List(int maxResults = 50)
    {
        if (maxResults is < 1 or > 200) { throw new ArgumentOutOfRangeException(nameof(maxResults)); }
        var jobs = Jobs.Values.OrderByDescending(x => x.Created).Take(maxResults)
            .Select(x => x.Snapshot(false)).ToArray();
        return new(jobs.Length, jobs);
    }

    [McpServerTool(Name = "talvora_claude_cancel", Destructive = true, Idempotent = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraClaudeJobResponse)),
     Description("Cancel Claude Code work and terminate its user process tree. Poll until cancelled; already terminal jobs are unchanged.")]
    public static TalvoraClaudeJobResponse Cancel(string jobId)
    {
        var job = Get(jobId);
        job.Cancel();
        return job.Snapshot(false);
    }

    private static Job Get(string id) => Jobs.TryGetValue(id, out var job) ? job :
        throw new KeyNotFoundException("Claude Code job not found.");

    private static void Notify(Action action)
    {
        try { action(); } catch (Exception) { /* Desktop delivery must never affect work. */ }
    }

    private static void Prune()
    {
        if (Jobs.Count <= 120) { return; }
        foreach (var job in Jobs.Values.Where(x => x.Snapshot(false).FinishedAtUtc is not null)
            .OrderBy(x => x.Created).Take(Math.Max(0, Jobs.Count - 100)))
        {
            // Keep cancellation sources alive for concurrent readers holding a Job reference.
            Jobs.TryRemove(job.Id, out _);
        }
    }
}
