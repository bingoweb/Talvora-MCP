using System.Buffers;
using System.Text;

namespace Talvora.Shared;

internal sealed record BoundedTextCaptureResult(
    string Text,
    long TotalCharacters,
    bool Truncated,
    bool Interrupted);

internal static class BoundedTextCapture
{
    public const int DefaultMaximumCharacters =
        4 * 1024 * 1024;
    public const int AbsoluteMaximumCharacters =
        16 * 1024 * 1024;

    public static async Task<BoundedTextCaptureResult> ReadAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters));
        }
        if (maximumCharacters > AbsoluteMaximumCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                maximumCharacters,
                $"Maximum capture must not exceed {AbsoluteMaximumCharacters} characters per stream.");
        }

        var headLimit = maximumCharacters / 2;
        var tailLimit = maximumCharacters - headLimit;
        var head = new StringBuilder(
            Math.Min(headLimit, 64 * 1024));
        var tail = new char[tailLimit];
        var tailCount = 0;
        var tailWriteIndex = 0;
        var buffer = ArrayPool<char>.Shared.Rent(16 * 1024);
        long totalCharacters = 0;

        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await reader.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return BuildResult(
                        head,
                        tail,
                        tailCount,
                        tailWriteIndex,
                        totalCharacters,
                        maximumCharacters,
                        interrupted: true);
                }

                if (read == 0)
                {
                    return BuildResult(
                        head,
                        tail,
                        tailCount,
                        tailWriteIndex,
                        totalCharacters,
                        maximumCharacters,
                        interrupted: false);
                }

                totalCharacters = checked(totalCharacters + read);
                var offset = 0;
                if (head.Length < headLimit)
                {
                    var accepted = Math.Min(
                        headLimit - head.Length,
                        read);
                    head.Append(
                        buffer,
                        0,
                        accepted);
                    offset = accepted;
                }

                while (offset < read && tail.Length > 0)
                {
                    tail[tailWriteIndex] = buffer[offset++];
                    tailWriteIndex =
                        (tailWriteIndex + 1) % tail.Length;
                    if (tailCount < tail.Length)
                    {
                        tailCount++;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static BoundedTextCaptureResult BuildResult(
        StringBuilder head,
        char[] tail,
        int tailCount,
        int tailWriteIndex,
        long totalCharacters,
        int maximumCharacters,
        bool interrupted)
    {
        var tailText = BuildTailText(
            tail,
            tailCount,
            tailWriteIndex);
        var truncated = totalCharacters > maximumCharacters;
        if (!truncated)
        {
            return new BoundedTextCaptureResult(
                head.ToString() + tailText,
                totalCharacters,
                Truncated: false,
                Interrupted: interrupted);
        }

        var marker =
            Environment.NewLine +
            $"...[Talvora output truncated: totalCharacters={totalCharacters}, retainedCharacters={maximumCharacters}]..." +
            Environment.NewLine;
        return new BoundedTextCaptureResult(
            head.ToString() + marker + tailText,
            totalCharacters,
            Truncated: true,
            Interrupted: interrupted);
    }

    private static string BuildTailText(
        char[] tail,
        int tailCount,
        int tailWriteIndex)
    {
        if (tailCount == 0)
        {
            return string.Empty;
        }

        if (tailCount < tail.Length)
        {
            return new string(tail, 0, tailCount);
        }

        return string.Concat(
            new string(
                tail,
                tailWriteIndex,
                tail.Length - tailWriteIndex),
            new string(
                tail,
                0,
                tailWriteIndex));
    }

    public static async Task<BoundedTextCaptureResult> ReadFileAsync(
        string path,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new BoundedTextCaptureResult(
                string.Empty,
                0,
                Truncated: false,
                Interrupted: false);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);

        return await ReadAsync(
                reader,
                maximumCharacters,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

public static class ProcessOutputPump
{
    public static async Task PumpToUtf8FileAsync(
        TextReader reader,
        string path)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var buffer = ArrayPool<char>.Shared.Rent(16 * 1024);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024,
                leaveOpen: false);

            while (true)
            {
                var read = await reader.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await writer.WriteAsync(
                        buffer.AsMemory(0, read),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await writer.FlushAsync(
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
