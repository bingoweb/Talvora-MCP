using System.Text;

namespace Talvora.Shared;

public static class AtomicFile
{
    public static async Task<string?> WriteAllTextAsync(
        string path,
        string content,
        Encoding encoding,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(encoding);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                $"File directory could not be resolved: {fullPath}");

        Directory.CreateDirectory(directory);

        string? backupPath = null;
        if (createBackup && File.Exists(fullPath))
        {
            backupPath = fullPath + ".bak";
            File.Copy(fullPath, backupPath, overwrite: true);
        }

        var tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(fullPath) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                content,
                encoding,
                cancellationToken).ConfigureAwait(false);

            File.Move(tempPath, fullPath, overwrite: true);
            return backupPath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
