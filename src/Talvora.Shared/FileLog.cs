using System.Text;
using System.Text.RegularExpressions;

namespace Talvora.Shared;

public static class FileLog
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex NamedSecretRegex = new(
        @"\b(?<name>authorization|proxy-authorization|api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|id[-_ ]?token|client[-_ ]?secret|password|passwd|pwd|secret|credential)\b(?<separator>\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;}\]]+)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);
    private static readonly Regex AuthorizationSchemeRegex = new(
        @"\b(?<scheme>Bearer|Basic)\s+[A-Za-z0-9._~+\/-]+=*",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);
    private static readonly Regex OpenAiKeyRegex = new(
        @"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{12,}\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);
    private static readonly Regex GitHubTokenRegex = new(
        @"\bgh[pousr]_[A-Za-z0-9]{20,}\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);
    private static readonly Regex ModalCredentialRegex = new(
        @"\b(?:ak|as|wk|ws|oc|ov)-[A-Za-z0-9._~-]{8,}\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);
    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public static void Write(
        string path,
        string message,
        Exception? exception = null,
        long maxBytes = 5L * 1024 * 1024)
    {
        try
        {
            lock (Sync)
            {
                var fullPath = Path.GetFullPath(path);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded(fullPath, maxBytes);

                var line =
                    $"{DateTimeOffset.Now:O} {RedactSensitiveData(message)}";
                if (exception is not null)
                {
                    line +=
                        $" :: {exception.GetType().Name}: " +
                        RedactSensitiveData(exception.Message);
                }

                File.AppendAllText(
                    fullPath,
                    line + Environment.NewLine,
                    Utf8NoBom);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
        }
    }

    public static string RedactSensitiveData(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = AuthorizationSchemeRegex.Replace(
            value,
            "${scheme} [REDACTED]");
        redacted = NamedSecretRegex.Replace(
            redacted,
            "${name}${separator}[REDACTED]");
        redacted = OpenAiKeyRegex.Replace(
            redacted,
            "[REDACTED]");
        redacted = GitHubTokenRegex.Replace(
            redacted,
            "[REDACTED]");
        redacted = ModalCredentialRegex.Replace(
            redacted,
            "[REDACTED]");
        return JwtRegex.Replace(
            redacted,
            "[REDACTED]");
    }

    private static void RotateIfNeeded(string path, long maxBytes)
    {
        if (maxBytes <= 0 || !File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length < maxBytes)
        {
            return;
        }

        var archived = path + ".1";
        File.Move(path, archived, overwrite: true);
    }
}
