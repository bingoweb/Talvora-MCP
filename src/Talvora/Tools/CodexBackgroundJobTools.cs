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

/// <summary>Codex MCP adapter for the shared persistent agent job registry.</summary>
[McpServerToolType]
public static class CodexBackgroundJobTools
{
    internal static TalvoraDesktopProgressNotifier? DesktopNotifier { get; set; }
    private static AgentJobRegistry Registry => AgentJobRegistry.Shared;

    private sealed class Runtime(AgentJobRegistry.Job job, string prompt)
    {
        private readonly object _gate = new();
        private string[] _activities = [];
        private string[] _changedFiles = [];
        private TalvoraCodexCliExecResponse? _result;
        private DateTime _lastDesktopUpdate = DateTime.MinValue;

        internal void OnProgress(CodexCliEventResult progress)
        {
            string[] changedFiles;
            int commands;
            int mcpCalls;
            string? activity;
            bool notify;
            lock (_gate)
            {
                _activities = progress.RecentActivities
                    .Select(value => CodexCliInvocationPolicy.SanitizeFinalAnswer(
                        value, prompt)).ToArray();
                _changedFiles = progress.ChangedFiles.Take(2000).ToArray();
                changedFiles = _changedFiles;
                commands = progress.CompletedCommands;
                mcpCalls = progress.McpToolCalls;
                activity = _activities.LastOrDefault();
                notify = DateTime.UtcNow - _lastDesktopUpdate > TimeSpan.FromSeconds(4);
                if (notify) _lastDesktopUpdate = DateTime.UtcNow;
            }
            Registry.Progress(job, sessionId: progress.SessionId,
                completedCommands: progress.CompletedCommands,
                failedCommands: progress.FailedCommands,
                mcpToolCalls: progress.McpToolCalls);
            if (notify)
            {
                DesktopNotifier?.UpdateCodexJob(job.Metadata.JobId, commands, mcpCalls,
                    activity, DateTime.UtcNow - job.Metadata.CreatedAtUtc, changedFiles);
            }
        }

        internal void Complete(TalvoraCodexCliExecResponse result)
        {
            lock (_gate)
            {
                _result = result;
                if (result.RecentActivities is { Count: > 0 } && !result.StandardOutputTruncated)
                    _activities = result.RecentActivities.Select(value =>
                        CodexCliInvocationPolicy.SanitizeFinalAnswer(value, prompt)).ToArray();
            }
            Registry.Progress(job, sessionId: result.SessionId,
                completedCommands: result.CompletedCommands,
                failedCommands: result.FailedCommands,
                mcpToolCalls: result.McpToolCalls);
            var state = job.CancellationToken.IsCancellationRequested ? "cancelled" :
                result.TimedOut ? "timed_out" : result.ExitCode == 0 ? "completed" : "failed";
            var error = state == "completed" ? null :
                CodexCliInvocationPolicy.SanitizeFinalAnswer(
                    result.ErrorDetail ?? result.StandardError, prompt);
            Registry.Finish(job, state, error);
        }

        internal void Fail(Exception error)
        {
            var cancelled = error is OperationCanceledException ||
                job.CancellationToken.IsCancellationRequested;
            Registry.Finish(job, cancelled ? "cancelled" : "failed", cancelled
                ? "Codex görevi iptal edildi."
                : CodexCliInvocationPolicy.SanitizeFinalAnswer(error.Message, prompt));
        }

        internal void OnTerminal(AgentJobMetadata metadata)
        {
            try
            {
                TalvoraCodexCliExecResponse? result;
                string[] changedFiles;
                lock (_gate)
                {
                    result = _result;
                    changedFiles = _changedFiles;
                }
                DesktopNotifier?.EndCodexJob(metadata.JobId, metadata.State,
                    (metadata.FinishedAtUtc ?? DateTime.UtcNow) - metadata.CreatedAtUtc,
                    metadata.CompletedCommands, metadata.McpToolCalls, changedFiles,
                    result?.StandardOutput);
            }
            catch (Exception)
            {
                // A desktop notification must not change the job's terminal state.
            }
        }

        internal TalvoraCodexJobResponse Snapshot(bool includeResult)
        {
            lock (_gate)
            {
                return FromMetadata(job.Metadata, includeResult ? _result : null, _activities);
            }
        }
    }

    private static TalvoraCodexJobResponse FromMetadata(AgentJobMetadata metadata,
        TalvoraCodexCliExecResponse? result, IReadOnlyList<string> activities) =>
        new(metadata.JobId, metadata.State, metadata.WorkingDirectory,
            metadata.CreatedAtUtc, metadata.FinishedAtUtc,
            (long)((metadata.FinishedAtUtc ?? DateTime.UtcNow) -
                metadata.CreatedAtUtc).TotalMilliseconds,
            metadata.SessionId, metadata.CompletedCommands, metadata.FailedCommands,
            metadata.McpToolCalls, activities, metadata.Error, result,
            metadata.LastProgressAtUtc);

    private static TalvoraCodexJobResponse Snapshot(
        AgentJobRegistry.Job job, bool includeResult) =>
        job.AdapterState is Runtime live
            ? live.Snapshot(includeResult)
            : FromMetadata(job.Metadata, null, []);

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
        string windowsSandboxMode = "default",
        bool allowParallel = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        Runtime? runtime = null;
        var entry = Registry.Create("codex", workingDirectory, allowParallel,
            sessionId: resumeSessionId, onTerminal: metadata => runtime?.OnTerminal(metadata));
        runtime = new Runtime(entry, prompt);
        entry.AdapterState = runtime;
        try
        {
            DesktopNotifier?.BeginCodexJob(entry.Metadata.JobId, entry.Metadata.WorkingDirectory);
        }
        catch (Exception)
        {
            // Desktop delivery cannot strand a registered agent job.
        }
        entry.ExecutionTask = Task.Run(async () =>
        {
            Registry.Start(entry);
            if (entry.Metadata.State != "running") return;
            try
            {
                var result = await CodexCliTools.ExecuteCore(
                    entry.Metadata.WorkingDirectory, prompt, model, reasoningEffort,
                    sandbox, timeoutSeconds, resumeSessionId, preserveSession,
                    networkAccess, approvalMode, additionalWritableDirectories,
                    allowNonGitWorkspace, allowFullAccess, windowsSandboxMode,
                    entry.CancellationToken, runtime.OnProgress,
                    (pid, started) => Registry.RecordProcess(entry, pid, started))
                    .ConfigureAwait(false);
                runtime.Complete(result);
            }
            catch (Exception ex) { runtime.Fail(ex); }
        });
        return Snapshot(entry, false);
    }

    [McpServerTool(
        Name = "talvora_codex_status",
        ReadOnly = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobResponse)),
     Description("Read Codex job status, persisted session ID, elapsed time and counters. Set includeResult=true to retrieve the completed result if available.")]
    public static TalvoraCodexJobResponse Status(
        string jobId, bool includeResult = false) =>
        Snapshot(Registry.Get("codex", jobId), includeResult);

    [McpServerTool(
        Name = "talvora_codex_list",
        ReadOnly = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobListResponse)),
     Description("List recent Codex background jobs, including orphaned jobs after restart.")]
    public static TalvoraCodexJobListResponse List(int maxResults = 50)
    {
        var jobs = Registry.List("codex", maxResults)
            .Select(job => Snapshot(job, false)).ToArray();
        return new TalvoraCodexJobListResponse(jobs.Length, jobs);
    }

    [McpServerTool(
        Name = "talvora_codex_cancel",
        Destructive = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexJobResponse)),
     Description("Cancel a Codex background job, including an orphan with a verified PID and process start time.")]
    public static TalvoraCodexJobResponse Cancel(string jobId)
    {
        var job = Registry.Get("codex", jobId);
        Registry.Cancel(job);
        return Snapshot(job, false);
    }
}
