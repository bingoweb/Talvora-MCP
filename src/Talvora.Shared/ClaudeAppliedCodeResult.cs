using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

/// <summary>Only an acknowledged built-in file-tool result from the approved workspace, never its request.</summary>
public sealed record ClaudeAppliedCodeResult(string Id, string ToolName, string File, JsonElement Result);

internal static class ClaudeCodeResultFilter
{
    internal static ClaudeAppliedCodeResult? Capture(string id, string tool, JsonElement result, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("filePath", out var file) || file.ValueKind != JsonValueKind.String ||
            result.TryGetProperty("staged", out var staged) && staged.ValueKind == JsonValueKind.True)
            return null;
        try
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(file.GetString()!);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            var safe = new Dictionary<string, object?>();
            foreach (var name in new[] { "type", "originalFile", "content", "oldString", "newString" })
            {
                if (!result.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var text = value.GetString()!;
                safe[name + "Bytes"] = Encoding.UTF8.GetByteCount(text);
                if (Encoding.UTF8.GetByteCount(text) <= 1024 * 1024) safe[name] = Scrub(text);
            }
            if (result.TryGetProperty("structuredPatch", out var patches) && patches.ValueKind == JsonValueKind.Array)
            {
                if (patches.GetArrayLength() > 20000) return null;
                safe["structuredPatch"] = patches.EnumerateArray().Select(patch => new
                {
                    oldStart = patch.GetProperty("oldStart").GetInt32(),
                    oldLines = patch.GetProperty("oldLines").GetInt32(),
                    newStart = patch.GetProperty("newStart").GetInt32(),
                    newLines = patch.GetProperty("newLines").GetInt32(),
                    lines = patch.GetProperty("lines").EnumerateArray().Select(line =>
                    {
                        var text = line.GetString() ?? "";
                        return text.Length == 0 ? text : text[..1] + Scrub(text[1..]);
                    }).ToArray(),
                }).ToArray();
            }
            return new(id, tool, path, JsonSerializer.SerializeToElement(safe));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { return null; }
    }

    private static string Scrub(string text) => string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line =>
    {
        var lower = line.ToLowerInvariant();
        return new[] { "password", "secret", "api_key", "apikey", "authorization", "bearer ",
            "private_key", "access_token", "auth_token", "client_secret", "token =", "token:", "\"token\"" }
            .Any(lower.Contains) ? "[gizli değer gösterilmedi]" : FileLog.RedactSensitiveData(line);
    }));
}
