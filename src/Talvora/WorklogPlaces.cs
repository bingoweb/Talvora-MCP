using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Shared;
using Talvora.WorklogDiff;

namespace Talvora;

/// <summary>
/// "nerede / fark" of a committed source edit (DESIGN-WORKLOG §6.2): real per-file counts from the
/// engine receipts and the declarations each hunk touched, read from the hunk headers of the real diff
/// preview ("@@ … @@ context" or a Talvora patch "@@ anchor"), named by <see cref="DeclarationLine"/>.
/// Nothing is guessed: a hunk without header context contributes no place.
/// </summary>
internal static partial class WorklogPlaces
{
    internal static IReadOnlyList<WorklogFileChange>? Build(JsonElement result, string? preview)
    {
        if (!TryGet(result, "files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var places = PlacesByFile(preview);
        var changes = new List<WorklogFileChange>();
        foreach (var file in files.EnumerateArray())
        {
            var path = Text(file, "newPath") ?? Text(file, "oldPath");
            var added = Number(file, "addedLines");
            var removed = Number(file, "removedLines");
            if (string.IsNullOrWhiteSpace(path) || added is null || removed is null)
            {
                return null; // counts must be real for every file, otherwise show only the fact line
            }
            var name = Path.GetFileName(path.Replace('\\', '/'));
            changes.Add(new WorklogFileChange(
                name, added.Value, removed.Value,
                places.TryGetValue(name, out var found) ? found.Distinct(StringComparer.Ordinal).Take(DesktopProgressProtocol.MaximumPlacesPerFile).ToArray() : []));
        }
        return changes.Count == 0 ? null : changes.Take(DesktopProgressProtocol.MaximumChangedFiles).ToArray();
    }

    /// <summary>File name → place names, in hunk order, from a unified diff or a Talvora patch preview.</summary>
    internal static Dictionary<string, List<string>> PlacesByFile(string? preview)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(preview))
        {
            return result;
        }

        string? current = null;
        foreach (var line in preview.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("@@ FILE ", StringComparison.Ordinal))
            {
                current = Path.GetFileName(line[8..].Trim().Replace('\\', '/'));
                continue;
            }
            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var path = line[4..].Split('\t')[0].Trim('"');
                if (path.StartsWith("b/", StringComparison.Ordinal)) path = path[2..];
                current = path == "/dev/null" ? current : Path.GetFileName(path.Replace('\\', '/'));
                continue;
            }
            if (current is null || !line.StartsWith("@@", StringComparison.Ordinal))
            {
                continue;
            }

            var unified = UnifiedHunk().Match(line);
            var context = unified.Success ? unified.Groups["context"].Value : line[2..];
            if (DeclarationLine.PlaceName(context) is { } place)
            {
                if (!result.TryGetValue(current, out var list))
                {
                    result[current] = list = [];
                }
                list.Add(place);
            }
        }
        return result;
    }

    private static bool TryGet(JsonElement json, string name, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string? Text(JsonElement json, string name) =>
        TryGet(json, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement json, string name) =>
        TryGet(json, name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= 0 ? number : null;

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+\d+(?:,\d+)? @@ ?(?<context>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UnifiedHunk();
}
