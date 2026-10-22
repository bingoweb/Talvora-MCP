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
        if (tool == "git_run" && succeeded)
        {
            var arguments = FactStrings(json, "arguments");
            var subcommand = GitArgumentParser.GetSubcommand(arguments);
            if (subcommand == "commit")
            {
                var match = Regex.Match(output, @"^\[[^\r\n\]]+ (?<hash>[0-9a-f]{7,40})\] (?<subject>[^\r\n]+)",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    var subject = match.Groups["subject"].Value;
                    return match.Groups["hash"].Value + " · " + subject[..Math.Min(subject.Length, 60)];
                }
            }
            if (subcommand == "push")
            {
                var index = Array.IndexOf(arguments, subcommand);
                // Destination facts require the result's explicit remote and branch.
                if (index + 2 < arguments.Length && !arguments[index + 1].StartsWith('-') &&
                    !arguments[index + 2].StartsWith('-'))
                    return arguments[index + 1] + "/" + arguments[index + 2];
            }
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
