using System.Text;

namespace Talvora.Shared;

public static class TextFileStore
{
    public static async Task<string> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes));
        }

        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);

        var snapshotLength = stream.Length;
        if (snapshotLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"Text document exceeds the {maximumBytes}-byte limit: {fullPath}");
        }

        var bytes = new byte[checked((int)snapshotLength)];
        await stream.ReadExactlyAsync(
                bytes.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        using var memory = new MemoryStream(
            bytes,
            writable: false);
        using var reader = new StreamReader(
            memory,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        return await reader
            .ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
