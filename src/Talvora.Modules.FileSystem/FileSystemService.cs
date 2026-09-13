namespace Talvora.Modules.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    public async ValueTask<string> ReadTextAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizePath(path);
        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteTextAsync(
        string path,
        string content,
        bool createParentDirectory = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = NormalizePath(path);
        if (createParentDirectory)
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = NormalizePath(path);
        File.Delete(fullPath);
        return ValueTask.CompletedTask;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
