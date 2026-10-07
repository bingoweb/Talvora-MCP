using System.Globalization;
using System.Text.Json;

namespace Talvora.SourceEditing;

internal static class ProjectDeliveryGate
{
    private const string Context7Evidence = ".context7/verification.json";
    private const string AwwwardsEvidence = ".talvora/awwwards-verification.json";
    private static readonly TimeSpan MaximumEvidenceAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(15);

    public static void EnsureSatisfied(string workingDirectory)
    {
        var root = ResolveProjectRoot(workingDirectory);
        ValidateContext7(root);
        ValidateAwwwards(root);
    }

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

    private static void ValidateContext7(string root)
    {
        var path = Path.Combine(root, Context7Evidence.Replace('/', Path.DirectorySeparatorChar));
        using var document = ReadEvidence(path, "Context7");
        var evidence = document.RootElement;
        RequireSchemaVersion(evidence, "Context7");
        RequireFreshTimestamp(evidence, "Context7");
        if (!evidence.TryGetProperty("scope", out var scope) ||
            scope.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(scope.GetString()))
        {
            throw Blocked("Context7 evidence must describe the verified project scope.");
        }
        if (!evidence.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array ||
            libraries.GetArrayLength() == 0)
        {
            throw Blocked("Context7 evidence must contain at least one verified library.");
        }
        foreach (var library in libraries.EnumerateArray())
        {
            var libraryId = library.TryGetProperty("libraryId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var verified = library.TryGetProperty("verified", out var guidance) && guidance.ValueKind == JsonValueKind.String ? guidance.GetString() : null;
            if (string.IsNullOrWhiteSpace(libraryId) ||
                !libraryId.StartsWith("/", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(verified))
            {
                throw Blocked("Every Context7 library entry must contain a canonical /org/project libraryId and verified guidance.");
            }
        }
        if (!evidence.TryGetProperty("vendorSources", out var vendorSources) ||
            vendorSources.ValueKind != JsonValueKind.Array ||
            vendorSources.GetArrayLength() == 0)
        {
            throw Blocked("Context7 evidence must include at least one official/vendor source confirmation.");
        }
    }

    private static void ValidateAwwwards(string root)
    {
        var path = Path.Combine(root, AwwwardsEvidence.Replace('/', Path.DirectorySeparatorChar));
        using var document = ReadEvidence(path, "Awwwards");
        var evidence = document.RootElement;
        RequireSchemaVersion(evidence, "Awwwards");
        RequireFreshTimestamp(evidence, "Awwwards");
        var sourceUrl = evidence.TryGetProperty("sourceUrl", out var source) && source.ValueKind == JsonValueKind.String ? source.GetString() : null;
        if (!IsAwwwardsUrl(sourceUrl))
        {
            throw Blocked("Awwwards evidence sourceUrl must point to https://www.awwwards.com/.");
        }
        if (!evidence.TryGetProperty("references", out var references) ||
            references.ValueKind != JsonValueKind.Array ||
            references.GetArrayLength() == 0 ||
            !references.EnumerateArray().Any(reference =>
                reference.ValueKind == JsonValueKind.String &&
                IsAwwwardsUrl(reference.GetString())))
        {
            throw Blocked("Awwwards delivery evidence must include at least one reviewed Awwwards reference.");
        }
        if (!evidence.TryGetProperty("designDecisions", out var decisions) ||
            decisions.ValueKind != JsonValueKind.Array ||
            decisions.GetArrayLength() == 0 ||
            !decisions.EnumerateArray().Any(decision =>
                decision.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(decision.GetString())))
        {
            throw Blocked("Awwwards delivery evidence must record at least one concrete design decision or explicit non-UI applicability decision.");
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
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw Blocked($"{gate} evidence is not valid JSON: {ex.Message}");
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
        if (now - parsed > MaximumEvidenceAge)
        {
            throw Blocked($"{gate} evidence is older than 14 days and must be refreshed before delivery.");
        }
    }

    private static bool IsAwwwardsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "awwwards.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "www.awwwards.com", StringComparison.OrdinalIgnoreCase));

    private static InvalidOperationException Blocked(string reason) =>
        new($"TALVORA_DELIVERY_GATE_BLOCKED: {reason}");
}
