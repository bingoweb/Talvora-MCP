using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Shared;
using Talvora.Tools;

namespace Talvora.SourceEditing;

internal static class ProjectDeliveryGate
{
    private const string Context7Evidence = ".context7/verification.json";
    private const int MaximumEvidenceFileBytes = 256 * 1024;
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(15);
    private static readonly Regex QuestionPattern = new(
        @"\(query: ""([^""]+)""\)", RegexOptions.CultureInvariant);

    public static void EnsureSatisfied(string workingDirectory, IReadOnlyList<string>? pushArguments = null)
    {
        var root = ResolveProjectRoot(workingDirectory);
        EnsureSatisfiedCore(root, pushArguments, null);
    }

    internal static void EnsureSatisfied(
        string workingDirectory, IReadOnlyList<string> pushArguments, string ledgerDirectory)
    {
        var root = ResolveProjectRoot(workingDirectory);
        EnsureSatisfiedCore(root, pushArguments, ledgerDirectory);
    }

    private static void EnsureSatisfiedCore(
        string root, IReadOnlyList<string>? pushArguments, string? ledgerDirectory)
    {
        if (pushArguments is null)
        {
            ValidateContext7(root, committed: false);
            return;
        }

        var changed = GetPushChangedPaths(root, pushArguments);
        if (changed.All(IsDocumentationPath))
            return;

        if (!changed.Contains(Context7Evidence, StringComparer.Ordinal))
            throw Blocked($"Context7 evidence must be committed with source changes: {Context7Evidence}");

        var questions = ValidateContext7(root, committed: true);
        if (questions.Count == 0)
            return;

        var ledger = new Context7Ledger(ledgerDirectory);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var records = ledger.ReadSince(now.AddHours(-24));
            foreach (var (id, question) in questions)
            {
                if (records.Any(record => record.Status == 200 &&
                    !TalvoraLineTrafficMeter.IsOwnClient(record.Caller) &&
                    record.Ts <= now && record.LibraryId == id && record.Query == question))
                    continue;
                throw Blocked($"Context7 evidence names a question Talvora did not record: {id} \"{question}\". Ask it with talvora_context7_docs and copy the question exactly.");
            }
        }
        finally
        {
            ledger.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static IReadOnlyList<string> GetPushChangedPaths(
        string root, IReadOnlyList<string> pushArguments)
    {
        var index = -1;
        for (var i = 0; i < pushArguments.Count; i++)
        {
            if (string.Equals(pushArguments[i], "push", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        var values = pushArguments.Skip(index + 1)
            .Where(arg => !arg.StartsWith("-", StringComparison.Ordinal))
            .ToArray();
        var remote = values.Length > 0 ? values[0] : "origin";
        var branch = values.Length > 1 ? values[1] :
            RunGit(root, ["branch", "--show-current"]).Trim();
        if (string.IsNullOrWhiteSpace(branch))
            throw Blocked("Context7 push requires a current branch.");
        var remoteRef = $"refs/remotes/{remote}/{branch}";
        try
        {
            RunGit(root, ["rev-parse", "--verify", "--quiet", remoteRef]);
        }
        catch (InvalidOperationException)
        {
            return RunGit(root, ["diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "HEAD"])
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return RunGit(root, ["diff", "--name-only", remoteRef, "HEAD"])
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string RunGit(string root, string[] arguments)
    {
        try
        {
            return GitTools.RunGitCheckedAsync(root, arguments)
                .GetAwaiter().GetResult().StandardOutput;
        }
        catch (Exception exception) when (exception is not ModelContextProtocol.McpException)
        {
            throw new InvalidOperationException("Context7 Git inspection failed: " + exception.Message, exception);
        }
    }

    private static bool IsDocumentationPath(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("docs/", StringComparison.Ordinal) ||
        path.StartsWith(".context7/", StringComparison.Ordinal) ||
        path.StartsWith(".talvora/", StringComparison.Ordinal);

    private static string ResolveProjectRoot(string workingDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(workingDirectory));
        if (!current.Exists)
        {
            throw Blocked($"Project directory does not exist: {current.FullName}");
        }
        for (var cursor = current; cursor is not null; cursor = cursor.Parent)
        {
            if (Directory.Exists(Path.Combine(cursor.FullName, ".git")) ||
                File.Exists(Path.Combine(cursor.FullName, ".git")))
            {
                return cursor.FullName;
            }
        }
        throw Blocked("A Git project root is required before Talvora can publish or push a project.");
    }

    private static IReadOnlyList<(string Id, string Question)> ValidateContext7(
        string root, bool committed)
    {
        var path = Path.Combine(root, Context7Evidence.Replace('/', Path.DirectorySeparatorChar));
        using var document = committed ? ReadCommittedEvidence(root) : ReadEvidence(path, "Context7");
        var evidence = document.RootElement;
        if (evidence.ValueKind != JsonValueKind.Object)
        {
            throw Blocked("Context7 evidence must be a JSON object.");
        }
        RequireSchemaVersion(evidence, "Context7");
        RequireFreshTimestamp(evidence, "Context7");
        if (!evidence.TryGetProperty("scope", out var scope) ||
            scope.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(scope.GetString()))
        {
            throw Blocked("Context7 evidence must describe the verified project scope.");
        }
        if (!evidence.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
        {
            throw Blocked("Context7 evidence libraries must be an array.");
        }
        if (libraries.GetArrayLength() > 12)
            throw Blocked("Context7 evidence must list at most 12 libraries.");
        var questions = new List<(string Id, string Question)>();
        foreach (var library in libraries.EnumerateArray())
        {
            if (library.ValueKind != JsonValueKind.Object)
            {
                throw Blocked("Every Context7 library entry must be a JSON object.");
            }
            var libraryId = library.TryGetProperty("libraryId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var verified = library.TryGetProperty("verified", out var guidance) && guidance.ValueKind == JsonValueKind.String ? guidance.GetString() : null;
            if (!IsCanonicalLibraryId(libraryId) ||
                string.IsNullOrWhiteSpace(verified))
            {
                throw Blocked("Every Context7 library entry must contain a canonical /org/project libraryId and verified guidance.");
            }
            var matches = QuestionPattern.Matches(verified);
            if (matches.Count == 0)
                throw Blocked("Context7 evidence verified guidance must include (query: \"question\").");
            foreach (Match match in matches)
                questions.Add((libraryId!, match.Groups[1].Value));
        }
        if (questions.Count == 0 && (!evidence.TryGetProperty("noLookup", out var noLookup) ||
            noLookup.ValueKind != JsonValueKind.String ||
            (noLookup.GetString()?.Trim().Length ?? 0) < 20))
            throw Blocked("Context7 evidence noLookup must contain at least 20 characters.");

        if (evidence.TryGetProperty("vendorSources", out var vendorSources) &&
            (vendorSources.ValueKind != JsonValueKind.Array ||
             vendorSources.GetArrayLength() > 12 ||
             vendorSources.EnumerateArray().Any(source =>
                 source.ValueKind != JsonValueKind.String ||
                 !source.GetString()!.StartsWith("https://", StringComparison.Ordinal))))
        {
            throw Blocked("Context7 evidence vendorSources must be 0–12 HTTPS URLs.");
        }
        return questions;
    }

    private static JsonDocument ReadCommittedEvidence(string root)
    {
        string json;
        try
        {
            json = RunGit(root, ["show", $"HEAD:{Context7Evidence}"]);
        }
        catch (InvalidOperationException)
        {
            throw Blocked($"Context7 evidence is missing in HEAD: {Context7Evidence}");
        }
        if (Encoding.UTF8.GetByteCount(json) > MaximumEvidenceFileBytes)
            throw Blocked("Context7 evidence is missing, malformed, or too large.");
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw Blocked($"Context7 evidence is not valid JSON: {ex.Message}");
        }
    }

    private static JsonDocument ReadEvidence(string path, string gate)
    {
        if (!File.Exists(path))
        {
            throw Blocked($"{gate} evidence is mandatory and missing: {path}");
        }
        try
        {
            return JsonFileStore.ReadBounded<JsonDocument>(
                path,
                MaximumEvidenceFileBytes);
        }
        catch (JsonException ex)
        {
            throw Blocked($"{gate} evidence is not valid JSON: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            throw Blocked($"{gate} evidence is missing, malformed, or too large: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw Blocked($"{gate} evidence could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw Blocked($"{gate} evidence is not accessible: {ex.Message}");
        }
    }

    private static void RequireSchemaVersion(JsonElement evidence, string gate)
    {
        if (!evidence.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out var parsed) ||
            parsed != 1)
        {
            throw Blocked($"{gate} evidence schemaVersion must be 1.");
        }
    }

    private static void RequireFreshTimestamp(JsonElement evidence, string gate)
    {
        var value = evidence.TryGetProperty("verifiedAtUtc", out var verifiedAt) && verifiedAt.ValueKind == JsonValueKind.String ? verifiedAt.GetString() : null;
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw Blocked($"{gate} evidence verifiedAtUtc must be a valid UTC timestamp.");
        }
        var now = DateTimeOffset.UtcNow;
        if (parsed > now + MaximumFutureSkew)
        {
            throw Blocked($"{gate} evidence timestamp is unexpectedly in the future.");
        }
        if (parsed.UtcDateTime.Date != now.UtcDateTime.Date)
        {
            throw Blocked($"{gate} evidence verifiedAtUtc must be today (UTC).");
        }
    }

    private static bool IsCanonicalLibraryId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !id.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var components = id.Split('/');
        return components.Length is 3 or 4 &&
            components[0].Length == 0 &&
            components.Skip(1).All(component =>
                !string.IsNullOrWhiteSpace(component) &&
                !component.Any(char.IsWhiteSpace) &&
                !component.Contains('\\'));
    }

    // McpException: the MCP SDK passes its message to the client. Any other exception type is
    // hidden behind "An error occurred invoking ...", so agents could not see why delivery stopped.
    private static ModelContextProtocol.McpException Blocked(string reason) =>
        new($"TALVORA_DELIVERY_GATE_BLOCKED: {reason}");
}
