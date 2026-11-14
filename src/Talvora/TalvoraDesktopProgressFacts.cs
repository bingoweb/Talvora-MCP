using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Shared;
using Talvora.Tools;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private static bool IsSourceEditTool(string tool) =>
        tool is "apply_patch" or "apply_edits" or "structural_edit" or "semantic_edit";

    private static string? FactText(JsonElement value, string key) =>
        TryGetPropertyIgnoreCase(value, key, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

    private static int? FactNumber(JsonElement value, string key) =>
        TryGetPropertyIgnoreCase(value, key, out var field) && field.ValueKind == JsonValueKind.Number &&
        field.TryGetInt32(out var number) && number >= 0 ? number : null;

    private static string[] FactStrings(JsonElement value, string key) =>
        TryGetPropertyIgnoreCase(value, key, out var field) && field.ValueKind == JsonValueKind.Array
            ? field.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).ToArray() : [];

    private static string DescribeFactFiles(IReadOnlyList<string> files)
    {
        var names = files.Take(2).Select(path => Path.GetFileName(path.Replace('\\', '/'))).ToArray();
        return string.Join(", ", names) + (files.Count > 2 ? $" ve {files.Count - 2} dosya" : string.Empty);
    }

    private static (int? Added, int? Removed) ReceiptCounts(JsonElement json)
    {
        if (!TryGetPropertyIgnoreCase(json, "files", out var files) || files.ValueKind != JsonValueKind.Array)
            return (null, null);
        long added = 0, removed = 0;
        foreach (var file in files.EnumerateArray())
        {
            var a = FactNumber(file, "addedLines");
            var r = FactNumber(file, "removedLines");
            if (a is null || r is null) return (null, null);
            added += a.Value;
            removed += r.Value;
        }
        return added <= int.MaxValue && removed <= int.MaxValue ? ((int)added, (int)removed) : (null, null);
    }

    /// <summary>WL5: facts from captured output or actual staged-file measurements only.</summary>
    private static string? DescribeWl5ResultFact(
        string tool, JsonElement json, IDictionary<string, JsonElement>? requestArguments)
    {
        if (tool is "git_run" or "user_run" or "run_process" &&
            (FactNumber(json, "exitCode") != 0 ||
            (TryGetPropertyIgnoreCase(json, "timedOut", out var timeout) &&
             timeout.ValueKind == JsonValueKind.True) ||
            (TryGetPropertyIgnoreCase(json, "success", out var success) &&
             success.ValueKind == JsonValueKind.False)))
            return null;
        var output = FactText(json, "standardOutput") ?? "";
        var error = FactText(json, "standardError") ?? "";
        var combined = output + "\n" + error;
        if (tool is "user_run" or "run_process")
        {
            var program = Path.GetFileNameWithoutExtension(GetArgumentText(
                requestArguments, "executable").Replace('\\', '/')).ToLowerInvariant();
            var args = GetArgumentArray(requestArguments, "arguments");
            if (program == "dotnet" && args.Any(arg =>
                arg.Contains(".Regression", StringComparison.OrdinalIgnoreCase) ||
                arg.Contains(".Tests", StringComparison.OrdinalIgnoreCase)))
            {
                var passed = Regex.Match(combined, @"\bPassed:\s*(?<n>\d+)", RegexOptions.IgnoreCase);
                var failed = Regex.Match(combined, @"\bFailed:\s*(?<n>\d+)", RegexOptions.IgnoreCase);
                if (passed.Success && failed.Success)
                    return passed.Groups["n"].Value + " test geçti · " +
                        failed.Groups["n"].Value + " başarısız";
            }
            return null;
        }
        if (tool != "git_run") return null;
        var argv = GetArgumentArray(requestArguments, "arguments");
        if (argv.Count == 0) argv = FactStrings(json, "arguments");
        var subcommand = GitArgumentParser.GetSubcommand(argv)?.ToLowerInvariant();
        var pos = subcommand is null ? -1 : argv.ToList().FindIndex(arg =>
            string.Equals(arg, subcommand, StringComparison.OrdinalIgnoreCase));
        var operands = pos < 0 ? [] : argv.Skip(pos + 1)
            .Where(arg => !arg.StartsWith("-", StringComparison.Ordinal) && arg != "--").ToArray();

        static string? Statistics(string text)
        {
            var stat = Regex.Match(text,
                @"(?m)^\s*(?<files>\d+) files? changed(?:, (?<added>\d+) insertions?\(\+\))?(?:, (?<removed>\d+) deletions?\(-\))?\s*$",
                RegexOptions.CultureInvariant);
            if (stat.Success)
                return stat.Groups["files"].Value + " dosya · +" +
                    (stat.Groups["added"].Success ? stat.Groups["added"].Value : "0") +
                    " −" + (stat.Groups["removed"].Success ? stat.Groups["removed"].Value : "0");
            var rows = Regex.Matches(text,
                @"(?m)^(?<added>\d+)\t(?<removed>\d+)\t[^\r\n]+$", RegexOptions.CultureInvariant);
            if (rows.Count == 0) return null;
            long adds = 0, removes = 0;
            foreach (Match row in rows)
            {
                adds += long.Parse(row.Groups["added"].Value, CultureInfo.InvariantCulture);
                removes += long.Parse(row.Groups["removed"].Value, CultureInfo.InvariantCulture);
            }
            return rows.Count + " dosya · +" + adds + " −" + removes;
        }

        switch (subcommand)
        {
            case "fetch":
                if (argv.Any(arg => arg is "-q" or "--quiet")) return null;
                var updates = Regex.Matches(combined,
                    @"(?m)^\s*[+*]?\s*[0-9a-f]{4,40}\.\.{1,2}[0-9a-f]{4,40}\s+[^\r\n]+\s+->\s+\S+",
                    RegexOptions.CultureInvariant).Count;
                return updates == 0 ? "yeni commit yok" : updates + " dalda yeni commit";
            case "pull":
                return combined.Contains("Already up to date", StringComparison.OrdinalIgnoreCase)
                    ? null : Statistics(combined);
            case "diff":
                return Statistics(combined);
            case "add":
                if (argv.Any(arg => arg is "-A" or "-u" or "." or "--all" or "--update"))
                    return FactNumber(json, "stagedFileCount") is { } staged ? staged + " dosya" : null;
                return operands.Length > 0 ? operands.Length + " dosya" : null;
            case "status":
                if (!argv.Any(arg => arg is "--short" or "-s" or "--porcelain" or "--porcelain=v1"))
                    return null;
                var changed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Count(line => line.TrimEnd('\r').Length >= 3 &&
                        !line.StartsWith("## ", StringComparison.Ordinal));
                return changed == 0 ? "değişiklik yok" : changed + " değişmiş dosya";
            case "log":
                var commits = Regex.Matches(output,
                    @"(?m)^commit [a-f0-9]{7,40}\b", RegexOptions.CultureInvariant).Count;
                return commits > 0 ? commits + " commit" : null;
            case "commit":
                var header = Regex.Match(output,
                    @"(?m)^\[[^\r\n\]]+ (?<hash>[0-9a-f]{7,40})\] ",
                    RegexOptions.CultureInvariant);
                var stats = Statistics(output);
                return header.Success && stats is not null
                    ? header.Groups["hash"].Value + " · " + stats : null;
            case "push":
                var refUpdate = Regex.Match(combined,
                    @"(?m)^\s*[+*]?\s*(?<old>[0-9a-f]{7,40})\.\.{1,2}(?<new>[0-9a-f]{7,40})\s+[^\r\n]+\s+->\s+\S+",
                    RegexOptions.CultureInvariant);
                if (refUpdate.Success)
                    return refUpdate.Groups["old"].Value[..7] + ".." + refUpdate.Groups["new"].Value[..7];
                return combined.Contains("Everything up-to-date", StringComparison.OrdinalIgnoreCase)
                    ? "zaten güncel" : null;
            default:
                return null;
        }
    }

    private static string? DescribeResultFact(string tool, JsonElement json)
    {
        var output = FactText(json, "standardOutput") ?? string.Empty;
        var error = FactText(json, "standardError") ?? string.Empty;
        var succeeded = FactNumber(json, "exitCode") == 0 &&
            !(TryGetPropertyIgnoreCase(json, "timedOut", out var timedOut) && timedOut.ValueKind == JsonValueKind.True);
        if (tool is "dotnet_build" or "dotnet_test" or "msbuild_run" or "test_report_summary")
        {
            var summary = TryGetPropertyIgnoreCase(json, "summary", out var supplied) &&
                supplied.ValueKind == JsonValueKind.Object ? supplied : json;
            if (tool is "dotnet_build" or "msbuild_run")
            {
                var errors = FactNumber(summary, "errors");
                var warnings = FactNumber(summary, "warnings");
                if (errors is null && output.Length + error.Length > 0)
                {
                    var parsed = QualityTools.DiagnosticsParse(text: output + "\n" + error);
                    var totals = BuildRunnerTools.FormatDotnetResult(
                        new TalvoraCliCommandResponse(0, output, error, false, 0, "", "", [], 0), true).Summary!;
                    if (parsed.Count == 0 && totals.Errors == 0 && totals.Warnings == 0) return null;
                    summary = JsonSerializer.SerializeToElement(parsed);
                    errors = totals.Errors;
                    warnings = totals.Warnings;
                }
                if (errors is null) return null;
                var paths = TryGetPropertyIgnoreCase(summary, "diagnostics", out var diagnostics) &&
                    diagnostics.ValueKind == JsonValueKind.Array
                    ? diagnostics.EnumerateArray().Select(item => FactText(item, "file"))
                        .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
                return $"{errors} hata" + (warnings is not null ? $" · {warnings} uyarı" : "") +
                    (paths.Length > 0 ? " · " + DescribeFactFiles(paths) : "");
            }
            var passed = FactNumber(summary, "testsPassed") ?? FactNumber(summary, "passed");
            var failed = FactNumber(summary, "testsFailed") ?? FactNumber(summary, "failed");
            var names = FactStrings(summary, "failedTests");
            if (TryGetPropertyIgnoreCase(summary, "failures", out var failures) &&
                failures.ValueKind == JsonValueKind.Array)
                names = failures.EnumerateArray().Select(item => FactText(item, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).ToArray();
            if (passed is null && failed is null && output.Length + error.Length > 0)
            {
                var parsed = BuildRunnerTools.FormatDotnetResult(
                    new TalvoraCliCommandResponse(0, output, error, false, 0, "", "", [], 0), true).Summary!;
                passed = parsed.TestsPassed;
                failed = parsed.TestsFailed;
                names = parsed.FailedTests.ToArray();
            }
            if (failed > 0)
                return $"{failed} test başarısız" + (names.Length > 0 ? ": " + string.Join(", ", names.Take(3)) : "");
            return passed is not null ? $"{passed} test geçti" : null;
        }
        if (tool.StartsWith("service_", StringComparison.Ordinal))
        {
            var name = FactText(json, "displayName") ?? FactText(json, "serviceName");
            var state = FactText(json, "afterStatus");
            return (name, state) switch
            {
                (not null, "Running") => name + " → çalışıyor",
                (not null, "Stopped") => name + " → durdu",
                _ => null,
            };
        }
        if (tool.StartsWith("choco_", StringComparison.Ordinal) && succeeded)
        {
            var arguments = FactStrings(json, "arguments");
            var action = Array.FindIndex(arguments, value => value is "install" or "upgrade" or "uninstall");
            if (action >= 0 && action + 1 < arguments.Length)
            {
                var package = arguments[action + 1];
                var match = Regex.Match(output, @"(?m)^" + Regex.Escape(package) + @"\s+v?(?<version>\d+\.[\w.+-]+)(?:\s|$)");
                if (match.Success) return package + " " + match.Groups["version"].Value;
            }
        }
        // Quality gate summary line (scripts/Invoke-QualityGate.ps1): "56 kontrol, 0 hata".
        var gate = Regex.Match(output, @"(?m)^(?<checks>\d+) kontrol, (?<errors>\d+) hata\s*$");
        if (gate.Success)
            return gate.Groups["checks"].Value + " kontrol · " + gate.Groups["errors"].Value + " hata";
        var matches = FactNumber(json, "matchCount");
        var filesScanned = FactNumber(json, "filesScanned");
        return tool == "search_text" && matches is not null
            ? (filesScanned is not null ? $"{filesScanned} dosya · " : "") + $"{matches} eşleşme"
            : null;
    }

    private static string EnrichTerminalTitle(string tool, string title, DesktopProgressEvidence? evidence)
    {
        if (tool == "dotnet_build")
        {
            var match = Regex.Match(evidence?.Summary ?? "", @"^(\d+) hata");
            if (match.Success && match.Groups[1].Value != "0")
                return title + " · " + match.Groups[1].Value + " hata";
        }
        if (tool == "dotnet_test")
        {
            var match = Regex.Match(evidence?.Summary ?? "", @"^(\d+) test başarısız");
            if (match.Success) return match.Value;
        }
        return title;
    }
}
