using System.Security.Cryptography;
using System.Text;

namespace Talvora.SourceEditing;

internal sealed record SourceTextLine(
    string Content,
    string Terminator);

internal static class SourceTextCodec
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly SourceTextEncodingDescriptor Utf8 =
        new("utf-8", StrictUtf8, []);

    private static readonly SourceTextEncodingDescriptor Utf8Bom =
        new(
            "utf-8-bom",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            [0xEF, 0xBB, 0xBF]);

    private static readonly SourceTextEncodingDescriptor Utf16LeBom =
        new(
            "utf-16le-bom",
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            [0xFF, 0xFE]);

    private static readonly SourceTextEncodingDescriptor Utf16BeBom =
        new(
            "utf-16be-bom",
            new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
            [0xFE, 0xFF]);

    private static readonly SourceTextEncodingDescriptor Utf32LeBom =
        new(
            "utf-32le-bom",
            new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true),
            [0xFF, 0xFE, 0x00, 0x00]);

    private static readonly SourceTextEncodingDescriptor Utf32BeBom =
        new(
            "utf-32be-bom",
            new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true),
            [0x00, 0x00, 0xFE, 0xFF]);

    public static async Task<SourceFileSnapshot> ReadSnapshotAsync(
        string fullPath,
        string relativePath,
        bool requireText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath))
        {
            return new SourceFileSnapshot(
                fullPath,
                relativePath,
                false,
                null,
                0,
                default,
                null,
                null);
        }

        var info = new FileInfo(fullPath);
        var attributes = info.Attributes;
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "Source edit target is a directory, not a regular file.",
                fullPath);
        }

        if (!requireText)
        {
            var streamedRevision =
                await ComputeRevisionAsync(
                    fullPath,
                    cancellationToken);
            info.Refresh();
            return new SourceFileSnapshot(
                fullPath,
                relativePath,
                true,
                streamedRevision,
                info.Exists ? info.Length : 0,
                attributes,
                info.Exists ? info.LastWriteTimeUtc : null,
                null);
        }

        var bytes =
            await SourceTextStreamingReader.ReadAllBytesBoundedAsync(
                fullPath,
                SourceTextStreamingReader.MaximumMaterializedSourceBytes,
                cancellationToken);
        var revision = SourceEditRevision.Format(SHA256.HashData(bytes));
        var document = Decode(bytes, fullPath);

        info.Refresh();
        return new SourceFileSnapshot(
            fullPath,
            relativePath,
            true,
            revision,
            bytes.LongLength,
            attributes,
            info.Exists ? info.LastWriteTimeUtc : null,
            document);
    }

    public static SourceTextDocument Decode(
        ReadOnlySpan<byte> bytes,
        string? path = null)
    {
        var descriptor = DetectEncoding(bytes, path, out var preambleLength);
        string text;
        try
        {
            text = descriptor.Encoding.GetString(bytes[preambleLength..]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.UnsupportedEncoding,
                "The file is not valid in its detected text encoding.",
                path,
                innerException: ex);
        }

        var newline = ClassifyNewline(text);
        return new SourceTextDocument(
            text,
            descriptor,
            newline,
            HasFinalNewline(text),
            bytes.Length);
    }

    public static byte[] Encode(SourceTextDocument document)
    {
        try
        {
            var body = document.Encoding.Encoding.GetBytes(document.Text);
            if (document.Encoding.Preamble.Length == 0)
            {
                return body;
            }

            var result = new byte[document.Encoding.Preamble.Length + body.Length];
            document.Encoding.Preamble.CopyTo(result, 0);
            body.CopyTo(result, document.Encoding.Preamble.Length);
            return result;
        }
        catch (EncoderFallbackException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.UnsupportedEncoding,
                "The proposed text cannot be represented in the original file encoding.",
                innerException: ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ResourceLimit,
                "The proposed source document is too large to encode safely.",
                innerException: ex);
        }
    }

    public static SourceTextEncodingDescriptor ParseEncoding(string? name)
    {
        var normalized = string.IsNullOrWhiteSpace(name)
            ? "utf-8"
            : name.Trim().ToLowerInvariant();

        return normalized switch
        {
            "utf-8" or "utf8" => Utf8,
            "utf-8-bom" or "utf8-bom" => Utf8Bom,
            "utf-16le-bom" or "utf16le-bom" => Utf16LeBom,
            "utf-16be-bom" or "utf16be-bom" => Utf16BeBom,
            "utf-32le-bom" or "utf32le-bom" => Utf32LeBom,
            "utf-32be-bom" or "utf32be-bom" => Utf32BeBom,
            _ => throw new SourceEditDomainException(
                SourceEditCodes.UnsupportedEncoding,
                $"Unsupported source encoding '{name}'.")
        };
    }

    public static bool IsLikelyTextPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }

        if (HasKnownBom(bytes))
        {
            try
            {
                _ = Decode(bytes);
                return true;
            }
            catch (SourceEditDomainException)
            {
                return false;
            }
        }

        var sampleLength = Math.Min(bytes.Length, 8192);
        if (bytes[..sampleLength].Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static async Task<string> ComputeRevisionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        return SourceEditRevision.Format(hash.GetHashAndReset());
    }

    public static IReadOnlyList<SourceTextLine> SplitLines(string text)
    {
        var lines = new List<SourceTextLine>();
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r')
            {
                var terminator = "\r";
                var end = index;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    terminator = "\r\n";
                    index++;
                }

                lines.Add(new SourceTextLine(text[start..end], terminator));
                start = index + 1;
            }
            else if (character == '\n')
            {
                lines.Add(new SourceTextLine(text[start..index], "\n"));
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(new SourceTextLine(text[start..], string.Empty));
        }
        else if (text.Length == 0)
        {
            lines.Add(new SourceTextLine(string.Empty, string.Empty));
        }
        else if (!HasFinalNewline(text))
        {
            lines.Add(new SourceTextLine(string.Empty, string.Empty));
        }

        return lines;
    }

    public static string JoinLines(IReadOnlyList<SourceTextLine> lines)
    {
        var capacity = 0L;
        foreach (var line in lines)
        {
            capacity += line.Content.Length + line.Terminator.Length;
        }

        if (capacity > int.MaxValue)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ResourceLimit,
                "The proposed source document exceeds the maximum .NET string size.");
        }

        var builder = new StringBuilder((int)capacity);
        foreach (var line in lines)
        {
            builder.Append(line.Content);
            builder.Append(line.Terminator);
        }

        return builder.ToString();
    }

    public static string GetPreferredNewline(
        SourceTextDocument document,
        IReadOnlyList<SourceTextLine>? lines = null,
        int nearbyLineIndex = -1)
    {
        if (lines is not null && nearbyLineIndex >= 0)
        {
            for (var distance = 0; distance < lines.Count; distance++)
            {
                var before = nearbyLineIndex - distance;
                if (before >= 0 && !string.IsNullOrEmpty(lines[before].Terminator))
                {
                    return lines[before].Terminator;
                }

                var after = nearbyLineIndex + distance;
                if (after < lines.Count && !string.IsNullOrEmpty(lines[after].Terminator))
                {
                    return lines[after].Terminator;
                }
            }
        }

        return document.Newline switch
        {
            "crlf" => "\r\n",
            "lf" => "\n",
            "cr" => "\r",
            _ => Environment.NewLine,
        };
    }

    public static string NormalizeInsertedNewlines(
        string text,
        string? requestedPolicy,
        SourceTextDocument? existingDocument)
    {
        var policy = string.IsNullOrWhiteSpace(requestedPolicy)
            ? "preserve"
            : requestedPolicy.Trim().ToLowerInvariant();

        if (policy == "exact")
        {
            return text;
        }

        var target = policy switch
        {
            "crlf" => "\r\n",
            "lf" => "\n",
            "cr" => "\r",
            "preserve" or "auto" => existingDocument is null
                ? Environment.NewLine
                : GetPreferredNewline(existingDocument),
            _ => throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                $"Unsupported newline policy '{requestedPolicy}'."),
        };

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return normalized.Replace("\n", target, StringComparison.Ordinal);
    }

    public static (string Text, int LinesRead, bool EndReached) SliceLines(
        string text,
        int startLine,
        int lineCount)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }

        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        var lines = SplitLines(text);
        var logicalCount =
            lines.Count == 1 &&
            lines[0].Content.Length == 0 &&
            lines[0].Terminator.Length == 0 &&
            text.Length == 0
                ? 0
                : lines.Count;

        if (logicalCount == 0 || startLine > logicalCount)
        {
            return (string.Empty, 0, true);
        }

        var startIndex = startLine - 1;
        var requested = lineCount == 0
            ? logicalCount - startIndex
            : Math.Min(lineCount, logicalCount - startIndex);
        var selected = lines.Skip(startIndex).Take(requested).ToArray();
        var result = JoinLines(selected);
        var endReached = startIndex + requested >= logicalCount;
        return (result, requested, endReached);
    }

    internal static SourceTextEncodingDescriptor DetectEncodingForStreamingRead(
        ReadOnlySpan<byte> bytes,
        string? path,
        out int preambleLength) =>
        DetectEncoding(
            bytes,
            path,
            out preambleLength);

    private static SourceTextEncodingDescriptor DetectEncoding(
        ReadOnlySpan<byte> bytes,
        string? path,
        out int preambleLength)
    {
        if (bytes.StartsWith(Utf32BeBom.Preamble))
        {
            preambleLength = Utf32BeBom.Preamble.Length;
            return Utf32BeBom;
        }

        if (bytes.StartsWith(Utf32LeBom.Preamble))
        {
            preambleLength = Utf32LeBom.Preamble.Length;
            return Utf32LeBom;
        }

        if (bytes.StartsWith(Utf8Bom.Preamble))
        {
            preambleLength = Utf8Bom.Preamble.Length;
            return Utf8Bom;
        }

        if (bytes.StartsWith(Utf16LeBom.Preamble))
        {
            preambleLength = Utf16LeBom.Preamble.Length;
            return Utf16LeBom;
        }

        if (bytes.StartsWith(Utf16BeBom.Preamble))
        {
            preambleLength = Utf16BeBom.Preamble.Length;
            return Utf16BeBom;
        }

        var sampleLength = Math.Min(bytes.Length, 8192);
        if (bytes[..sampleLength].Contains((byte)0))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.UnsupportedEncoding,
                "BOM-less text containing NUL bytes is treated as binary/unsupported encoding.",
                path);
        }

        preambleLength = 0;
        return Utf8;
    }

    private static bool HasKnownBom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Utf8Bom.Preamble) ||
        bytes.StartsWith(Utf16LeBom.Preamble) ||
        bytes.StartsWith(Utf16BeBom.Preamble) ||
        bytes.StartsWith(Utf32LeBom.Preamble) ||
        bytes.StartsWith(Utf32BeBom.Preamble);

    private static string ClassifyNewline(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    index++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[index] == '\n')
            {
                lf++;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0)
        {
            return "none";
        }

        if (kinds > 1)
        {
            return "mixed";
        }

        return crlf > 0 ? "crlf" : lf > 0 ? "lf" : "cr";
    }

    private static bool HasFinalNewline(string text) =>
        text.EndsWith("\n", StringComparison.Ordinal) ||
        text.EndsWith("\r", StringComparison.Ordinal);
}
