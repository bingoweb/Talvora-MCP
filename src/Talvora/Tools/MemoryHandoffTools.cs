using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Memory;

namespace Talvora.Tools;

[McpServerToolType]
public static partial class MemoryHandoffTools
{
    private const int MaxHandoffBytes = 512 * 1024;

    [McpServerTool(
        Name = "talvora_memory_handoff_candidates",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryHandoffCandidatesResult)),
     Description("Generate bounded, patch-ready HANDOFF.md candidate markdown from high-value active project memories. This tool never writes HANDOFF.md or any repository file.")]
    public static Task<TalvoraMemoryHandoffCandidatesResult> Candidates(
        string project,
        int limit = 30,
        double minImportance = 0.70,
        double minConfidence = 0.80,
        CancellationToken cancellationToken = default) =>
        TalvoraMemoryRuntime.Store.HandoffCandidatesAsync(
            project,
            limit,
            minImportance,
            minConfidence,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_memory_handoff_review",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMemoryHandoffReviewResult)),
     Description("Review a project's HANDOFF.md without modifying it. Compares high-value memory candidates with the document, inspects live Git HEAD/branch, and for the Talvora repository compares local /healthz sourceCommit. Returns stale/missing issues plus patch-ready suggested markdown; file edits remain an explicit separate source-edit action.")]
    public static async Task<TalvoraMemoryHandoffReviewResult> Review(
        string project,
        string? handoffPath = null,
        int candidateLimit = 30,
        CancellationToken cancellationToken = default)
    {
        var projectRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(project)
                ? throw new ArgumentException(
                    "Project path cannot be empty.",
                    nameof(project))
                : project);
        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Project directory was not found: {projectRoot}");
        }

        var resolvedHandoff = ResolveHandoffPath(
            projectRoot,
            handoffPath);
        var candidates =
            await TalvoraMemoryRuntime.Store.HandoffCandidatesAsync(
                projectRoot,
                candidateLimit,
                minImportance: 0.70,
                minConfidence: 0.80,
                cancellationToken);

        string handoffText = string.Empty;
        var handoffExists = File.Exists(resolvedHandoff);
        if (handoffExists)
        {
            var info = new FileInfo(resolvedHandoff);
            if (info.Length > MaxHandoffBytes)
            {
                throw new InvalidDataException(
                    $"HANDOFF.md exceeds the {MaxHandoffBytes} byte review ceiling.");
            }
            handoffText = await File.ReadAllTextAsync(
                resolvedHandoff,
                cancellationToken);
        }

        var issues = new List<TalvoraMemoryHandoffReviewIssue>();
        if (!handoffExists)
        {
            issues.Add(
                new TalvoraMemoryHandoffReviewIssue(
                    "handoff-missing",
                    "warning",
                    "HANDOFF.md bulunamadı; öneri metni üretildi ancak dosya oluşturulmadı."));
        }

        var (gitBranch, gitHead) = ReadGitHead(projectRoot);
        var currentWindow = GetCurrentWindow(handoffText);
        var runtimeSourceCommit = await TryReadTalvoraRuntimeSourceCommitAsync(
            projectRoot,
            cancellationToken);
        var handoffHasRuntime =
            ContainsCommit(currentWindow, runtimeSourceCommit);
        var handoffHasGit =
            ContainsCommit(currentWindow, gitHead);

        // For Talvora itself, runtime sourceCommit is the canonical deployed
        // fingerprint. A later docs-only Git commit must not force a
        // self-referential HANDOFF rewrite loop. Repositories without a
        // runtime fingerprint still use Git HEAD as their stale boundary.
        if (gitHead is not null &&
            currentWindow.Length > 0 &&
            !handoffHasGit &&
            (runtimeSourceCommit is null || !handoffHasRuntime))
        {
            issues.Add(
                new TalvoraMemoryHandoffReviewIssue(
                    "git-head-stale",
                    "warning",
                    $"HANDOFF current bölümü canlı Git HEAD'i içermiyor. branch={gitBranch ?? "unknown"}, head={gitHead}."));
        }

        if (runtimeSourceCommit is not null)
        {
            if (currentWindow.Length > 0 &&
                !handoffHasRuntime)
            {
                issues.Add(
                    new TalvoraMemoryHandoffReviewIssue(
                        "runtime-source-stale",
                        "warning",
                        $"HANDOFF current bölümü canlı Talvora runtime sourceCommit değerini içermiyor: {runtimeSourceCommit}."));
            }

            if (gitHead is not null &&
                !handoffHasRuntime &&
                !string.Equals(
                    gitHead,
                    runtimeSourceCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    new TalvoraMemoryHandoffReviewIssue(
                        "runtime-repo-divergence",
                        "info",
                        $"Canlı runtime ve repo HEAD farklı. runtime={runtimeSourceCommit}, repo={gitHead}. Bu durum yalnız dokümantasyon commit'i varsa normal olabilir."));
            }
        }

        foreach (var candidate in candidates.Candidates.Take(12))
        {
            if (handoffText.Contains(
                    candidate.Title,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            issues.Add(
                new TalvoraMemoryHandoffReviewIssue(
                    "high-value-memory-missing",
                    "info",
                    $"Yüksek değerli aktif hafıza handoff'ta başlık olarak görünmüyor: {candidate.Title}",
                    candidate.Id));
        }

        return new TalvoraMemoryHandoffReviewResult(
            candidates.Project,
            resolvedHandoff,
            handoffExists,
            gitBranch,
            gitHead,
            runtimeSourceCommit,
            issues.Count,
            issues,
            candidates.SuggestedMarkdown);
    }

    private static bool ContainsCommit(
        string text,
        string? commit)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(commit))
        {
            return false;
        }

        return text.Contains(
                   commit,
                   StringComparison.OrdinalIgnoreCase) ||
               text.Contains(
                   commit[..Math.Min(7, commit.Length)],
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveHandoffPath(
        string projectRoot,
        string? handoffPath)
    {
        var candidate = string.IsNullOrWhiteSpace(handoffPath)
            ? Path.Combine(projectRoot, "HANDOFF.md")
            : Path.IsPathRooted(handoffPath)
                ? Path.GetFullPath(handoffPath)
                : Path.GetFullPath(
                    Path.Combine(projectRoot, handoffPath));

        var rootWithSeparator =
            projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(
                rootWithSeparator,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "HANDOFF review path must remain inside the requested project root.");
        }
        return candidate;
    }

    private static (string? Branch, string? Head) ReadGitHead(
        string projectRoot)
    {
        var gitPath = Path.Combine(projectRoot, ".git");
        string gitDirectory;
        if (Directory.Exists(gitPath))
        {
            gitDirectory = gitPath;
        }
        else if (File.Exists(gitPath))
        {
            var pointer = File.ReadAllText(gitPath).Trim();
            const string prefix = "gitdir:";
            if (!pointer.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (null, null);
            }
            var target = pointer[prefix.Length..].Trim();
            gitDirectory = Path.GetFullPath(
                Path.IsPathRooted(target)
                    ? target
                    : Path.Combine(projectRoot, target));
        }
        else
        {
            return (null, null);
        }

        var headPath = Path.Combine(gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return (null, null);
        }
        var headText = File.ReadAllText(headPath).Trim();
        const string refPrefix = "ref:";
        if (!headText.StartsWith(
                refPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return (null, NormalizeSha(headText));
        }

        var reference = headText[refPrefix.Length..].Trim();
        var branch = reference.StartsWith(
            "refs/heads/",
            StringComparison.Ordinal)
            ? reference["refs/heads/".Length..]
            : reference;
        var looseRef = Path.Combine(
            gitDirectory,
            reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(looseRef))
        {
            return (branch, NormalizeSha(File.ReadAllText(looseRef).Trim()));
        }

        var packedRefs = Path.Combine(gitDirectory, "packed-refs");
        if (File.Exists(packedRefs))
        {
            foreach (var line in File.ReadLines(packedRefs))
            {
                if (line.Length == 0 ||
                    line[0] is '#' or '^')
                {
                    continue;
                }
                var space = line.IndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }
                if (string.Equals(
                        line[(space + 1)..].Trim(),
                        reference,
                        StringComparison.Ordinal))
                {
                    return (
                        branch,
                        NormalizeSha(line[..space]));
                }
            }
        }

        return (branch, null);
    }

    private static string? NormalizeSha(string value) =>
        GitShaRegex().IsMatch(value)
            ? value.ToLowerInvariant()
            : null;

    private static string GetCurrentWindow(string handoffText)
    {
        if (string.IsNullOrWhiteSpace(handoffText))
        {
            return string.Empty;
        }
        var lines = handoffText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        return string.Join(
            '\n',
            lines.Take(140));
    }

    private static async Task<string?> TryReadTalvoraRuntimeSourceCommitAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                Path.GetFileName(projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
                "Talvora-MCP",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2),
            };
            using var response = await client.GetAsync(
                "http://127.0.0.1:7676/healthz",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);
            using var json =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
            return json.RootElement.TryGetProperty(
                    "sourceCommit",
                    out var sourceCommit)
                ? NormalizeSha(sourceCommit.GetString() ?? string.Empty)
                : null;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitShaRegex();
}
