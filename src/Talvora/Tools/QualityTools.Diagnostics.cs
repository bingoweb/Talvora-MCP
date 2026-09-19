using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class QualityTools
{
    private static readonly Regex MsbuildDiagnosticRegex = new(
        @"^(?<file>.+?)\((?<line>\d+)(?:,(?<column>\d+))?\):\s*(?<severity>error|warning|info)\s*(?<code>[A-Za-z]+\d+)?\s*:?\s*(?<message>.*?)(?:\s+\[(?<project>.+)\])?$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking);

    private static readonly Regex UnixDiagnosticRegex = new(
        @"^(?<file>.+?):(?<line>\d+):(?<column>\d+):\s*(?<severity>fatal error|error|warning|note|info):\s*(?<message>.*)$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking);

    private static readonly Regex EslintDiagnosticRegex = new(
        @"^\s*(?<line>\d+):(?<column>\d+)\s+(?<severity>error|warning)\s+(?<message>.*?)(?:\s{2,}(?<code>[@\w./-]+))?\s*$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking);

    private static readonly Regex RustDiagnosticRegex = new(
        @"^(?<severity>error|warning)(?:\[(?<code>[^\]]+)\])?:\s*(?<message>.+)$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking);

    private static readonly Regex RustLocationRegex = new(
        @"^\s*-->\s*(?<file>.+?):(?<line>\d+):(?<column>\d+)\s*$",
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking);

    [McpServerTool(
        Name = "talvora_diagnostics_parse",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDiagnosticsResponse)),
     Description("Normalize common compiler/linter diagnostics from inline text or a file. Recognizes MSBuild/.NET/TypeScript, GCC/Clang-style, Rust, and ESLint-style output without executing any tool.")]
    public static TalvoraDiagnosticsResponse DiagnosticsParse(
        string? text = null,
        string? path = null,
        int maxDiagnostics = 1000)
    {
        if (maxDiagnostics < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDiagnostics));
        }

        if (text is null && string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Either text or path is required.");
        }

        var source = "inline";
        IEnumerable<string> lines;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Diagnostics source file was not found.",
                    fullPath);
            }

            lines = File.ReadLines(fullPath);
            source = fullPath;
        }
        else
        {
            lines = SplitLines(text ?? string.Empty);
        }

        var diagnostics = new List<TalvoraDiagnosticEntry>();
        var truncated = false;
        string? eslintFile = null;
        PendingRustDiagnostic? pendingRust = null;

        foreach (var rawLine in lines)
        {
            if (maxDiagnostics > 0 &&
                diagnostics.Count >= maxDiagnostics)
            {
                truncated = true;
                break;
            }

            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (pendingRust is not null)
            {
                var location = RustLocationRegex.Match(line);
                if (location.Success)
                {
                    diagnostics.Add(new TalvoraDiagnosticEntry(
                        NormalizeSeverity(pendingRust.Severity),
                        pendingRust.Code,
                        pendingRust.Message,
                        location.Groups["file"].Value,
                        ParseNullableInt(location.Groups["line"].Value),
                        ParseNullableInt(location.Groups["column"].Value),
                        "rustc"));
                    pendingRust = null;
                    continue;
                }
            }

            var msbuild = MsbuildDiagnosticRegex.Match(line);
            if (msbuild.Success)
            {
                diagnostics.Add(new TalvoraDiagnosticEntry(
                    NormalizeSeverity(msbuild.Groups["severity"].Value),
                    NullIfEmpty(msbuild.Groups["code"].Value),
                    msbuild.Groups["message"].Value.Trim(),
                    msbuild.Groups["file"].Value.Trim(),
                    ParseNullableInt(msbuild.Groups["line"].Value),
                    ParseNullableInt(msbuild.Groups["column"].Value),
                    "msbuild"));
                continue;
            }

            var unix = UnixDiagnosticRegex.Match(line);
            if (unix.Success)
            {
                diagnostics.Add(new TalvoraDiagnosticEntry(
                    NormalizeSeverity(unix.Groups["severity"].Value),
                    null,
                    unix.Groups["message"].Value.Trim(),
                    unix.Groups["file"].Value.Trim(),
                    ParseNullableInt(unix.Groups["line"].Value),
                    ParseNullableInt(unix.Groups["column"].Value),
                    "compiler"));
                continue;
            }

            var rust = RustDiagnosticRegex.Match(line);
            if (rust.Success)
            {
                pendingRust = new PendingRustDiagnostic(
                    rust.Groups["severity"].Value,
                    NullIfEmpty(rust.Groups["code"].Value),
                    rust.Groups["message"].Value.Trim());
                continue;
            }

            var eslint = EslintDiagnosticRegex.Match(line);
            if (eslint.Success && eslintFile is not null)
            {
                diagnostics.Add(new TalvoraDiagnosticEntry(
                    NormalizeSeverity(eslint.Groups["severity"].Value),
                    NullIfEmpty(eslint.Groups["code"].Value),
                    eslint.Groups["message"].Value.Trim(),
                    eslintFile,
                    ParseNullableInt(eslint.Groups["line"].Value),
                    ParseNullableInt(eslint.Groups["column"].Value),
                    "eslint"));
                continue;
            }

            if (!char.IsWhiteSpace(rawLine, 0) &&
                LooksLikeSourcePath(line))
            {
                eslintFile = line.Trim();
            }
        }

        if (!truncated &&
            pendingRust is not null &&
            (maxDiagnostics == 0 ||
             diagnostics.Count < maxDiagnostics))
        {
            diagnostics.Add(new TalvoraDiagnosticEntry(
                NormalizeSeverity(pendingRust.Severity),
                pendingRust.Code,
                pendingRust.Message,
                null,
                null,
                null,
                "rustc"));
        }

        return new TalvoraDiagnosticsResponse(
            source,
            diagnostics.Count,
            diagnostics.Count(item =>
                item.Severity == "error"),
            diagnostics.Count(item =>
                item.Severity == "warning"),
            diagnostics.Count(item =>
                item.Severity == "info"),
            truncated,
            diagnostics);
    }

    private sealed record PendingRustDiagnostic(
        string Severity,
        string? Code,
        string Message);

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static int? ParseNullableInt(string value) =>
        int.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string NormalizeSeverity(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "fatal error" or "error" => "error",
            "warning" => "warning",
            "note" or "info" => "info",
            _ => "info",
        };

    private static bool LooksLikeSourcePath(string line)
    {
        var extension = Path.GetExtension(line);
        return extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vue", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".svelte", StringComparison.OrdinalIgnoreCase);
    }
}
