using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Shared;

namespace Talvora;

/// <summary>Reads only a started job's bounded log and observed exit status (§6.4 WL6).</summary>
internal static class WorklogFollower
{
    internal const int MaximumLogBytes = 64 * 1024;
    internal static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ProcessLimit = TimeSpan.FromHours(2);
    internal static readonly TimeSpan CiInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CiLimit = TimeSpan.FromMinutes(30);
    internal const string PendingDeployPath = @"C:\ProgramData\Talvora\Worklog\pending-deploy.json";

    internal enum JobKind { Other, QualityGate, Smoke, MetadataSmoke, InstallerBuild }

    internal sealed record Outcome(
        bool Succeeded, string Title, string? Fact, string? Reason,
        bool TimedOut = false, string? TechnicalResult = null);
    internal sealed record PendingDeploy(
        string SessionId, string OperationId, string SourceCommit,
        DateTimeOffset StartedAtUtc, string Actor, string? ProjectName)
    {
        public string? SessionKey { get; init; }
        public DateTimeOffset BeganUtc { get; init; }
        public int Steps { get; init; }
        public WorklogTimelineEntry[] Timeline { get; init; } = [];
        public string? TaskNote { get; init; }
        public string[] ChangedFiles { get; init; } = [];
        public WorklogCheckState LastBuild { get; init; } = WorklogCheckState.Unknown;
        public WorklogCheckState LastTests { get; init; } = WorklogCheckState.Unknown;
    }

    private static readonly Dictionary<string, string> Stages = new(StringComparer.Ordinal)
    {
        ["Restore"] = "Paketlerin geri yüklenmesi",
        ["Build"] = "Derleme",
        ["PowerShellParse"] = "PowerShell sözdizimi",
        ["PackageManager"] = "Paket yöneticisi kuralları",
        ["Context7"] = "Context7 kanıtı",
        ["Delivery"] = "Teslim kapısı",
        ["SourceEditSemantic"] = "Kod düzenleme motoru (anlamsal)",
        ["SourceEdit"] = "Kod düzenleme motoru",
        ["Notification"] = "Masaüstü bildirim testleri",
        ["ParallelPhase"] = "Paralel testler",
        ["PowerShellRegressions"] = "PowerShell test betikleri",
        ["IntegrationPolicy"] = "Entegrasyon kuralları",
        ["NativeInstallerRuntime"] = "Yükleyici çalışma testi",
        ["HealthHandle"] = "Sağlık ve tanıtıcı denetimi",
        ["ControlCenter"] = "Kontrol Merkezi testi",
        ["BusinessBootstrap"] = "Başlangıç kurulumu testi",
    };

    internal static string? StageLabel(string name) =>
        Stages.TryGetValue(name, out var label) ? label
        : name.StartsWith("PS:", StringComparison.Ordinal) &&
          Regex.IsMatch(name[3..], @"^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)
            ? "PowerShell test betiği · " + name[3..] + ".ps1"
            : null;

    internal static JobKind Classify(IReadOnlyList<string> args)
    {
        var command = string.Join(' ', args);
        if (command.Contains("Invoke-QualityGate.ps1", StringComparison.OrdinalIgnoreCase))
            return JobKind.QualityGate;
        if (command.Contains("Build-Windows-Installer.ps1", StringComparison.OrdinalIgnoreCase))
            return JobKind.InstallerBuild;
        if (command.Contains("Talvora.Smoke", StringComparison.OrdinalIgnoreCase))
            return command.Contains("--metadata-policy-only", StringComparison.OrdinalIgnoreCase)
                ? JobKind.MetadataSmoke : JobKind.Smoke;
        return JobKind.Other;
    }

    /// <summary>Untrusted redirect paths are allowed only within the requested workspace.</summary>
    internal static string? ResolveLogPath(IReadOnlyList<string> args, string workspace)
    {
        var match = Regex.Match(string.Join(' ', args),
            @"(?:\*>\s*|(?<=\s)>\s*)(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
            RegexOptions.CultureInvariant);
        if (!match.Success || !Directory.Exists(workspace)) return null;
        var candidate = match.Groups["path"].Value.Trim('"', '\'');
        try
        {
            var root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.IsPathFullyQualified(candidate)
                ? candidate : Path.Combine(root, candidate));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return null;
            // A reparse point could silently redirect an otherwise internal path outside the workspace.
            var current = root;
            foreach (var part in Path.GetRelativePath(root, path)
                         .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }
            return path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                                   IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static async Task<string> ReadTailAsync(string path, CancellationToken token)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
            var toRead = (int)Math.Min(file.Length, MaximumLogBytes);
            file.Seek(-toRead, SeekOrigin.End);
            var buffer = new byte[toRead];
            var bytes = 0;
            while (bytes < toRead)
            {
                var count = await file.ReadAsync(buffer.AsMemory(bytes, toRead - bytes), token);
                if (count == 0) break;
                bytes += count;
            }
            return Encoding.UTF8.GetString(buffer, 0, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   FileNotFoundException or DirectoryNotFoundException)
        {
            return "";
        }
    }

    private static string Duration(DateTimeOffset started) =>
        WorklogSubject.FormatDuration(DateTimeOffset.UtcNow - started);

    internal static string LiveFact(JobKind kind, string log, DateTimeOffset started)
    {
        var elapsed = Duration(started);
        if (kind != JobKind.QualityGate) return elapsed;
        var stages = Regex.Matches(log,
            @"(?m)^START\s+(?<name>[A-Za-z]+)(?:\s+(?<count>\d+))?\s*$",
            RegexOptions.CultureInvariant);
        if (stages.Count == 0) return elapsed;
        var open = new List<Match>();
        foreach (Match entry in Regex.Matches(log,
            @"(?m)^(?<kind>START|PASS|FAIL)\s+(?<name>[A-Za-z]+)(?:\s+(?<count>\d+))?",
            RegexOptions.CultureInvariant))
        {
            if (entry.Groups["kind"].Value == "START")
            {
                open.Add(entry);
            }
            else
            {
                var index = open.FindLastIndex(item => string.Equals(
                    item.Groups["name"].Value, entry.Groups["name"].Value,
                    StringComparison.Ordinal));
                if (index >= 0) open.RemoveAt(index);
            }
        }
        var latest = open.Count == 0 ? stages[^1] : open[^1];
        var stage = StageLabel(latest.Groups["name"].Value);
        if (stage is null) return elapsed;
        var passed = Regex.Matches(log, @"(?m)^PASS\s+", RegexOptions.CultureInvariant).Count;
        var errors = Regex.Matches(log, @"(?m)^FAIL\s+", RegexOptions.CultureInvariant).Count;
        if (latest.Groups["name"].Value == "ParallelPhase" &&
            latest.Groups["count"].Success)
            stage = "Paralel testler (" + latest.Groups["count"].Value + " kontrol birlikte)";
        return "şu an: " + stage + " · " + passed + " aşama geçti · " +
               errors + " hata · " + elapsed;
    }

    private static Outcome FinishProcess(
        JobKind kind, string log, int? exitCode, DateTimeOffset started)
    {
        var duration = Duration(started);
        var validExit = exitCode == 0;
        if (kind == JobKind.QualityGate)
        {
            var summary = Regex.Match(log,
                @"(?m)^(?<checks>\d+) kontrol, (?<errors>\d+) hata\s*$",
                RegexOptions.CultureInvariant);
            if (summary.Success)
            {
                var fact = summary.Groups["checks"].Value + " kontrol · " +
                    summary.Groups["errors"].Value + " hata · " + duration;
                var failed = summary.Groups["errors"].Value != "0" ||
                    !log.Contains("Hepsi geçti.", StringComparison.Ordinal);
                var reason = Regex.Match(log, @"(?m)^Kontrol başarısız:\s*(?<name>[^\r\n]+)");
                var label = reason.Success ? StageLabel(reason.Groups["name"].Value) : null;
                return new Outcome(validExit && !failed,
                    validExit && !failed ? "Kalite kontrolü geçti" : "Kalite kontrolü başarısız",
                    fact, label is not null ? "Kontrol başarısız: " + label : null);
            }
        }
        if (kind is JobKind.Smoke or JobKind.MetadataSmoke)
        {
            var smoke = kind == JobKind.Smoke;
            var succeeded = validExit && log.Contains(
                smoke ? "TALVORA MCP SMOKE GREEN" : "METADATA POLICY LIVE GREEN",
                StringComparison.Ordinal);
            return new Outcome(succeeded, smoke
                    ? succeeded ? "Kurulu sürüm testi geçti" : "Kurulu sürüm testi başarısız"
                    : succeeded ? "Araç tanımları denetimi geçti" : "Araç tanımları denetimi başarısız",
                duration, succeeded ? null : smoke
                    ? "Kurulu sürüm testi bir adımda durdu; ayrıntı günlükte."
                    : "Araç tanımları denetimi bir adımda durdu; ayrıntı günlükte.",
                TechnicalResult: succeeded ? null : SmokeFailureLine(log));
        }
        if (kind == JobKind.InstallerBuild && validExit)
        {
            var json = Regex.Match(log,
                @"""SizeBytes""\s*:\s*(?<bytes>\d+)[^\r\n]*?""Sha256""\s*:\s*""(?<sha>[0-9a-fA-F]{64})""");
            if (json.Success &&
                long.TryParse(json.Groups["bytes"].Value, CultureInfo.InvariantCulture, out var bytes))
                return new Outcome(true, "Kurulum paketi hazırlandı",
                    (bytes / 1_000_000d).ToString("0.0", CultureInfo.GetCultureInfo("tr-TR")) +
                    " MB · SHA-256 " + json.Groups["sha"].Value[..8], null);
        }
        return new Outcome(validExit,
            validExit ? "İşlem tamamlandı" : "İşlem hata ile bitti",
            duration,
            exitCode is null ? "İşlemin çıkış kodu doğrulanamadı." : null,
            TechnicalResult: !validExit && exitCode is { } code
                ? "Sonuç: tamamlanamadı  //  kod " + code : null);
    }

    private static string? SmokeFailureLine(string log)
    {
        const string marker = "TALVORA MCP SMOKE RED";
        var index = log.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return null;
        var line = log[(index + marker.Length)..]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).FirstOrDefault(value => value.Length > 0);
        if (line is null) return null;
        var safe = FileLog.RedactSensitiveData(line);
        return safe.Length > 160 ? safe[..160] : safe;
    }

    internal static async Task<Outcome> FollowProcessAsync(
        Func<CancellationToken, Task<int?>> waitForExit,
        string? logPath, JobKind kind, Action<string> onProgress,
        DateTimeOffset started, TimeSpan interval, TimeSpan limit, CancellationToken token)
    {
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(token);
        var process = waitForExit(watch.Token);
        try
        {
            var observed = "";
            while (!process.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - started >= limit)
                    return new Outcome(false, "İzleme süresi doldu", Duration(started), null, true);
                if (logPath is not null) observed = await ReadTailAsync(logPath, token);
                onProgress(LiveFact(kind, observed, started));
                await Task.WhenAny(process, Task.Delay(interval, token));
            }
            // The last completed line may have been written during process shutdown.
            if (logPath is not null) observed = await ReadTailAsync(logPath, token);
            return FinishProcess(kind, observed, await process, started);
        }
        finally
        {
            watch.Cancel(); // Never leave a process-exit waiter after the two-hour limit.
        }
    }

    internal static async Task<Outcome> FollowCiAsync(
        Func<CancellationToken, Task<(int ExitCode, string Output)>> check,
        DateTimeOffset started, TimeSpan interval, TimeSpan limit, CancellationToken token)
    {
        var unavailable = 0;
        while (DateTimeOffset.UtcNow - started < limit)
        {
            await Task.Delay(interval, token);
            var (exit, output) = await check(token);
            if (exit == 0)
            {
                var match = Regex.Match(output, @"(?m)^CI başarılı \((?<duration>[^)\r\n]+)\):");
                return new Outcome(true, "GitHub denetimi geçti",
                    match.Success ? match.Groups["duration"].Value : null, null);
            }
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).ToArray();
            if (exit == 1 && lines.Length > 0 &&
                lines[0].StartsWith("CI başarısız", StringComparison.Ordinal))
            {
                var match = Regex.Match(lines[0],
                    @"^CI başarısız \([^,]+,\s*(?<duration>[^)]+)\):");
                var detail = lines.Skip(1).FirstOrDefault(line => line.Length > 0);
                var safe = detail is null ? null : FileLog.RedactSensitiveData(detail);
                return new Outcome(false, "GitHub denetimi başarısız",
                    match.Success ? match.Groups["duration"].Value : null,
                    "GitHub denetimi başarısız oldu; ayrıntılar CI günlüğünde.",
                    TechnicalResult: safe is { Length: > 160 } ? safe[..160] : safe);
            }
            unavailable = exit == 2 ? 0 : unavailable + 1;
            if (unavailable >= 3)
                return new Outcome(false, "GitHub denetiminin sonucu alınamadı", null,
                    "GitHub'a ulaşılamadı; sonuç bir sonraki denetimde okunacak.", true);
        }
        return new Outcome(false, "GitHub denetimi henüz bitmemişti", Duration(started), null, true);
    }

    internal static async Task WritePendingDeployAsync(
        string path, PendingDeploy value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _ = await AtomicFile.WriteAllTextAsync(path,
            JsonSerializer.Serialize(value), new UTF8Encoding(false),
            cancellationToken: token);
    }

    internal static async Task<PendingDeploy?> TakePendingDeployAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 256 * 1024) return null;
            var json = await File.ReadAllTextAsync(path, token);
            var pending = JsonSerializer.Deserialize<PendingDeploy>(json);
            return pending is not null &&
                   DateTimeOffset.UtcNow - pending.StartedAtUtc <= TimeSpan.FromMinutes(30) &&
                   pending.StartedAtUtc <= DateTimeOffset.UtcNow &&
                   pending.SessionId.Length is > 0 and <= 160 &&
                   pending.OperationId.Length is > 0 and <= 160 &&
                   pending.SourceCommit.Length is >= 7 and <= 64 ? pending : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static Outcome CompleteDeploy(PendingDeploy pending, string? currentCommit)
    {
        var elapsed = WorklogSubject.FormatDuration(DateTimeOffset.UtcNow - pending.StartedAtUtc);
        if (!string.IsNullOrWhiteSpace(currentCommit) &&
            string.Equals(currentCommit, pending.SourceCommit, StringComparison.OrdinalIgnoreCase))
            return new Outcome(true, "Yeni sürüm çalışıyor · " + currentCommit[..7],
                elapsed, null);
        return new Outcome(false, "Yeni sürüm kurulamadı", elapsed,
            "Yeni sürüm kurulamadı; önceki sürüm çalışıyor.");
    }
}

/// <summary>WL6 follows only jobs launched by Talvora; no arbitrary command execution.</summary>
internal sealed partial class TalvoraDesktopProgressNotifier
{
    private readonly CancellationTokenSource _followStopCts = new();
    internal string PendingDeployWritePath { get; set; } = WorklogFollower.PendingDeployPath;

    private static JsonElement ResultJson<T>(T result) =>
        result is ModelContextProtocol.Protocol.CallToolResult
            { StructuredContent: JsonElement structured }
            ? structured : JsonSerializer.SerializeToElement(result);

    private static async Task<int?> WaitForObservedExitAsync(
        int processId, DateTimeOffset started, CancellationToken token)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime() < started.UtcDateTime.AddMinutes(-1))
                return null; // The PID may have been reused.
            await process.WaitForExitAsync(token);
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   System.ComponentModel.Win32Exception)
        {
            return null; // A missing process is not evidence of success.
        }
    }

    private bool TryStartFollowedBackground<T>(
        string operationId, string tool, IDictionary<string, JsonElement>? args,
        T result, DateTimeOffset started)
    {
        if (tool is not ("user_process_start" or "job_start")) return false;
        var pid = FactNumber(ResultJson(result), "processId");
        if (pid is null or <= 0) return false;
        var arguments = GetArgumentArray(args, "arguments");
        var kind = WorklogFollower.Classify(arguments);
        var workspace = GetArgumentText(args, "workingDirectory");
        var log = WorklogFollower.ResolveLogPath(arguments, workspace);
        MarkFollowedWorklog(operationId);
        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await WorklogFollower.FollowProcessAsync(
                    ct => WaitForObservedExitAsync(pid.Value, started, ct),
                    log, kind,
                    fact => TryUpdateFollowedWorklog(operationId, fact,
                        DateTimeOffset.UtcNow - started),
                    started, WorklogFollower.ProcessInterval,
                    WorklogFollower.ProcessLimit, _followStopCts.Token);
                var scriptTitles = WorklogSubject.ForExecutable(
                    GetArgumentText(args, "executable"), arguments).Titles;
                var title = kind switch
                {
                    WorklogFollower.JobKind.QualityGate => outcome.Succeeded
                        ? WorklogSubject.ForScript("Invoke-QualityGate.ps1",
                            string.Join(' ', arguments)).Titles.Succeeded
                        : WorklogSubject.ForScript("Invoke-QualityGate.ps1",
                            string.Join(' ', arguments)).Titles.Failed,
                    WorklogFollower.JobKind.InstallerBuild => outcome.Succeeded
                        ? WorklogCopy.PackageBuild.Succeeded : WorklogCopy.PackageBuild.Failed,
                    WorklogFollower.JobKind.Other => outcome.Succeeded
                        ? scriptTitles.Succeeded : scriptTitles.Failed,
                    _ => outcome.Title,
                };
                TryEndWorklog(operationId,
                    outcome.TimedOut ? DesktopProgressKind.Cancelled
                        : outcome.Succeeded ? DesktopProgressKind.Completed : DesktopProgressKind.Failed,
                    outcome.TimedOut ? "İzleme süresi doldu" : title,
                    outcome.Reason ?? (outcome.Succeeded
                        ? WorklogCopy.SucceededFallback : "İşlem hata ile bitti."),
                    new DesktopProgressEvidence(Summary: outcome.Fact,
                        Result: outcome.TechnicalResult is { } detail
                            ? detail.StartsWith("Sonuç: ", StringComparison.Ordinal)
                                ? detail : "Sonuç: " + detail
                            : outcome.Fact is null ? null : "Sonuç: " + outcome.Fact),
                    DateTimeOffset.UtcNow - started);
            }
            catch (OperationCanceledException) when (_followStopCts.IsCancellationRequested)
            {
                // Service stopped; the next service instance owns any persisted deploy.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Followed background process could not complete its worklog.");
                TryEndWorklog(operationId, DesktopProgressKind.Failed,
                    "İşlem sonucu doğrulanamadı", "Arka plan işleminin durumu okunamadı.",
                    null, DateTimeOffset.UtcNow - started);
            }
        });
        return true;
    }

    private bool TryStartFollowedCi(
        string operationId, OperationNarrative narrative,
        IDictionary<string, JsonElement>? args, DateTimeOffset started)
    {
        if (!string.Equals(narrative.Subject, "Get-CiStatus.ps1",
                StringComparison.OrdinalIgnoreCase)) return false;
        var workspace = GetArgumentText(args, "workingDirectory");
        var executable = GetArgumentText(args, "executable");
        if (!Directory.Exists(workspace) || !File.Exists(executable) ||
            !File.Exists(Path.Combine(workspace, "scripts", "Get-CiStatus.ps1")))
            return false;
        MarkFollowedWorklog(operationId);
        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await WorklogFollower.FollowCiAsync(async ct =>
                {
                    var checkedRun = await InteractiveUserProcessRunner.RunAsync(
                        executable, workspace,
                        ["-NoProfile", "-File", "scripts/Get-CiStatus.ps1",
                            "-TimeoutMinutes", "0"],
                        null, 90, ct);
                    return (checkedRun.ExitCode,
                        checkedRun.StandardOutput + "\n" + checkedRun.StandardError);
                }, started, WorklogFollower.CiInterval, WorklogFollower.CiLimit,
                _followStopCts.Token);
                TryEndWorklog(operationId,
                    outcome.TimedOut ? DesktopProgressKind.Cancelled
                        : outcome.Succeeded ? DesktopProgressKind.Completed : DesktopProgressKind.Failed,
                    outcome.Title,
                    outcome.Reason ?? WorklogCopy.SucceededFallback,
                    new DesktopProgressEvidence(Summary: outcome.Fact,
                        Result: outcome.TechnicalResult is { } detail
                            ? "Sonuç: " + detail
                            : outcome.Fact is null ? null : "Sonuç: " + outcome.Fact),
                    DateTimeOffset.UtcNow - started);
            }
            catch (OperationCanceledException) when (_followStopCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Followed CI check failed to report the final status.");
                TryEndWorklog(operationId, DesktopProgressKind.Failed,
                    "GitHub denetimi başarısız", "GitHub denetiminin sonucu alınamadı.",
                    null, DateTimeOffset.UtcNow - started);
            }
        });
        TryUpdateFollowedWorklog(operationId,
            "GitHub denetimi sürüyor · " + WorklogSubject.FormatDuration(
                DateTimeOffset.UtcNow - started), DateTimeOffset.UtcNow - started);
        return true;
    }

    private async Task<bool> TryPersistFollowedDeployAsync<T>(
        string operationId, OperationNarrative narrative, T result, DateTimeOffset started)
    {
        if (narrative.Category != OperationCategory.Deployment) return false;
        var output = FactText(ResultJson(result), "standardOutput");
        if (string.IsNullOrWhiteSpace(output)) return false;
        var line = Regex.Match(output,
            @"(?m)^\s*(?<json>\{[^\r\n]*""launched""\s*:\s*true[^\r\n]*\})\s*$");
        if (!line.Success) return false;
        string? target;
        try
        {
            using var json = JsonDocument.Parse(line.Groups["json"].Value);
            target = json.RootElement.TryGetProperty("sourceCommit", out var commit)
                ? commit.GetString() : null;
        }
        catch (JsonException) { return false; }
        if (target is null || target.Length < 7) return false;
        WorklogFollower.PendingDeploy? pending = null;
        lock (_worklogGate)
        {
            if (_activeWorklog.TryGetValue(operationId, out var active))
                pending = new WorklogFollower.PendingDeploy(active.Session.Id, operationId,
                    target, started, active.Session.Actor, active.Session.ProjectName)
                {
                    SessionKey = active.Session.Key,
                    BeganUtc = active.Session.BeganUtc,
                    Steps = active.Session.Steps,
                    Timeline = active.Session.Timeline.Take(DesktopProgressProtocol.MaximumTimelineEntries).ToArray(),
                    TaskNote = active.Session.TaskNote,
                    ChangedFiles = active.Session.ChangedFiles.Take(500).ToArray(),
                    LastBuild = active.Session.LastBuild,
                    LastTests = active.Session.LastTests,
                };
        }
        if (pending is null) return false;
        try
        {
            await WorklogFollower.WritePendingDeployAsync(
                PendingDeployWritePath, pending, _followStopCts.Token);
            MarkFollowedWorklog(operationId);
            TryUpdateFollowedWorklog(operationId,
                WorklogSubject.FormatDuration(DateTimeOffset.UtcNow - started),
                DateTimeOffset.UtcNow - started);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Pending deploy worklog could not be persisted.");
            return false;
        }
    }

    /// <summary>Called once by the new service; restores the previous operation's session ID.</summary>
    internal async Task ReplayPendingDeploymentAsync(
        string path, string? runningCommit, CancellationToken token = default)
    {
        var pending = await WorklogFollower.TakePendingDeployAsync(path, token);
        if (pending is null) return;
        var outcome = WorklogFollower.CompleteDeploy(pending, runningCommit);
        string? transientKey = null;
        WorklogSession session;
        lock (_worklogGate)
        {
            if (!string.IsNullOrWhiteSpace(pending.SessionKey))
            {
                if (!_sessions.TryGetValue(pending.SessionKey, out session!))
                {
                    if (!_sessionsById.TryGetValue(pending.SessionId, out session!))
                    {
                        session = new WorklogSession(pending.SessionId, pending.Actor,
                            pending.ProjectName)
                        {
                            Key = pending.SessionKey,
                            BeganUtc = pending.BeganUtc,
                            Steps = pending.Steps,
                            LastBuild = pending.LastBuild,
                            LastTests = pending.LastTests,
                            TaskNote = pending.TaskNote,
                        };
                        session.Timeline.AddRange((pending.Timeline ?? []).Take(
                            DesktopProgressProtocol.MaximumTimelineEntries));
                        session.ChangedFiles.UnionWith((pending.ChangedFiles ?? []).Take(500));
                        _sessionsById[session.Id] = session;
                    }
                    _sessions[pending.SessionKey] = session;
                }
            }
            else if (!_sessionsById.TryGetValue(pending.SessionId, out session!))
            {
                transientKey = "deploy:" + pending.SessionId;
                session = new WorklogSession(pending.SessionId, pending.Actor,
                    pending.ProjectName) { BeganUtc = pending.StartedAtUtc };
                _sessions[transientKey] = session;
                _sessionsById[session.Id] = session;
            }
            session.Category = OperationCategory.Deployment;
            _activeWorklog[pending.OperationId] = new ActiveWorklogItem(
                "run_powershell",
                new OperationNarrative(OperationCategory.Deployment, WorklogCopy.Deploy),
                null, ++_worklogSequence, session);
        }
        var elapsed = DateTimeOffset.UtcNow - pending.StartedAtUtc;
        TryEndWorklog(pending.OperationId,
            outcome.Succeeded ? DesktopProgressKind.Completed : DesktopProgressKind.Failed,
            outcome.Title, outcome.Reason ?? WorklogCopy.SucceededFallback,
            new DesktopProgressEvidence(Summary: outcome.Fact,
                Result: outcome.Fact is null ? null : "Sonuç: " + outcome.Fact),
            elapsed);
        if (transientKey is not null)
        {
            lock (_worklogGate)
            {
                if (_sessions.TryGetValue(transientKey, out var stored) &&
                    ReferenceEquals(stored, session))
                    _sessions.Remove(transientKey);
                if (_sessionsById.TryGetValue(session.Id, out var indexed) &&
                    ReferenceEquals(indexed, session))
                    _sessionsById.Remove(session.Id);
                session.CloseDelay?.Cancel();
                session.CloseDelay = null;
            }
        }
    }
}
