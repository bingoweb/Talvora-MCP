using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Talvora.SourceEditing;

internal sealed record SourceTextSliceSnapshot(
    long Length,
    string Revision,
    SourceTextEncodingDescriptor Encoding,
    string Newline,
    bool HasFinalNewline,
    int LinesRead,
    bool EndReached,
    string Text,
    int NextStartLine,
    int NextStartCharacter,
    bool ResponseLimited,
    int ResponseLimitCharacters);

internal static class SourceTextStreamingReader
{
    internal const int DefaultReadMaxCharacters =
        1024 * 1024;
    internal const int AbsoluteReadMaxCharacters =
        4 * 1024 * 1024;
    internal const long MaximumMaterializedSourceBytes =
        256L * 1024 * 1024;

    public static async Task<SourceTextSliceSnapshot> ReadSliceAsync(
        string fullPath,
        int startLine,
        int startCharacter,
        int lineCount,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }
        if (startCharacter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startCharacter));
        }
        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }
        if (maxCharacters < 0 ||
            maxCharacters > AbsoluteReadMaxCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                $"maxCharacters must be between 0 and {AbsoluteReadMaxCharacters}.");
        }

        var responseLimit =
            maxCharacters == 0
                ? AbsoluteReadMaxCharacters
                : maxCharacters;
        if (responseLimit < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                "The source read response window must allow at least two UTF-16 characters.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Source file was not found.",
                fullPath);
        }

        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.Directory) != 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "Source edit target is a directory, not a regular file.",
                fullPath);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            options:
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        var length = stream.Length;
        var revision =
            await ComputeRevisionAsync(
                stream,
                cancellationToken).ConfigureAwait(false);

        stream.Position = 0;
        var sampleLength =
            checked((int)Math.Min(8192L, length));
        var sample = new byte[sampleLength];
        var sampleRead = 0;
        while (sampleRead < sample.Length)
        {
            var read =
                await stream.ReadAsync(
                        sample.AsMemory(
                            sampleRead,
                            sample.Length - sampleRead),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            sampleRead += read;
        }

        var descriptor =
            SourceTextCodec.DetectEncodingForStreamingRead(
                sample.AsSpan(0, sampleRead),
                fullPath,
                out var preambleLength);
        stream.Position = preambleLength;

        using var reader = new StreamReader(
            stream,
            descriptor.Encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 128 * 1024,
            leaveOpen: true);
        var collector =
            new SliceCollector(
                fullPath,
                startLine,
                startCharacter,
                lineCount,
                responseLimit);
        var chars =
            ArrayPool<char>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read =
                    await reader.ReadAsync(
                            chars.AsMemory(
                                0,
                                chars.Length),
                            cancellationToken)
                        .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                collector.Process(
                    chars.AsSpan(0, read));
            }
        }
        catch (DecoderFallbackException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.UnsupportedEncoding,
                "The file is not valid in its detected text encoding.",
                fullPath,
                innerException: ex);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }

        var result = collector.Complete();
        return new SourceTextSliceSnapshot(
            length,
            revision,
            descriptor,
            result.Newline,
            result.HasFinalNewline,
            result.LinesRead,
            result.EndReached,
            result.Text,
            result.NextStartLine,
            result.NextStartCharacter,
            result.ResponseLimited,
            responseLimit);
    }

    internal static async Task<byte[]> ReadAllBytesBoundedAsync(
        string fullPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0 ||
            maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes));
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            options:
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw MaterializationLimit(
                fullPath,
                stream.Length,
                maximumBytes);
        }

        byte[] bytes;
        try
        {
            bytes =
                new byte[checked((int)stream.Length)];
        }
        catch (OutOfMemoryException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ResourceLimit,
                "The source file is too large to materialize safely for text editing.",
                fullPath,
                innerException: ex);
        }

        var total = 0;
        while (total < bytes.Length)
        {
            var read =
                await stream.ReadAsync(
                        bytes.AsMemory(
                            total,
                            bytes.Length - total),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
        }

        var probe = new byte[1];
        if (await stream.ReadAsync(
                probe.AsMemory(),
                cancellationToken).ConfigureAwait(false) != 0)
        {
            throw MaterializationLimit(
                fullPath,
                Math.Max(
                    stream.Length,
                    maximumBytes + 1),
                maximumBytes);
        }

        if (total != bytes.Length)
        {
            Array.Resize(ref bytes, total);
        }
        return bytes;
    }

    private static async Task<string> ComputeRevisionAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
        var buffer =
            ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (true)
            {
                var read =
                    await stream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken)
                        .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                hash.AppendData(
                    buffer,
                    0,
                    read);
            }
            return SourceEditRevision.Format(
                hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static SourceEditDomainException MaterializationLimit(
        string fullPath,
        long actualBytes,
        long maximumBytes) =>
        new(
            SourceEditCodes.ResourceLimit,
            $"The source file requires {actualBytes} bytes; canonical text mutation materialization is limited to {maximumBytes} bytes.",
            fullPath);

    private sealed class SliceCollector
    {
        private readonly string path;
        private readonly int startLine;
        private readonly int startCharacter;
        private readonly int lineCount;
        private readonly int maximumCharacters;
        private readonly StringBuilder output;
        private int currentLine = 1;
        private int currentCharacter;
        private int linesRead;
        private int lastCapturedLine;
        private int nextStartLine;
        private int nextStartCharacter;
        private int newlineTokens;
        private int crlfCount;
        private int lfCount;
        private int crCount;
        private bool sawText;
        private bool lastTokenWasNewline;
        private bool pendingCr;
        private char? pendingHighSurrogate;
        private bool invalidStartCharacter;
        private bool responseBudgetExhausted;
        private bool responseLimited;
        private bool hasMore;
        private readonly char[] tokenBuffer = new char[2];

        public SliceCollector(
            string path,
            int startLine,
            int startCharacter,
            int lineCount,
            int maximumCharacters)
        {
            this.path = path;
            this.startLine = startLine;
            this.startCharacter = startCharacter;
            this.lineCount = lineCount;
            this.maximumCharacters = maximumCharacters;
            output =
                new StringBuilder(
                    Math.Min(
                        maximumCharacters,
                        64 * 1024));
            nextStartLine = startLine;
            nextStartCharacter = startCharacter;
        }

        public void Process(ReadOnlySpan<char> characters)
        {
            foreach (var character in characters)
            {
                if (pendingCr)
                {
                    pendingCr = false;
                    if (character == '\n')
                    {
                        ProcessNewline("\r\n");
                        continue;
                    }
                    ProcessNewline("\r");
                }

                if (pendingHighSurrogate.HasValue)
                {
                    var high =
                        pendingHighSurrogate.Value;
                    pendingHighSurrogate = null;
                    if (!char.IsLowSurrogate(character))
                    {
                        throw InvalidUnicode();
                    }

                    tokenBuffer[0] = high;
                    tokenBuffer[1] = character;
                    ProcessContent(
                        tokenBuffer.AsSpan(0, 2));
                    continue;
                }

                if (character == '\r')
                {
                    pendingCr = true;
                    continue;
                }
                if (character == '\n')
                {
                    ProcessNewline("\n");
                    continue;
                }
                if (char.IsHighSurrogate(character))
                {
                    pendingHighSurrogate = character;
                    continue;
                }
                if (char.IsLowSurrogate(character))
                {
                    throw InvalidUnicode();
                }

                tokenBuffer[0] = character;
                ProcessContent(
                    tokenBuffer.AsSpan(0, 1));
            }
        }

        public SliceResult Complete()
        {
            if (pendingCr)
            {
                pendingCr = false;
                ProcessNewline("\r");
            }
            if (pendingHighSurrogate.HasValue)
            {
                throw InvalidUnicode();
            }

            var logicalLines =
                sawText
                    ? newlineTokens +
                      (lastTokenWasNewline ? 0 : 1)
                    : 0;
            if (startLine <= logicalLines &&
                currentLine == startLine &&
                startCharacter > currentCharacter)
            {
                invalidStartCharacter = true;
            }
            if (startLine <= logicalLines &&
                invalidStartCharacter)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startCharacter),
                    $"startCharacter {startCharacter} is outside logical line {startLine} in '{path}'.");
            }
            if (startLine > logicalLines)
            {
                return new SliceResult(
                    string.Empty,
                    0,
                    true,
                    startLine,
                    startCharacter,
                    false,
                    ClassifyNewline(),
                    lastTokenWasNewline);
            }

            return new SliceResult(
                output.ToString(),
                linesRead,
                !hasMore,
                nextStartLine,
                nextStartCharacter,
                responseLimited,
                ClassifyNewline(),
                lastTokenWasNewline);
        }

        private void ProcessContent(ReadOnlySpan<char> token)
        {
            sawText = true;
            lastTokenWasNewline = false;
            if (currentLine == startLine &&
                startCharacter > currentCharacter &&
                startCharacter <
                    currentCharacter + token.Length)
            {
                invalidStartCharacter = true;
            }

            if (!invalidStartCharacter &&
                IsAtOrAfterStart() &&
                IsLineAllowed(currentLine))
            {
                TryAppend(
                    token,
                    currentLine,
                    checked(
                        currentCharacter +
                        token.Length));
            }
            else if (!invalidStartCharacter &&
                     IsAtOrAfterStart())
            {
                hasMore = true;
            }

            currentCharacter =
                checked(
                    currentCharacter +
                    token.Length);
        }

        private void ProcessNewline(string token)
        {
            sawText = true;
            lastTokenWasNewline = true;
            newlineTokens =
                checked(newlineTokens + 1);
            if (token == "\r\n")
            {
                crlfCount++;
            }
            else if (token == "\n")
            {
                lfCount++;
            }
            else
            {
                crCount++;
            }

            if (currentLine == startLine &&
                startCharacter > currentCharacter)
            {
                invalidStartCharacter = true;
            }
            if (!invalidStartCharacter &&
                IsAtOrAfterStart() &&
                IsLineAllowed(currentLine))
            {
                TryAppend(
                    token.AsSpan(),
                    checked(currentLine + 1),
                    0);
            }
            else if (!invalidStartCharacter &&
                     IsAtOrAfterStart())
            {
                hasMore = true;
            }

            if (currentLine == int.MaxValue)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.ResourceLimit,
                    "The source file contains more logical lines than the source read coordinate model supports.",
                    path);
            }
            currentLine++;
            currentCharacter = 0;
        }

        private bool IsAtOrAfterStart() =>
            currentLine > startLine ||
            (currentLine == startLine &&
             currentCharacter >= startCharacter);

        private bool IsLineAllowed(int line)
        {
            if (lineCount == 0)
            {
                return true;
            }
            return line >= startLine &&
                   line <
                       (long)startLine +
                       lineCount;
        }

        private void TryAppend(
            ReadOnlySpan<char> token,
            int afterLine,
            int afterCharacter)
        {
            if (responseBudgetExhausted ||
                token.Length >
                    maximumCharacters -
                    output.Length)
            {
                responseBudgetExhausted = true;
                responseLimited = true;
                hasMore = true;
                return;
            }

            if (lastCapturedLine != currentLine)
            {
                linesRead =
                    checked(linesRead + 1);
                lastCapturedLine = currentLine;
            }
            output.Append(token);
            nextStartLine = afterLine;
            nextStartCharacter = afterCharacter;
            if (output.Length == maximumCharacters)
            {
                responseBudgetExhausted = true;
            }
        }

        private string ClassifyNewline()
        {
            var kinds =
                (crlfCount > 0 ? 1 : 0) +
                (lfCount > 0 ? 1 : 0) +
                (crCount > 0 ? 1 : 0);
            if (kinds == 0)
            {
                return "none";
            }
            if (kinds > 1)
            {
                return "mixed";
            }
            return crlfCount > 0
                ? "crlf"
                : lfCount > 0
                    ? "lf"
                    : "cr";
        }

        private SourceEditDomainException InvalidUnicode() =>
            new(
                SourceEditCodes.UnsupportedEncoding,
                "The decoded source contains an invalid UTF-16 surrogate sequence.",
                path);
    }

    private sealed record SliceResult(
        string Text,
        int LinesRead,
        bool EndReached,
        int NextStartLine,
        int NextStartCharacter,
        bool ResponseLimited,
        string Newline,
        bool HasFinalNewline);
}
