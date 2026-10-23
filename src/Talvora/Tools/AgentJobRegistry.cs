using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Talvora.Tools;

internal sealed record AgentJobMetadata(
    string JobId,
    string Engine,
    string WorkingDirectory,
    DateTime CreatedAtUtc,
    DateTime? FinishedAtUtc = null,
    string State = "queued",
    string? SessionId = null,
    int? RootPid = null,
    DateTimeOffset? ProcessStartTimeUtc = null,
    int CompletedCommands = 0,
    int FailedCommands = 0,
    int McpToolCalls = 0,
    int ToolCalls = 0,
    int FailedTools = 0,
    int PermissionDenials = 0,
    DateTime? LastProgressAtUtc = null,
    string? Error = null);

/// <summary>
/// One registry for Codex and Claude jobs. Only bounded operational metadata
/// is persisted: prompts, CLI arguments, and environment never enter records.
/// </summary>
internal sealed class AgentJobRegistry
{
    internal const string InterruptedError =
        "Talvora yeniden başlarken görev yarıda kaldı; sessionId ile devam edebilirsiniz.";

    internal static AgentJobRegistry Shared { get; } = new();

    private const int MaximumActiveJobs = 4;
    private const int MaximumRememberedJobs = 120;
    private const int RetainedJobsAfterPrune = 100;
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    internal sealed class Job
    {
        private readonly AgentJobRegistry _owner;
        private AgentJobMetadata _metadata;
        private readonly CancellationTokenSource? _cancellation;
        private readonly Action<AgentJobMetadata>? _onTerminal;

        internal Job(AgentJobRegistry owner, AgentJobMetadata metadata,
            Action<AgentJobMetadata>? onTerminal = null, bool recovered = false)
        {
            _owner = owner;
            _metadata = metadata;
            _onTerminal = onTerminal;
            if (!recovered)
            {
                _cancellation = new CancellationTokenSource();
                CancellationToken = _cancellation.Token;
            }
        }

        internal AgentJobMetadata Metadata
        {
            get { lock (_owner._gate) { return _metadata; } }
        }

        internal CancellationToken CancellationToken { get; }
        internal object? AdapterState { get; set; }
        internal Task? ExecutionTask { get; set; }
        internal void RequestCancellation()
        {
            try { _cancellation?.Cancel(); }
            catch (ObjectDisposedException) { /* A concurrent finish disposed this source. */ }
        }
        internal void DisposeCancellation() => _cancellation?.Dispose();
        internal void NotifyTerminal() => _onTerminal?.Invoke(Metadata);
        internal void Update(AgentJobMetadata value) => _metadata = value;
    }

    internal AgentJobRegistry(string? storageRoot = null)
    {
        _root = Path.GetFullPath(storageRoot ??
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), "Talvora", "AgentJobs"));
    }

    internal Job Create(string engine, string workingDirectory,
        bool allowParallel = false, string? sessionId = null,
        Action<AgentJobMetadata>? onTerminal = null)
    {
        if (engine is not ("codex" or "claude"))
            throw new ArgumentException("Unknown agent engine.", nameof(engine));
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
        lock (_gate)
        {
            var active = _jobs.Values.Where(job => IsActive(job.Metadata.State)).ToArray();
            if (active.Length >= MaximumActiveJobs)
                throw new InvalidOperationException(
                    "Aynı anda en fazla 4 ajan görevi çalışabilir.");
            if (!allowParallel && active.Any(job =>
                    string.Equals(job.Metadata.WorkingDirectory, directory,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Bu çalışma klasöründe zaten bir ajan görevi çalışıyor.");
            var record = new AgentJobMetadata(
                Guid.NewGuid().ToString("N"), engine, directory, DateTime.UtcNow,
                SessionId: sessionId);
            var created = new Job(this, record, onTerminal);
            Persist(record);
            _jobs.Add(record.JobId, created);
            Prune();
            return created;
        }
    }

    internal Job Get(string engine, string jobId)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.Metadata.Engine != engine)
                throw new KeyNotFoundException("Ajan görevi bulunamadı.");
            return job;
        }
    }

    internal IReadOnlyList<Job> List(string engine, int maxResults)
    {
        if (maxResults is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        lock (_gate)
        {
            return _jobs.Values.Where(job => job.Metadata.Engine == engine)
                .OrderByDescending(job => job.Metadata.CreatedAtUtc)
                .Take(maxResults).ToArray();
        }
    }

    internal void Start(Job job) => Transition(job, "running");

    internal void Progress(Job job, string? sessionId = null,
        int completedCommands = 0, int failedCommands = 0, int mcpToolCalls = 0,
        int toolCalls = 0, int failedTools = 0, int permissionDenials = 0)
    {
        lock (_gate)
        {
            var old = job.Metadata;
            if (!IsActive(old.State)) return;
            Update(job, old with
            {
                SessionId = sessionId ?? old.SessionId,
                CompletedCommands = Math.Max(old.CompletedCommands, completedCommands),
                FailedCommands = Math.Max(old.FailedCommands, failedCommands),
                McpToolCalls = Math.Max(old.McpToolCalls, mcpToolCalls),
                ToolCalls = Math.Max(old.ToolCalls, toolCalls),
                FailedTools = Math.Max(old.FailedTools, failedTools),
                PermissionDenials = Math.Max(old.PermissionDenials, permissionDenials),
                LastProgressAtUtc = DateTime.UtcNow,
            });
        }
    }

    internal void RecordProcess(Job job, int pid, DateTimeOffset startTimeUtc)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        lock (_gate)
        {
            var old = job.Metadata;
            if (!IsActive(old.State)) return;
            Update(job, old with
            {
                RootPid = pid,
                ProcessStartTimeUtc = startTimeUtc.ToUniversalTime(),
            });
        }
    }

    internal void Finish(Job job, string terminalState, string? error = null)
    {
        if (terminalState is not ("completed" or "failed" or "timed_out" or "cancelled"))
            throw new ArgumentException("Invalid terminal state.", nameof(terminalState));
        bool changed;
        lock (_gate)
        {
            var old = job.Metadata;
            changed = !IsTerminal(old.State);
            if (changed)
                Update(job, old with { State = terminalState, Error = error,
                    FinishedAtUtc = DateTime.UtcNow });
        }
        if (!changed) return;
        job.DisposeCancellation();
        job.NotifyTerminal();
    }

    internal void Cancel(Job job)
    {
        var metadata = job.Metadata;
        if (metadata.State == "orphaned")
        {
            if (FindSameProcess(metadata) is { } process)
            {
                using (process)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) { /* Already exited. */ }
                }
                Finish(job, "cancelled");
            }
            else
            {
                Finish(job, "failed", InterruptedError);
            }
            return;
        }
        if (metadata.State == "queued")
            Finish(job, "cancelled");
        else if (metadata.State == "running")
            job.RequestCancellation();
    }

    internal async Task RecoverAsync()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var path in Directory.EnumerateFiles(_root, "*.json").Take(2000))
        {
            AgentJobMetadata? record;
            try
            {
                var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                record = JsonSerializer.Deserialize<AgentJobMetadata>(text);
            }
            catch (Exception ex) when (ex is IOException or JsonException
                or UnauthorizedAccessException)
            {
                continue;
            }
            if (record is null || record.Engine is not ("codex" or "claude") ||
                !Guid.TryParseExact(record.JobId, "N", out _) ||
                !string.Equals(Path.GetFileNameWithoutExtension(path),
                    record.JobId, StringComparison.OrdinalIgnoreCase))
                continue;
            lock (_gate)
            {
                if (_jobs.ContainsKey(record.JobId)) continue;
                var previous = record;
                if (!IsTerminal(record.State))
                {
                    using var liveProcess = FindSameProcess(record);
                    record = liveProcess is not null
                        ? record with { State = "orphaned", FinishedAtUtc = DateTime.UtcNow }
                        : record with { State = "failed", FinishedAtUtc = DateTime.UtcNow,
                            Error = InterruptedError };
                }
                var job = new Job(this, record, recovered: true);
                _jobs.Add(record.JobId, job);
                if (record != previous) Persist(record);
            }
        }
        lock (_gate) { Prune(); }
    }

    private void Transition(Job job, string state)
    {
        lock (_gate)
        {
            var previous = job.Metadata;
            if (previous.State != "queued") return;
            Update(job, previous with { State = state });
        }
    }

    private void Update(Job job, AgentJobMetadata metadata)
    {
        Persist(metadata);
        job.Update(metadata);
    }

    private void Persist(AgentJobMetadata metadata)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, metadata.JobId + ".json");
        var temp = Path.Combine(_root, metadata.JobId + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(metadata), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool IsTerminal(string state) =>
        state is "completed" or "failed" or "timed_out" or "cancelled";

    private static bool IsActive(string state) =>
        state is "queued" or "running" or "orphaned";

    private static Process? FindSameProcess(AgentJobMetadata record)
    {
        if (record.RootPid is not > 0 || record.ProcessStartTimeUtc is null) return null;
        Process? process = null;
        try
        {
            process = Process.GetProcessById(record.RootPid.Value);
            if (!process.HasExited &&
                process.StartTime.ToUniversalTime().Ticks ==
                record.ProcessStartTimeUtc.Value.UtcDateTime.Ticks)
                return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or Win32Exception)
        {
            // No live process with this exact persisted identity.
        }
        process?.Dispose();
        return null;
    }

    private void Prune()
    {
        if (_jobs.Count <= MaximumRememberedJobs) return;
        foreach (var job in _jobs.Values.Where(item => IsTerminal(item.Metadata.State))
            .OrderBy(item => item.Metadata.CreatedAtUtc)
            .Take(Math.Max(0, _jobs.Count - RetainedJobsAfterPrune)).ToArray())
        {
            var record = job.Metadata;
            _jobs.Remove(record.JobId);
            job.DisposeCancellation();
            var file = Path.Combine(_root, record.JobId + ".json");
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
