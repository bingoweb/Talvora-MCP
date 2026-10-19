using System.Text.Json;
using System.Text.RegularExpressions;

namespace Talvora.SourceEditing;

internal static class WorkContractGate
{
    private const string ContractFile = "work-contract.json";

    public static async Task<string?> ValidateAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        Func<string[], CancellationToken, Task<string>> queryGit,
        CancellationToken cancellationToken)
    {
        var contractPath = Path.Combine(
            repositoryPath, ".talvora", ContractFile);
        if (!File.Exists(contractPath) ||
            !string.Equals(
                Talvora.Tools.GitArgumentParser.GetSubcommand(arguments),
                "commit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Contract configuration;
        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    contractPath, cancellationToken));
            var json = document.RootElement;
            var version = json.GetProperty("schemaVersion").GetInt32();
            var statusFile = json.GetProperty("statusFile").GetString();
            var cardTrailer = json.GetProperty("cardTrailer").GetString();
            var auditTagPrefix = json.GetProperty("auditTagPrefix").GetString();
            var legacyTrailers = json.GetProperty("legacyCardTrailers")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .ToArray();
            if (version != 1 ||
                string.IsNullOrWhiteSpace(statusFile) ||
                string.IsNullOrWhiteSpace(cardTrailer) ||
                string.IsNullOrWhiteSpace(auditTagPrefix) ||
                legacyTrailers.Any(string.IsNullOrWhiteSpace))
            {
                return "Invalid work-contract configuration.";
            }

            configuration = new Contract(
                statusFile, cardTrailer, legacyTrailers, auditTagPrefix);
        }
        catch (Exception ex) when (ex is JsonException or
            KeyNotFoundException or InvalidOperationException or
            FormatException)
        {
            return "Invalid work-contract configuration.";
        }

        if (arguments.Any(argument =>
                string.Equals(argument, "--amend",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "Git commit --amend is forbidden by the work contract.";
        }

        var message = await ReadMessageAsync(
            repositoryPath, arguments, cancellationToken);
        if (message is null)
        {
            return "Git commit message must use -m, -F, or --file.";
        }

        var trailers = FindTrailers(message, configuration);
        if (trailers.Count != 1 ||
            !string.Equals(
                message.TrimEnd().Split('\n')[^1].Trim(),
                trailers.Count == 1 ? trailers[0].FullLine : string.Empty,
                StringComparison.Ordinal))
        {
            return "Git commit message must end with exactly one Card: <ID> trailer (or its configured legacy trailer).";
        }

        var staged = await queryGit(
            ["diff", "--cached", "--name-only"],
            cancellationToken);
        if (!staged.Replace('\\', '/').Split('\n')
                .Any(path => string.Equals(
                    path.Trim(), configuration.StatusFile,
                    StringComparison.Ordinal)))
        {
            return $"Required status file is not staged: {configuration.StatusFile}";
        }

        var tagNames = await queryGit(
            [
                "for-each-ref",
                "--sort=-refname",
                "--format=%(refname:short)",
                $"refs/tags/{configuration.AuditTagPrefix}",
            ],
            cancellationToken);
        var latestTag = tagNames.Split('\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var revisionRange = latestTag is null
            ? "HEAD"
            : latestTag + "..HEAD";

        var commits = await queryGit(
            ["log", revisionRange, "--format=%B%x00"],
            cancellationToken);
        var cardId = trailers[0].Id;
        var repeated = commits.Split('\0')
            .Any(commit => FindTrailers(commit, configuration)
                .Any(trailer => string.Equals(
                    trailer.Id, cardId, StringComparison.Ordinal)));
        if (repeated && !Regex.IsMatch(
                message, @"(?m)^CI follow-up: [^\r\n]+$",
                RegexOptions.CultureInvariant))
        {
            return $"A commit for card {cardId} already exists after the newest audit tag.";
        }

        return null;
    }

    private sealed record Contract(
        string StatusFile,
        string CardTrailer,
        IReadOnlyList<string> LegacyTrailers,
        string AuditTagPrefix);

    private sealed record Trailer(string FullLine, string Id);

    private static IReadOnlyList<Trailer> FindTrailers(
        string message, Contract configuration)
    {
        var names = new[] { configuration.CardTrailer }
            .Concat(configuration.LegacyTrailers)
            .Select(Regex.Escape);
        var expression = new Regex(
            @"^(?:" + string.Join("|", names) +
            @"):[ \t]+(?<id>[A-Za-z0-9][A-Za-z0-9._-]*)[ \t]*$",
            RegexOptions.CultureInvariant);
        return message.Replace("\r\n", "\n").Split('\n')
            .Select(line => (Line: line, Match: expression.Match(line)))
            .Where(pair => pair.Match.Success)
            .Select(pair => new Trailer(
                pair.Line, pair.Match.Groups["id"].Value))
            .ToArray();
    }

    private static async Task<string?> ReadMessageAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string? value = null;
            var file = false;
            if (argument is "-m" or "--message")
            {
                if (++index >= arguments.Count) return null;
                value = arguments[index];
            }
            else if (argument.StartsWith("--message=", StringComparison.Ordinal))
            {
                value = argument["--message=".Length..];
            }
            else if (argument.StartsWith("-m", StringComparison.Ordinal) &&
                argument.Length > 2)
            {
                value = argument[2..];
            }
            else if (argument is "-F" or "--file")
            {
                if (++index >= arguments.Count) return null;
                value = arguments[index];
                file = true;
            }
            else if (argument.StartsWith("--file=", StringComparison.Ordinal))
            {
                value = argument["--file=".Length..];
                file = true;
            }
            else if (argument.StartsWith("-F", StringComparison.Ordinal) &&
                argument.Length > 2)
            {
                value = argument[2..];
                file = true;
            }

            if (value is null) continue;
            if (file)
            {
                var path = Path.GetFullPath(
                    Path.IsPathRooted(value)
                        ? value
                        : Path.Combine(repositoryPath, value));
                if (!File.Exists(path))
                {
                    return null;
                }
                value = await File.ReadAllTextAsync(path, cancellationToken);
            }
            messages.Add(value);
        }

        return messages.Count == 0
            ? null
            : string.Join("\n\n", messages);
    }
}
