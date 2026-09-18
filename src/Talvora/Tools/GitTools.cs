using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraGitRunResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string GitExecutable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public sealed record TalvoraGitInfoResponse(
    string RequestedPath,
    string Root,
    string GitDirectory,
    bool Bare,
    string Head,
    string? Branch,
    bool Detached,
    bool Dirty,
    IReadOnlyList<string> Remotes);

public sealed record TalvoraGitStatusResponse(
    string Root,
    string? Branch,
    string? Head,
    string? Upstream,
    int Ahead,
    int Behind,
    bool Dirty,
    IReadOnlyList<string> Entries,
    string Raw);

public sealed record TalvoraGitDiffResponse(
    string Root,
    bool Staged,
    string? RevisionRange,
    int ContextLines,
    IReadOnlyList<string> Paths,
    string Diff);

public sealed record TalvoraGitCommitEntry(
    string Commit,
    string ShortCommit,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject);

public sealed record TalvoraGitLogResponse(
    string Root,
    int Count,
    IReadOnlyList<TalvoraGitCommitEntry> Commits);

public sealed record TalvoraGitBranchEntry(
    string Ref,
    string Commit,
    bool Current,
    string? Upstream,
    string? UpstreamTrack,
    DateTimeOffset? CommitterDate,
    string Subject);

public sealed record TalvoraGitBranchesResponse(
    string Root,
    int Count,
    IReadOnlyList<TalvoraGitBranchEntry> Branches);

[McpServerToolType]
public static class GitTools
{
    private const char FieldSeparator = '\u001f';

    [McpServerTool(
        Name = "talvora_git_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitInfoResponse)),
     Description("Return structured Git repository identity for any accessible path: repository root, git directory, HEAD, branch/detached state, dirty state, and remotes.")]
    public static async Task<TalvoraGitInfoResponse> Info(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var path = NormalizeRepositoryPath(repositoryPath);
        var root = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        var gitDirectory = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        var bareText = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--is-bare-repository"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        var head = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "HEAD"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        var branchResult = await RunGitAsync(
            path,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var branch = branchResult.ExitCode == 0
            ? branchResult.StandardOutput.Trim()
            : null;

        var status = await RunGitCheckedAsync(
            path,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            cancellationToken: cancellationToken);

        var remotes = await RunGitCheckedAsync(
            path,
            ["remote", "-v"],
            cancellationToken: cancellationToken);

        return new TalvoraGitInfoResponse(
            Path.GetFullPath(repositoryPath),
            root,
            gitDirectory,
            string.Equals(bareText, "true", StringComparison.OrdinalIgnoreCase),
            head,
            branch,
            branch is null,
            !string.IsNullOrEmpty(status.StandardOutput),
            SplitLines(remotes.StandardOutput));
    }

    [McpServerTool(
        Name = "talvora_git_status",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitStatusResponse)),
     Description("Return Git porcelain-v2 status with parsed branch/HEAD/upstream/ahead/behind metadata and raw machine-readable entry lines.")]
    public static async Task<TalvoraGitStatusResponse> Status(
        string repositoryPath,
        bool includeUntracked = true,
        CancellationToken cancellationToken = default)
    {
        var path = NormalizeRepositoryPath(repositoryPath);
        var args = new List<string>
        {
            "status",
            "--porcelain=v2",
            "--branch",
            includeUntracked ? "--untracked-files=all" : "--untracked-files=no",
        };

        var result = await RunGitCheckedAsync(path, args, cancellationToken: cancellationToken);
        var lines = SplitLines(result.StandardOutput);

        string? branch = null;
        string? head = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var entries = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = line["# branch.head ".Length..];
                if (string.Equals(branch, "(detached)", StringComparison.Ordinal))
                {
                    branch = null;
                }
            }
            else if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                head = line["# branch.oid ".Length..];
                if (string.Equals(head, "(initial)", StringComparison.Ordinal))
                {
                    head = null;
                }
            }
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = line["# branch.upstream ".Length..];
            }
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var token in line["# branch.ab ".Length..]
                             .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith('+') &&
                        int.TryParse(token.AsSpan(1), out var parsedAhead))
                    {
                        ahead = parsedAhead;
                    }
                    else if (token.StartsWith('-') &&
                             int.TryParse(token.AsSpan(1), out var parsedBehind))
                    {
                        behind = parsedBehind;
                    }
                }
            }
            else if (!line.StartsWith("# ", StringComparison.Ordinal))
            {
                entries.Add(line);
            }
        }

        var root = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        return new TalvoraGitStatusResponse(
            root,
            branch,
            head,
            upstream,
            ahead,
            behind,
            entries.Count > 0,
            entries,
            result.StandardOutput);
    }

    [McpServerTool(
        Name = "talvora_git_diff",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitDiffResponse)),
     Description("Return a Git unified diff for any accessible repository. Supports staged diffs, arbitrary revision ranges, context lines, and path filters. No repository or ref allowlist is applied.")]
    public static async Task<TalvoraGitDiffResponse> Diff(
        string repositoryPath,
        bool staged = false,
        string? revisionRange = null,
        string[]? paths = null,
        int contextLines = 3,
        CancellationToken cancellationToken = default)
    {
        if (contextLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextLines));
        }

        var path = NormalizeRepositoryPath(repositoryPath);
        var args = new List<string>
        {
            "diff",
            "--no-ext-diff",
            $"--unified={contextLines}",
        };

        if (staged)
        {
            args.Add("--cached");
        }

        if (!string.IsNullOrWhiteSpace(revisionRange))
        {
            args.Add(revisionRange);
        }

        var pathList = paths ?? [];
        if (pathList.Length > 0)
        {
            args.Add("--");
            args.AddRange(pathList);
        }

        var result = await RunGitCheckedAsync(
            path,
            args,
            timeoutSeconds: 120,
            cancellationToken: cancellationToken);

        var root = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        return new TalvoraGitDiffResponse(
            root,
            staged,
            revisionRange,
            contextLines,
            pathList,
            result.StandardOutput);
    }

    [McpServerTool(
        Name = "talvora_git_log",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitLogResponse)),
     Description("Return structured Git commit history. maxCount=0 means unlimited. revision can be any Git revision or revision range.")]
    public static async Task<TalvoraGitLogResponse> Log(
        string repositoryPath,
        string? revision = null,
        int maxCount = 50,
        int skip = 0,
        bool all = false,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 0 || skip < 0)
        {
            throw new ArgumentOutOfRangeException("maxCount and skip cannot be negative.");
        }

        var path = NormalizeRepositoryPath(repositoryPath);
        var args = new List<string>
        {
            "log",
            "--date=iso-strict",
            $"--format=%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s",
        };

        if (maxCount > 0)
        {
            args.Add($"--max-count={maxCount}");
        }
        if (skip > 0)
        {
            args.Add($"--skip={skip}");
        }
        if (all)
        {
            args.Add("--all");
        }
        if (!string.IsNullOrWhiteSpace(revision))
        {
            args.Add(revision);
        }

        var result = await RunGitCheckedAsync(
            path,
            args,
            timeoutSeconds: 120,
            cancellationToken: cancellationToken);

        var commits = new List<TalvoraGitCommitEntry>();
        foreach (var line in SplitLines(result.StandardOutput))
        {
            var fields = line.Split(FieldSeparator);
            if (fields.Length < 6)
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(fields[4], out var date))
            {
                date = default;
            }

            commits.Add(new TalvoraGitCommitEntry(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                date,
                fields[5]));
        }

        var root = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        return new TalvoraGitLogResponse(
            root,
            commits.Count,
            commits);
    }

    [McpServerTool(
        Name = "talvora_git_branches",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitBranchesResponse)),
     Description("Return structured local and optionally remote Git refs with commit, current-branch marker, upstream tracking, commit date, and subject.")]
    public static async Task<TalvoraGitBranchesResponse> Branches(
        string repositoryPath,
        bool includeRemote = true,
        CancellationToken cancellationToken = default)
    {
        var path = NormalizeRepositoryPath(repositoryPath);
        var format =
            $"%(refname){FieldSeparator}%(objectname){FieldSeparator}%(HEAD){FieldSeparator}" +
            $"%(upstream:short){FieldSeparator}%(upstream:trackshort){FieldSeparator}" +
            $"%(committerdate:iso-strict){FieldSeparator}%(subject)";

        var args = new List<string>
        {
            "for-each-ref",
            $"--format={format}",
            "--sort=refname",
            "refs/heads",
        };

        if (includeRemote)
        {
            args.Add("refs/remotes");
        }

        var result = await RunGitCheckedAsync(
            path,
            args,
            cancellationToken: cancellationToken);

        var branches = new List<TalvoraGitBranchEntry>();
        foreach (var line in SplitLines(result.StandardOutput))
        {
            var fields = line.Split(FieldSeparator);
            if (fields.Length < 7)
            {
                continue;
            }

            DateTimeOffset? date = null;
            if (DateTimeOffset.TryParse(fields[5], out var parsed))
            {
                date = parsed;
            }

            branches.Add(new TalvoraGitBranchEntry(
                fields[0],
                fields[1],
                string.Equals(fields[2], "*", StringComparison.Ordinal),
                string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3],
                string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4],
                date,
                fields[6]));
        }

        var root = (await RunGitCheckedAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        return new TalvoraGitBranchesResponse(root, branches.Count, branches);
    }

    [McpServerTool(
        Name = "talvora_git_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitRunResponse)),
     Description("Run Git with arbitrary arguments in any accessible repository or working directory. No Git subcommand, ref, remote, path, or option denylist/allowlist is applied.")]
    public static Task<TalvoraGitRunResponse> Run(
        string repositoryPath,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        return RunGitAsync(
            NormalizeRepositoryPath(repositoryPath),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static string NormalizeRepositoryPath(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        var fullPath = Path.GetFullPath(repositoryPath);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Repository parent path could not be resolved.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Git working directory was not found: {fullPath}");
        }

        return fullPath;
    }

    private static async Task<TalvoraGitRunResponse> RunGitCheckedAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Git failed with exit code {result.ExitCode}. TimedOut={result.TimedOut}. stderr={result.StandardError}");
        }

        return result;
    }

    private static async Task<TalvoraGitRunResponse> RunGitAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var git = ResolveGitExecutable();
        var argumentList = arguments.ToArray();

        var startInfo = new ProcessStartInfo
        {
            FileName = git,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start git.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSeconds > 0)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        return new TalvoraGitRunResponse(
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut,
            process.Id,
            git,
            workingDirectory,
            argumentList);
    }

    private static string ResolveGitExecutable()
    {
        var candidates = new List<string>();

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory, "git.exe"));
            candidates.Add(Path.Combine(directory, "git"));
        }

        candidates.Add(@"C:\Program Files\Git\cmd\git.exe");
        candidates.Add(@"C:\Program Files\Git\bin\git.exe");

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return "git";
    }

    private static string[] SplitLines(string value) =>
        value.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries);
}
