using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private const int MaximumEvidenceFiles = 8;
    private const int MaximumPreviewLines = 18;
    private const int MaximumPreviewCharacters = 2400;

    private static DesktopProgressEvidence? BuildInitialEvidence(
        string? toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        var normalized = NormalizeToolName(toolName);
        if (normalized == "apply_patch")
        {
            var patch = GetArgumentText(arguments, "patch");
            var workspaceRoot = GetArgumentText(arguments, "workspaceRoot");
            return BuildPatchEvidence(patch, workspaceRoot);
        }

        if (normalized is "dotnet_build" or "dotnet_test")
        {
            var target = GetArgumentText(arguments, "target");
            var configuration = GetArgumentText(arguments, "configuration");
            var summary = string.IsNullOrWhiteSpace(target)
                ? "Gerçek doğrulama çalışıyor."
                : $"Hedef: {target}";
            if (!string.IsNullOrWhiteSpace(configuration))
            {
                summary += $"  //  Yapı: {configuration}";
            }

            return new DesktopProgressEvidence(Summary: summary);
        }

        return null;
    }

    private static DesktopProgressEvidence? BuildTerminalEvidence<T>(
        DesktopProgressEvidence? initial,
        T result)
    {
        try
        {
            var json =
                result is CallToolResult callToolResult &&
                callToolResult.StructuredContent is JsonElement structured
                    ? structured
                    : JsonSerializer.SerializeToElement(result);
            var resultSummary = DescribeStructuredResult(json);
            var resultFiles = ExtractStructuredFiles(json);

            if (initial is null &&
                string.IsNullOrWhiteSpace(resultSummary) &&
                resultFiles.Count == 0)
            {
                return null;
            }

            var files =
                initial?.Files is { Count: > 0 }
                    ? initial.Files
                    : resultFiles;
            return new DesktopProgressEvidence(
                Summary: initial?.Summary,
                Files: files,
                AddedLines: initial?.AddedLines,
                RemovedLines: initial?.RemovedLines,
                CodePreview: initial?.CodePreview,
                Result: resultSummary ?? initial?.Result);
        }
        catch
        {
            return initial;
        }
    }

    private static DesktopProgressEvidence? BuildPatchEvidence(
        string patch,
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return null;
        }

        var files = new List<string>();
        var preview = new List<string>();
        var fileCount = 0;
        var added = 0;
        var removed = 0;

        foreach (var rawLine in patch.Replace("\r\n", "\n").Split('\n'))
        {
            if (rawLine.StartsWith("*** Update File: ", StringComparison.Ordinal) ||
                rawLine.StartsWith("*** Add File: ", StringComparison.Ordinal) ||
                rawLine.StartsWith("*** Delete File: ", StringComparison.Ordinal))
            {
                var separator = rawLine.IndexOf(": ", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    fileCount++;
                    AddEvidenceFile(
                        files,
                        rawLine[(separator + 2)..],
                        workspaceRoot);
                }
                continue;
            }

            if (rawLine.StartsWith("+", StringComparison.Ordinal) &&
                !rawLine.StartsWith("+++", StringComparison.Ordinal))
            {
                added++;
                AddPreviewLine(preview, rawLine);
            }
            else if (rawLine.StartsWith("-", StringComparison.Ordinal) &&
                     !rawLine.StartsWith("---", StringComparison.Ordinal))
            {
                removed++;
                AddPreviewLine(preview, rawLine);
            }
        }

        var summary =
            $"{fileCount} dosya  //  +{added}  -{removed}";
        return new DesktopProgressEvidence(
            Summary: summary,
            Files: files,
            AddedLines: added,
            RemovedLines: removed,
            CodePreview: JoinPreview(preview));
    }

    private static void AddEvidenceFile(
        List<string> files,
        string path,
        string workspaceRoot)
    {
        if (files.Count >= MaximumEvidenceFiles ||
            string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var cleaned = path.Trim();
        if (!string.IsNullOrWhiteSpace(workspaceRoot) &&
            cleaned.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            cleaned =
                cleaned[workspaceRoot.Length..]
                    .TrimStart('\\', '/');
        }

        if (!files.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(cleaned);
        }
    }

    private static void AddPreviewLine(
        List<string> preview,
        string line)
    {
        if (preview.Count >= MaximumPreviewLines)
        {
            return;
        }

        preview.Add(RedactSensitivePreviewLine(line));
    }

    private static string RedactSensitivePreviewLine(string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("password") ||
            lower.Contains("secret") ||
            lower.Contains("api_key") ||
            lower.Contains("apikey") ||
            lower.Contains("authorization") ||
            lower.Contains("bearer ") ||
            lower.Contains("private_key") ||
            lower.Contains("access_token") ||
            lower.Contains("auth_token") ||
            lower.Contains("client_secret") ||
            lower.Contains("token =") ||
            lower.Contains("token:") ||
            lower.Contains("\"token\""))
        {
            return line.Length == 0
                ? line
                : $"{line[0]} [gizli değer gösterilmedi]";
        }

        return FileLog.RedactSensitiveData(line);
    }

    private static string? JoinPreview(IReadOnlyList<string> preview)
    {
        if (preview.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var line in preview)
        {
            if (builder.Length + line.Length + Environment.NewLine.Length >
                MaximumPreviewCharacters)
            {
                builder.AppendLine("…");
                break;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string? DescribeStructuredResult(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetPropertyIgnoreCase(json, "success", out var success) &&
            success.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            if (success.GetBoolean())
            {
                return "Sonuç: başarılı";
            }

            var failure = TryDescribeError(json);
            return string.IsNullOrWhiteSpace(failure)
                ? "Sonuç: tamamlanamadı"
                : $"Sonuç: tamamlanamadı  //  {failure}";
        }

        if (TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code))
        {
            return code == 0
                ? "Sonuç: başarılı"
                : $"Sonuç: tamamlanamadı  //  kod {code}";
        }

        return null;
    }

    private static string? TryDescribeError(JsonElement json)
    {
        if (!TryGetPropertyIgnoreCase(json, "error", out var error))
        {
            return null;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            return LimitEvidenceText(error.GetString());
        }

        if (error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? code = null;
        string? message = null;
        if (TryGetPropertyIgnoreCase(error, "code", out var codeElement) &&
            codeElement.ValueKind == JsonValueKind.String)
        {
            code = codeElement.GetString();
        }

        if (TryGetPropertyIgnoreCase(error, "message", out var messageElement) &&
            messageElement.ValueKind == JsonValueKind.String)
        {
            message = messageElement.GetString();
        }

        var combined =
            string.Join(
                " — ",
                new[] { code, message }
                    .Where(static value =>
                        !string.IsNullOrWhiteSpace(value)));
        return LimitEvidenceText(combined);
    }

    private static string? LimitEvidenceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const int maximumCharacters = 420;
        var cleaned =
            FileLog.RedactSensitiveData(value)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        return cleaned.Length <= maximumCharacters
            ? cleaned
            : cleaned[..maximumCharacters] + "…";
    }

    private static IReadOnlyList<string> ExtractStructuredFiles(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(json, "files", out var filesElement) ||
            filesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var files = new List<string>();
        foreach (var item in filesElement.EnumerateArray())
        {
            if (files.Count >= MaximumEvidenceFiles ||
                item.ValueKind != JsonValueKind.Object)
            {
                break;
            }

            string? path = null;
            if (TryGetPropertyIgnoreCase(item, "newPath", out var newPath) &&
                newPath.ValueKind == JsonValueKind.String)
            {
                path = newPath.GetString();
            }
            else if (TryGetPropertyIgnoreCase(item, "oldPath", out var oldPath) &&
                     oldPath.ValueKind == JsonValueKind.String)
            {
                path = oldPath.GetString();
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                files.Add(path);
            }
        }

        return files;
    }
}
