using System.Text;
using DiffPlex;
using Talvora.Shared;

namespace Talvora.WorklogDiff;

internal sealed record TextDiffPreview(int? Added, int? Removed, string? Preview, string? Summary);

internal static class TextDiffBuilder
{
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static string[] Lines(string text)
    {
        if (text.Length == 0) return [];
        if (text.EndsWith('\n')) text = text[..^1];
        return text.Split('\n');
    }

    public static (int? Added, int? Removed) Count(string before, string after)
    {
        var diff = new Differ().CreateCustomDiffs(Normalize(before), Normalize(after), false, Lines);
        return (diff.DiffBlocks.Sum(block => block.InsertCountB),
            diff.DiffBlocks.Sum(block => block.DeleteCountA));
    }

    // DiffPlex owns matching; this method only formats its committed line edits.
    public static TextDiffPreview Build(string before, string after, string oldPath, string newPath,
        long? beforeBytes = null, long? afterBytes = null, bool encodingChanged = false, bool binary = false)
    {
        if (binary)
            return new(null, null, null, $"ikili dosya değişti · {afterBytes ?? beforeBytes ?? 0}");
        var oldText = Normalize(before);
        var newText = Normalize(after);
        var diff = new Differ().CreateCustomDiffs(oldText, newText, false, Lines);
        var added = diff.DiffBlocks.Sum(block => block.InsertCountB);
        var removed = diff.DiffBlocks.Sum(block => block.DeleteCountA);
        if ((beforeBytes ?? Encoding.UTF8.GetByteCount(before)) > 1024 * 1024 ||
            (afterBytes ?? Encoding.UTF8.GetByteCount(after)) > 1024 * 1024 ||
            diff.PiecesOld.Count > 20000 || diff.PiecesNew.Count > 20000)
            return new(added, removed, null, $"Dosya çok büyük; değişiklik özeti: +{added} −{removed}");
        if (before != after && oldText == newText && oldPath == newPath)
            return new(added, removed, null, "Yalnız satır sonları değişti");
        if (before == after && encodingChanged && oldPath == newPath)
            return new(added, removed, null, "Yalnız kodlama değişti");
        if (added == 0 && removed == 0 && oldPath == newPath)
            return new(added, removed, null, before == after ? null : "Yalnız satır sonları değişti");

        var output = new StringBuilder();
        output.AppendLine("@@ FILE " + FileLog.RedactSensitiveData(newPath.Length == 0 ? oldPath : newPath));
        output.AppendLine("--- " + (oldPath.Length == 0 ? "/dev/null" : "a/" + FileLog.RedactSensitiveData(oldPath)));
        output.AppendLine("+++ " + (newPath.Length == 0 ? "/dev/null" : "b/" + FileLog.RedactSensitiveData(newPath)));
        var shown = 0;
        var blocks = diff.DiffBlocks;
        for (var first = 0; first < blocks.Count && shown < 400;)
        {
            var last = first;
            var endOld = blocks[last].DeleteStartA + blocks[last].DeleteCountA;
            while (last + 1 < blocks.Count && blocks[last + 1].DeleteStartA - endOld <= 6)
            {
                last++;
                endOld = blocks[last].DeleteStartA + blocks[last].DeleteCountA;
            }
            var startOld = Math.Max(0, blocks[first].DeleteStartA - 3);
            var startNew = Math.Max(0, blocks[first].InsertStartB - 3);
            endOld = Math.Min(diff.PiecesOld.Count, endOld + 3);
            var endNew = Math.Min(diff.PiecesNew.Count,
                blocks[last].InsertStartB + blocks[last].InsertCountB + 3);
            string? declaration = null;
            // Nearest declaration above the first changed line (the hunk's own context lines count).
            for (var line = blocks[first].DeleteStartA - 1; line >= 0; line--)
            {
                if (DeclarationLine.PlaceName(diff.PiecesOld[line]) is null) continue;
                declaration = diff.PiecesOld[line].Trim();
                if (declaration.Length > 80) declaration = declaration[..80];
                break;
            }
            output.AppendLine($"@@ -{(endOld == startOld ? startOld : startOld + 1)},{endOld - startOld} +{(endNew == startNew ? startNew : startNew + 1)},{endNew - startNew} @@" +
                (declaration is null ? "" : " " + RedactLine(declaration)));
            var cursor = startOld;
            for (var b = first; b <= last && shown < 400; b++)
            {
                var block = blocks[b];
                while (cursor < block.DeleteStartA)
                    output.AppendLine(" " + RedactLine(diff.PiecesOld[cursor++]));
                for (var n = 0; n < block.DeleteCountA && shown < 400; n++, shown++)
                    output.AppendLine("-" + RedactLine(diff.PiecesOld[block.DeleteStartA + n]));
                for (var n = 0; n < block.InsertCountB && shown < 400; n++, shown++)
                    output.AppendLine("+" + RedactLine(diff.PiecesNew[block.InsertStartB + n]));
                cursor = block.DeleteStartA + block.DeleteCountA;
            }
            if (shown < 400)
                while (cursor < endOld) output.AppendLine(" " + RedactLine(diff.PiecesOld[cursor++]));
            first = last + 1;
        }
        if (added + removed > shown)
            output.AppendLine($"… {added + removed - shown} satır daha · tamamı Çalışma günlüğünde");
        var preview = output.ToString().TrimEnd();
        const int limit = 512 * 1024 - 128;
        if (preview.Length > limit)
            preview = preview[..limit] + "\n… (gösterim sınırı aşıldı; kalan kod bu kartta gösterilemiyor)";
        return new(added, removed, preview, null);
    }

    internal static string RedactLine(string line)
    {
        var lower = line.ToLowerInvariant();
        if (new[] { "password", "secret", "api_key", "apikey", "authorization", "bearer ",
            "private_key", "access_token", "auth_token", "client_secret", "token =", "token:", "\"token\"" }
            .Any(lower.Contains))
            return "[gizli değer gösterilmedi]";
        return FileLog.RedactSensitiveData(line);
    }
}
