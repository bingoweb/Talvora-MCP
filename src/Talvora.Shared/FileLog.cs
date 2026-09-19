using System.Text;

namespace Talvora.Shared;

public static class FileLog
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

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

                var line = $"{DateTimeOffset.Now:O} {message}";
                if (exception is not null)
                {
                    line += $" :: {exception.GetType().Name}: {exception.Message}";
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
