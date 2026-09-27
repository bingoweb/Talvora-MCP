namespace Talvora.Shared;

public static class TextLines
{
    public static string[] Split(string value) =>
        value.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries);

    public static string NormalizeTrailingNewline(string value) =>
        value.TrimEnd('\r', '\n');

    public static string EnsureTrailingNewline(string value) =>
        value.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? value
            : NormalizeTrailingNewline(value) + Environment.NewLine;

    public static string? FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            var line = Split(value)
                .FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item));
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }
}
