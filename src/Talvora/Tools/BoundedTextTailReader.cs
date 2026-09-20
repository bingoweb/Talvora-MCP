using System.Buffers;
using System.Text;

namespace Talvora.Tools;

internal static class BoundedTextTailReader
{
    public static TalvoraTextTailResponse Read(
        string fullPath,
        int effectiveLineCount,
        long beforeLine,
        int characterLimit,
        bool serverLineCeilingApplied,
        CancellationToken cancellationToken)
    {
        if (effectiveLineCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveLineCount));
        }
        if (characterLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(characterLimit));
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);

        var lines =
            new Queue<TailLine>(
                Math.Min(
                    effectiveLineCount,
                    4096));
        var lineBuffer =
            new BoundedLineSuffixBuffer(
                characterLimit);
        var chars =
            ArrayPool<char>.Shared.Rent(
                64 * 1024);
        long totalLines = 0;
        long eligibleLines = 0;
        long queuedCharacters = 0;
        var characterLimited = false;
        var pendingContent = false;
        var previousWasCarriageReturn = false;

        void RemoveOldest(
            bool dueToCharacterBudget)
        {
            var removed = lines.Dequeue();
            queuedCharacters -=
                removed.Text.Length;
            if (dueToCharacterBudget)
            {
                characterLimited = true;
            }
        }

        void FinalizeLine()
        {
            totalLines =
                checked(totalLines + 1);
            var text =
                lineBuffer.ToText();
            var lineWasLimited =
                lineBuffer.Truncated;
            lineBuffer.Reset();
            pendingContent = false;

            if (beforeLine > 0 &&
                totalLines >= beforeLine)
            {
                return;
            }

            eligibleLines =
                checked(eligibleLines + 1);
            lines.Enqueue(
                new TailLine(
                    totalLines,
                    text,
                    lineWasLimited));
            queuedCharacters +=
                text.Length;

            while (lines.Count >
                   effectiveLineCount)
            {
                RemoveOldest(
                    dueToCharacterBudget: false);
            }

            while (lines.Count > 0 &&
                   queuedCharacters +
                       (long)Math.Max(
                           0,
                           lines.Count - 1) *
                       Environment.NewLine.Length >
                   characterLimit)
            {
                RemoveOldest(
                    dueToCharacterBudget: true);
            }
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read =
                    reader.Read(
                        chars,
                        0,
                        chars.Length);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0;
                     index < read;
                     index++)
                {
                    var value = chars[index];
                    if (value == '\r')
                    {
                        FinalizeLine();
                        previousWasCarriageReturn = true;
                        continue;
                    }
                    if (value == '\n')
                    {
                        if (previousWasCarriageReturn)
                        {
                            previousWasCarriageReturn = false;
                            continue;
                        }

                        FinalizeLine();
                        continue;
                    }

                    previousWasCarriageReturn = false;
                    lineBuffer.Append(value);
                    pendingContent = true;
                }
            }

            if (pendingContent)
            {
                FinalizeLine();
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(
                chars);
        }

        var startLine =
            lines.Count == 0
                ? 0
                : lines.Peek().LineNumber;
        var hasEarlierLines =
            startLine > 1;
        var responseLimited =
            characterLimited ||
            lines.Any(
                static line =>
                    line.Truncated) ||
            (serverLineCeilingApplied &&
             eligibleLines >
                 effectiveLineCount);

        return new TalvoraTextTailResponse(
            fullPath,
            totalLines,
            startLine,
            lines.Count,
            string.Join(
                Environment.NewLine,
                lines.Select(
                    static line =>
                        line.Text)),
            responseLimited,
            hasEarlierLines,
            hasEarlierLines
                ? startLine
                : null);
    }

    private sealed record TailLine(
        long LineNumber,
        string Text,
        bool Truncated);

    private sealed class BoundedLineSuffixBuffer
    {
        private readonly int limit;
        private readonly StringBuilder builder = new();
        private char[]? ring;
        private int next;

        public BoundedLineSuffixBuffer(
            int limit)
        {
            this.limit = limit;
        }

        public bool Truncated { get; private set; }

        public void Append(
            char value)
        {
            if (ring is null)
            {
                if (builder.Length < limit)
                {
                    builder.Append(value);
                    return;
                }

                ring = new char[limit];
                builder.CopyTo(
                    0,
                    ring,
                    0,
                    limit);
                builder.Clear();
                next = 0;
            }

            ring[next] = value;
            next =
                (next + 1) %
                limit;
            Truncated = true;
        }

        public string ToText()
        {
            if (ring is null)
            {
                return builder.ToString();
            }

            var ordered =
                new char[limit];
            var suffixLength =
                limit - next;
            Array.Copy(
                ring,
                next,
                ordered,
                0,
                suffixLength);
            if (next > 0)
            {
                Array.Copy(
                    ring,
                    0,
                    ordered,
                    suffixLength,
                    next);
            }

            return new string(ordered);
        }

        public void Reset()
        {
            builder.Clear();
            ring = null;
            next = 0;
            Truncated = false;
        }
    }
}
