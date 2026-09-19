using System.IO;
using System.Text;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ControlCenterRawLogService
{
    private const int MaxTailBytes = 512 * 1024;
    private const int MaxLines = 1200;

    public static string ReadTail()
    {
        var path = TrayLog.PathName;
        if (!File.Exists(path))
        {
            return string.Empty;
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

            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                while (lines.Count > MaxLines)
                {
                    _ = lines.Dequeue();
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            FileLog.Write(
                TrayLog.PathName,
                "Raw tray log tail could not be read",
                ex,
                maxBytes: 3L * 1024 * 1024);
            return string.Empty;
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