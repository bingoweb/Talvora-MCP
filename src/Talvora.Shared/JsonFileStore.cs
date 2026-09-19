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
