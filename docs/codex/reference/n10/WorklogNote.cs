namespace Talvora.Shared;

/// <summary>Normalises agent-written worklog text (task note, step purpose): one line, trimmed, bounded, redacted.</summary>
public static class WorklogNote
{
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var line = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        line = FileLog.RedactSensitiveData(line);
        return line.Length <= DesktopProgressProtocol.MaximumTaskNoteCharacters
            ? line
            : line[..(DesktopProgressProtocol.MaximumTaskNoteCharacters - 1)] + "…";
    }
}
