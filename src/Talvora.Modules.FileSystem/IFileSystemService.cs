namespace Talvora.Modules.FileSystem;

public interface IFileSystemService
{
    ValueTask<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    ValueTask WriteTextAsync(
        string path,
        string content,
        bool createParentDirectory = false,
        CancellationToken cancellationToken = default);

    ValueTask DeleteFileAsync(string path, CancellationToken cancellationToken = default);
}
