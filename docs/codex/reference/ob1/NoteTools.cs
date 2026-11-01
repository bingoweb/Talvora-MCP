using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraNoteWriteResponse(string Path, string Mode, int Characters);

/// <summary>OB1: writes Markdown notes into the user's Obsidian vault; the repository stays the only source of project docs.</summary>
[McpServerToolType]
public static class NoteTools
{
    internal const int MaximumNoteCharacters = 64_000;
    internal const string LinkedDocsFolderName = "Repo belgeleri";

    // Turkish password lines ("şifre: …", "parola = …") that the shared English redaction patterns do not cover.
    private static readonly Regex TurkishSecretLineRegex = new(
        @"\b(?:şifre|parola)\s*[:=]\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [McpServerTool(
        Name = "talvora_note_write",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraNoteWriteResponse)),
     Description("Create or append a Markdown note in the user's Obsidian vault. Refuses secret-looking text, paths outside the vault, hidden folders and the linked \"Repo belgeleri\" folders (project docs change only in the repository, through cards). Read notes with talvora_read_source or talvora_search_text.")]
    public static async Task<TalvoraNoteWriteResponse> WriteAsync(
        [Description("Absolute vault root that contains a .obsidian folder, e.g. C:\\Users\\tayla\\Obsidian\\Taylan.")] string vaultRoot,
        [Description("Vault-relative .md path, e.g. \"40 Oturum Notları/2026-11-01 Talvora W1.md\".")] string path,
        [Description("Markdown text, at most 64000 characters.")] string content,
        [Description("create (the note must not exist) or append (the note must exist).")] string mode = "create",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot) || !Path.IsPathFullyQualified(vaultRoot))
        {
            throw new ArgumentException("Kasa yolu tam bir yol olmalı.", nameof(vaultRoot));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultRoot));
        if (!Directory.Exists(Path.Combine(root, ".obsidian")))
        {
            throw new ArgumentException("Bu klasör bir Obsidian kasası değil (.obsidian klasörü yok).", nameof(vaultRoot));
        }

        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new ArgumentException("Not yolu kasaya göre göreli olmalı.", nameof(path));
        }

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.StartsWith('.')))
        {
            throw new ArgumentException("Gizli klasörlere ve üst klasörlere not yazılmaz.", nameof(path));
        }

        if (segments.Any(segment => string.Equals(segment, LinkedDocsFolderName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Repo belgeleri yalnızca repoda, kartla değişir.", nameof(path));
        }

        if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Yalnızca .md notları yazılır.", nameof(path));
        }

        var fullPath = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Not yolu kasanın dışına çıkıyor.", nameof(path));
        }

        RejectReparsePoints(root, fullPath);

        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumNoteCharacters)
        {
            throw new ArgumentException($"Not en çok {MaximumNoteCharacters} karakter olabilir.", nameof(content));
        }

        if (ContainsSecret(content))
        {
            throw new ArgumentException(
                "Not gizli bilgiye benzeyen bir şey içeriyor (anahtar, belirteç ya da parola); yazılmadı. Gizli bilgi yerine yalnızca bulunduğu yeri yazın.",
                nameof(content));
        }

        // The vault runs the Templater plugin; "<%" would let a written note execute code when Obsidian opens it.
        if (content.Contains("<%", StringComparison.Ordinal))
        {
            throw new ArgumentException("Not şablon kodu (<% … %>) içeriyor; kasaya kod yazılmaz.", nameof(content));
        }

        string text;
        switch (mode)
        {
            case "create":
                if (File.Exists(fullPath))
                {
                    throw new InvalidOperationException("Bu not zaten var; sonuna eklemek için mode=append kullanın.");
                }

                text = content;
                break;
            case "append":
                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException("Not bulunamadı; yeni not için mode=create kullanın.");
                }

                var existing = await File.ReadAllTextAsync(fullPath, cancellationToken);
                text = existing.Length == 0 || existing.EndsWith('\n')
                    ? existing + content
                    : existing + "\n" + content;
                break;
            default:
                throw new ArgumentException("mode create ya da append olmalı.", nameof(mode));
        }

        await AtomicFile.WriteAllTextAsync(
            fullPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken);

        return new TalvoraNoteWriteResponse(Path.GetRelativePath(root, fullPath), mode, content.Length);
    }

    internal static bool ContainsSecret(string content) =>
        FileLog.ContainsSensitiveData(content) ||
        content.Contains("PRIVATE KEY-----", StringComparison.Ordinal) ||
        TurkishSecretLineRegex.IsMatch(content);

    // A junction or symbolic link inside the vault (the linked repository docs) is never written through.
    private static void RejectReparsePoints(string root, string fullPath)
    {
        var current = root;
        var relativeDirectory = Path.GetRelativePath(root, Path.GetDirectoryName(fullPath)!);
        if (relativeDirectory != ".")
        {
            foreach (var segment in relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (Directory.Exists(current) &&
                    new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ArgumentException("Bağlantı klasörlerine (repo belgeleri) not yazılmaz.", "path");
                }
            }
        }

        if (File.Exists(fullPath) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ArgumentException("Bağlantı dosyalarına not yazılmaz.", "path");
        }
    }
}
