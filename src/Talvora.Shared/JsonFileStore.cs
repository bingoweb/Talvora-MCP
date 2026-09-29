using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

public static class JsonFileStore
{
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<T> ReadAsync<T>(
        string path,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   options,
                   cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException(
                   $"Invalid JSON document: {fullPath}");
    }

    public static T ReadBounded<T>(
        string path,
        int maximumBytes,
        JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes));
        }

        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        var snapshotLength = stream.Length;
        if (snapshotLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"JSON document exceeds the {maximumBytes}-byte limit: {fullPath}");
        }

        var bytes = new byte[checked((int)snapshotLength)];
        stream.ReadExactly(bytes);
        return DeserializeSnapshot<T>(
            bytes,
            fullPath,
            options);
    }

    public static async Task<T> ReadBoundedAsync<T>(
        string path,
        int maximumBytes,
        JsonSerializerOptions? options = null,
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
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
        var snapshotLength = stream.Length;
        if (snapshotLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"JSON document exceeds the {maximumBytes}-byte limit: {fullPath}");
        }

        var bytes = new byte[checked((int)snapshotLength)];
        await stream.ReadExactlyAsync(
                bytes.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeSnapshot<T>(
            bytes,
            fullPath,
            options);
    }

    private static T DeserializeSnapshot<T>(
        ReadOnlySpan<byte> bytes,
        string fullPath,
        JsonSerializerOptions? options)
    {
        var json = bytes;
        if (json.Length >= 3 &&
            json[0] == 0xEF &&
            json[1] == 0xBB &&
            json[2] == 0xBF)
        {
            json = json[3..];
        }

        return JsonSerializer.Deserialize<T>(
                   json,
                   options)
               ?? throw new InvalidDataException(
                   $"Invalid JSON document: {fullPath}");
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonSerializerOptions? options = null,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, options);
        await AtomicFile.WriteAllTextAsync(
            path,
            json,
            Utf8NoBom,
            createBackup,
            cancellationToken).ConfigureAwait(false);
    }
}
