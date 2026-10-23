using System.Security.Cryptography;
using System.Text;
using Talvora.Shared;
using Talvora.WorklogDiff;

namespace Talvora;

/// <summary>
/// Bounded, in-memory before-images captured when a Codex job starts.
/// Existing uncommitted changes are part of the baseline, never attributed to Codex.
/// </summary>
internal sealed class CodexWorkspaceSnapshot
{
    private const int MaximumFiles = 2000;
    private const long MaximumBytes = 64L * 1024 * 1024;
    private const long MaximumFileBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;
    private readonly Dictionary<string, (string? Text, string Hash, long Bytes)> _before =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastShown = new(StringComparer.OrdinalIgnoreCase);
    private bool _truncated;

    private CodexWorkspaceSnapshot(string root) => _root = Path.GetFullPath(root);

    internal static CodexWorkspaceSnapshot Capture(string root)
    {
        var snapshot = new CodexWorkspaceSnapshot(root);
        var directories = new Stack<string>();
        directories.Push(snapshot._root);
        long bytes = 0;
        while (directories.Count > 0 && !snapshot._truncated)
        {
            var directory = directories.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                {
                    if (new[] { ".git", "bin", "obj", "node_modules" }.Contains(
                        Path.GetFileName(subdirectory), StringComparer.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                        directories.Push(subdirectory);
                }
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (snapshot._before.Count >= MaximumFiles) { snapshot._truncated = true; break; }
                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (info.Length > MaximumFileBytes)
                    {
                        snapshot._before[file] = (null, "", info.Length);
                        continue;
                    }
                    if (bytes + info.Length > MaximumBytes) { snapshot._truncated = true; break; }
                    var data = File.ReadAllBytes(file);
                    bytes += data.Length;
                    snapshot._before[file] = (Decode(data), Convert.ToHexString(SHA256.HashData(data)), data.Length);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                snapshot._truncated = true;
            }
        }
        return snapshot;
    }

    internal string? SafeRelativeName(string path)
    {
        try
        {
            var full = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(_root, path));
            var relative = Path.GetRelativePath(_root, full);
            if (relative is "." or ".." || Path.IsPathRooted(relative) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(x => new[] { ".git", "bin", "obj", "node_modules" }
                        .Contains(x, StringComparer.OrdinalIgnoreCase)))
                return null;
            if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return null;
            return relative.Replace('\\', '/');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal DesktopProgressEvidence? ObserveAppliedFile(string path)
    {
        var name = SafeRelativeName(path);
        if (name is null) return null;
        var full = Path.GetFullPath(Path.Combine(_root, name));
        try
        {
            if (!_before.TryGetValue(full, out var original))
            {
                if (_truncated)
                {
                    if (File.Exists(full) && new FileInfo(full).Length <= MaximumFileBytes)
                    {
                        var data = File.ReadAllBytes(full);
                        if (data.Length <= MaximumFileBytes)
                            _before[full] = (Decode(data), Convert.ToHexString(SHA256.HashData(data)), data.Length);
                    }
                    return new DesktopProgressEvidence("1 dosya değişti", [name]);
                }
                original = ("", "", 0);
            }
            var exists = File.Exists(full);
            var size = exists ? new FileInfo(full).Length : 0;
            if (size > MaximumFileBytes || original.Text is null)
                return new DesktopProgressEvidence("1 dosya değişti", [name]);
            var afterBytes = exists ? File.ReadAllBytes(full) : [];
            var after = Decode(afterBytes);
            if (after is null) return new DesktopProgressEvidence("1 dosya değişti", [name]);
            var digest = Convert.ToHexString(SHA256.HashData(afterBytes));
            if (original.Hash == digest || _lastShown.GetValueOrDefault(full) == digest) return null;
            _lastShown[full] = digest;
            var result = TextDiffBuilder.Build(original.Text, after, original.Hash.Length == 0 ? "" : name,
                exists ? name : "", original.Bytes, afterBytes.Length);
            return new DesktopProgressEvidence(result.Summary ?? "1 dosya değişti", [name],
                result.Added, result.Removed, result.Preview);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Decode(byte[] data)
    {
        try
        {
            var text = StrictUtf8.GetString(data);
            return text.Contains('\0') ? null : text;
        }
        catch (DecoderFallbackException) { return null; }
    }
}
