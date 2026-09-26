using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Talvora.Shared;

public static class AtomicFile
{
    private const int DurableBufferSize = 64 * 1024;

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

        var requestedBackupPath =
            createBackup
                ? fullPath + ".bak"
                : null;

        var tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(fullPath) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await WriteDurableTextAsync(
                tempPath,
                content,
                encoding,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return PublishDurable(
                tempPath,
                fullPath,
                requestedBackupPath);
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

    private static async Task WriteDurableTextAsync(
        string tempPath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = DurableBufferSize,
                Options =
                    FileOptions.Asynchronous |
                    FileOptions.WriteThrough,
            });

        await using (var writer = new StreamWriter(
            stream,
            encoding,
            DurableBufferSize,
            leaveOpen: true))
        {
            await writer.WriteAsync(
                content.AsMemory(),
                cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(
                cancellationToken).ConfigureAwait(false);
        }

        stream.Flush(flushToDisk: true);
    }

    private static string? PublishDurable(
        string sourcePath,
        string destinationPath,
        string? backupPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(
                sourcePath,
                destinationPath,
                backupPath,
                ignoreMetadataErrors: false);
            return backupPath;
        }

        MoveDurable(
            sourcePath,
            destinationPath,
            replaceExisting: false);
        return null;
    }

    private static void MoveDurable(
        string sourcePath,
        string destinationPath,
        bool replaceExisting)
    {
        var flags = MoveFileFlags.WriteThrough;
        if (replaceExisting)
        {
            flags |= MoveFileFlags.ReplaceExisting;
        }

        if (!MoveFileExW(
                sourcePath,
                destinationPath,
                flags))
        {
            throw new IOException(
                $"Durable file publication failed moving '{sourcePath}' to '{destinationPath}'.",
                new Win32Exception(
                    Marshal.GetLastWin32Error()));
        }
    }

    private static void TryDeleteTemp(string tempPath)
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

    [Flags]
    private enum MoveFileFlags : uint
    {
        ReplaceExisting = 0x1,
        WriteThrough = 0x8,
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "MoveFileExW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(
        string existingFileName,
        string newFileName,
        MoveFileFlags flags);
}
