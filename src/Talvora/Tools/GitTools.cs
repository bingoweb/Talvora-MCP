using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

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
    IReadOnlyList<TalvoraGitCommitEntry> Commits,
    long Skip = 0,
    bool Truncated = false,
    long? NextSkip = null);

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
    private const int AbsoluteGitLogResults = 10_000;

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
        var identity =
            await ResolveRepositoryIdentityAsync(
                path,
                cancellationToken);

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

        TalvoraGitRunResponse? status = null;
        if (!identity.Bare)
        {
            status = await RunGitCheckedAsync(
                path,
                ["status", "--porcelain=v1", "--untracked-files=all"],
                cancellationToken: cancellationToken);
        }

        var remotes = await RunGitCheckedAsync(
            path,
            ["remote", "-v"],
            cancellationToken: cancellationToken);

        return new TalvoraGitInfoResponse(
            Path.GetFullPath(repositoryPath),
            identity.Root,
            identity.GitDirectory,
            identity.Bare,
            head,
            branch,
            branch is null,
            status is not null &&
                !string.IsNullOrEmpty(status.StandardOutput),
            TextLines.Split(remotes.StandardOutput));
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
        var lines = TextLines.Split(result.StandardOutput);

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
                        int.TryParse(
                            token.AsSpan(1),
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var parsedAhead))
                    {
                        ahead = parsedAhead;
                    }
                    else if (token.StartsWith('-') &&
                             int.TryParse(
                                 token.AsSpan(1),
                                 System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out var parsedBehind))
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

        var root = (
            await ResolveRepositoryIdentityAsync(
                path,
                cancellationToken)).Root;

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

        var root = (
            await ResolveRepositoryIdentityAsync(
                path,
                cancellationToken)).Root;

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
     Description("Return structured Git commit history. maxCount=0 requests the finite server maximum page. Use skip/nextSkip to continue the same revision selection while refs are unchanged; revision can be any Git revision or revision range.")]
    public static async Task<TalvoraGitLogResponse> Log(
        string repositoryPath,
        string? revision = null,
        int maxCount = 50,
        long skip = 0,
        bool all = false,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCount),
                "maxCount cannot be negative.");
        }

        if (skip < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skip),
                "skip cannot be negative.");
        }

        var effectiveMaxCount =
            maxCount == 0
                ? AbsoluteGitLogResults
                : Math.Min(
                    maxCount,
                    AbsoluteGitLogResults);

        var path = NormalizeRepositoryPath(repositoryPath);
        var args = new List<string>
        {
            "log",
            "--date=iso-strict",
            $"--format=%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s",
        };

        args.Add($"--max-count={effectiveMaxCount + 1}");
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
        foreach (var line in TextLines.Split(result.StandardOutput))
        {
            var fields = line.Split(FieldSeparator);
            if (fields.Length < 6)
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(
                    fields[4],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var date))
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

        var truncated = commits.Count > effectiveMaxCount;
        if (truncated)
        {
            commits.RemoveRange(
                effectiveMaxCount,
                commits.Count - effectiveMaxCount);
        }

        var root = (
            await ResolveRepositoryIdentityAsync(
                path,
                cancellationToken)).Root;

        return new TalvoraGitLogResponse(
            root,
            commits.Count,
            commits,
            skip,
            truncated,
            truncated
                ? checked(skip + commits.Count)
                : null);
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
        foreach (var line in TextLines.Split(result.StandardOutput))
        {
            var fields = line.Split(FieldSeparator);
            if (fields.Length < 7)
            {
                continue;
            }

            DateTimeOffset? date = null;
            if (DateTimeOffset.TryParse(
                    fields[5],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
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

        var root = (
            await ResolveRepositoryIdentityAsync(
                path,
                cancellationToken)).Root;

        return new TalvoraGitBranchesResponse(root, branches.Count, branches);
    }

    [McpServerTool(
        Name = "talvora_git_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitRunResponse)),
     Description("Run Git with arbitrary arguments in any accessible repository or working directory. Known working-tree mutation commands in recognized development workspaces route to talvora_apply_patch by default; explicitAdmin=true deliberately preserves the unrestricted Git administration path. Read/status/history/fetch/push workflows remain directly available. HTTPS pushes to github.com automatically run in the logged-on Windows user session so Git Credential Manager can use that user\'s cached OAuth credential.")]
    public static Task<TalvoraGitRunResponse> Run(
        string repositoryPath,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        bool explicitAdmin = false,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        var normalizedRepositoryPath =
            NormalizeRepositoryPath(repositoryPath);
        SourceMutationPolicy.EnsureGitWorkingTreeMutationAllowed(
            normalizedRepositoryPath,
            arguments,
            "talvora_git_run",
            explicitAdmin);

        return RunGitAsync(
            normalizedRepositoryPath,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<(
        string Root,
        string GitDirectory,
        bool Bare)> ResolveRepositoryIdentityAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var bareText = (await RunGitCheckedAsync(
            workingDirectory,
            ["rev-parse", "--is-bare-repository"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        var bare = string.Equals(
            bareText,
            "true",
            StringComparison.OrdinalIgnoreCase);

        var gitDirectory = (await RunGitCheckedAsync(
            workingDirectory,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        if (bare)
        {
            return (gitDirectory, gitDirectory, true);
        }

        var root = (await RunGitCheckedAsync(
            workingDirectory,
            ["rev-parse", "--show-toplevel"],
            cancellationToken: cancellationToken)).StandardOutput.Trim();

        return (root, gitDirectory, false);
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
        var git = ResolveGitExecutable();
        var requestedArguments = arguments.ToArray();

        ProcessExecutionResult result;
        if (await ShouldUseInteractiveUserForGitHubPushAsync(
                git,
                workingDirectory,
                requestedArguments,
                cancellationToken).ConfigureAwait(false))
        {
            var interactiveEnvironment = environment is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(environment);

            interactiveEnvironment["GIT_TERMINAL_PROMPT"] = "0";
            interactiveEnvironment["GCM_INTERACTIVE"] = "0";
            interactiveEnvironment["GCM_GUI_PROMPT"] = "0";

            result = await InteractiveUserProcessRunner.RunAsync(
                git,
                workingDirectory,
                requestedArguments,
                interactiveEnvironment,
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await ProcessRunner.RunAsync(
                git,
                workingDirectory,
                requestedArguments,
                environment,
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }

        return new TalvoraGitRunResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments);
    }

    private static async Task<bool> ShouldUseInteractiveUserForGitHubPushAsync(
        string git,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var pushIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(
                    arguments[index],
                    "push",
                    StringComparison.OrdinalIgnoreCase))
            {
                pushIndex = index;
                break;
            }
        }

        if (pushIndex < 0)
        {
            return false;
        }

        var remote = ExtractPushRemote(arguments, pushIndex);
        if (string.IsNullOrWhiteSpace(remote))
        {
            var branch = await RunGitServiceAsync(
                git,
                workingDirectory,
                ["symbolic-ref", "--quiet", "--short", "HEAD"],
                cancellationToken).ConfigureAwait(false);

            if (branch.ExitCode == 0)
            {
                var branchName = branch.StandardOutput.Trim();
                if (!string.IsNullOrWhiteSpace(branchName))
                {
                    var configured = await RunGitServiceAsync(
                        git,
                        workingDirectory,
                        ["config", "--get", $"branch.{branchName}.remote"],
                        cancellationToken).ConfigureAwait(false);

                    if (configured.ExitCode == 0)
                    {
                        remote = configured.StandardOutput.Trim();
                    }
                }
            }

            remote = string.IsNullOrWhiteSpace(remote)
                ? "origin"
                : remote;
        }

        string remoteUrl;
        if (Uri.TryCreate(remote, UriKind.Absolute, out var directUri))
        {
            remoteUrl = directUri.AbsoluteUri;
        }
        else
        {
            var resolved = await RunGitServiceAsync(
                git,
                workingDirectory,
                ["remote", "get-url", "--push", remote],
                cancellationToken).ConfigureAwait(false);

            if (resolved.ExitCode != 0)
            {
                return false;
            }

            remoteUrl = resolved.StandardOutput.Trim();
        }

        return Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) &&
            (string.Equals(
                 uri.Scheme,
                 Uri.UriSchemeHttps,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 uri.Scheme,
                 Uri.UriSchemeHttp,
                 StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(
                uri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPushRemote(
        IReadOnlyList<string> arguments,
        int pushIndex)
    {
        for (var index = pushIndex + 1; index < arguments.Count; index++)
        {
            var value = arguments[index];

            if (value is "--repo" or "--receive-pack" or "--exec" or "--push-option" or "-o")
            {
                index++;
                continue;
            }

            if (value.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            return value;
        }

        return null;
    }

    private static Task<ProcessExecutionResult> RunGitServiceAsync(
        string git,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(
            git,
            workingDirectory,
            arguments,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

    private static string ResolveGitExecutable() =>
        CommandResolver.Resolve(
            ["git.exe", "git"],
            [@"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files\Git\bin\git.exe"])
        ?? "git.exe";
}