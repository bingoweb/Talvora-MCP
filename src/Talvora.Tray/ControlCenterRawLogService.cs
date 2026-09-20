using System.IO;
using System.Text;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ControlCenterRawLogService
{
    private const int MaxTailBytes = 512 * 1024;
    private const int MaxLines = 1200;

    public static string ReadTail(
        ManagedMcpRegistration? registration = null)
    {
        var paths = new List<string>
        {
            TrayLog.PathName,
        };

        if (registration is not null)
        {
            paths.AddRange(
                registration.DiscoveryHints
                    .Where(hint => string.Equals(
                        hint.Kind,
                        "log-file",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(hint => hint.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        var lines = new Queue<string>();

        foreach (var path in paths
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AppendTail(path, lines);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendTail(
        string path,
        Queue<string> lines)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var start = Math.Max(0, stream.Length - MaxTailBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);

            if (start > 0)
            {
                _ = reader.ReadLine();
            }

            var sourceLabel = Path.GetFileName(path);
            lines.Enqueue($"--- {sourceLabel} ---");

            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(
                    FileLog.RedactSensitiveData(line));
                while (lines.Count > MaxLines)
                {
                    _ = lines.Dequeue();
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            FileLog.Write(
                TrayLog.PathName,
                $"Raw log tail could not be read: {path}",
                ex,
                maxBytes: 3L * 1024 * 1024);
        }
    }

    internal static void AssertBoundedReadContract()
    {
        if (MaxTailBytes > 1024 * 1024 || MaxLines > 2000)
        {
            throw new InvalidOperationException(
                "Raw log viewer limits are unexpectedly large.");
        }
    }
}
