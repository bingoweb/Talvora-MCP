using System.Text.RegularExpressions;
using Talvora.Shared;

namespace Talvora.WorklogDiff;

/// <summary>
/// Recognises a declaration line (type/module, function or method, PowerShell function, Markdown heading)
/// and returns its name. Used for the "nerede" row (N10) and for hunk-header context in generated diffs (N3b).
/// Returns null for anything else, so no free code text is ever shown as a place.
/// </summary>
internal static partial class DeclarationLine
{
    internal static string? PlaceName(string? line)
    {
        var text = line?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (Heading().Match(text) is { Success: true } heading)
        {
            return Clip(heading.Groups["text"].Value.Trim());
        }
        if (TypeDeclaration().Match(text) is { Success: true } type)
        {
            return Clip(type.Groups["name"].Value);
        }
        if (PowerShellFunction().Match(text) is { Success: true } function)
        {
            return Clip(function.Groups["name"].Value);
        }
        foreach (Match call in Callable().Matches(text))
        {
            var name = call.Groups["name"].Value;
            if (!Keywords.Contains(name))
            {
                return Clip(name);
            }
        }
        return null;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "catch", "using", "return", "lock", "fixed",
        "sizeof", "typeof", "nameof", "default", "new", "await", "function", "elif", "else",
    };

    private static string Clip(string value) =>
        value.Length <= DesktopProgressProtocol.MaximumPlaceCharacters
            ? value
            : value[..(DesktopProgressProtocol.MaximumPlaceCharacters - 1)] + "…";

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\b(?:class|record|struct|interface|enum|namespace|module|trait|impl)\s+(?<name>[\p{L}_][\p{L}\p{N}_.]*)", RegexOptions.CultureInvariant)]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*function\s+(?<name>[\p{L}_][\p{L}\p{N}_-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellFunction();

    [GeneratedRegex(@"(?<name>[\p{L}_$][\p{L}\p{N}_$.]*)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex Callable();
}
