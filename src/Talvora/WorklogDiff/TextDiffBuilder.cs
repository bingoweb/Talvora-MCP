using DiffPlex;

namespace Talvora.WorklogDiff;

internal static class TextDiffBuilder
{
    public static (int? Added, int? Removed) Count(string before, string after)
    {
        var diff = new Differ().CreateCustomDiffs(
            before.Replace("\r\n", "\n").Replace('\r', '\n'),
            after.Replace("\r\n", "\n").Replace('\r', '\n'),
            false, static text =>
            {
                if (text.Length == 0) return [];
                if (text.EndsWith('\n')) text = text[..^1];
                return text.Split('\n');
            });
        return (diff.DiffBlocks.Sum(block => block.InsertCountB),
            diff.DiffBlocks.Sum(block => block.DeleteCountA));
    }
}
