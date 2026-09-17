using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraPathEntry(string Path, string Name, bool IsDirectory, long? Length, DateTime LastWriteTimeUtc);

[McpServerToolType]
public static class FileTools
{
    [McpServerTool(Name = "talvora_read_text", ReadOnly = true, OpenWorld = true), Description("Read a UTF-8 text file from any path accessible to the Talvora service.")]
    public static Task<string> ReadText(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(Path.GetFullPath(path), cancellationToken);

    [McpServerTool(Name = "talvora_write_text", Destructive = true, Idempotent = true, OpenWorld = true), Description("Write UTF-8 text to any path accessible to the Talvora service, creating parent directories when needed.")]
    public static async Task<object> WriteText(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return new { path = fullPath, length = new FileInfo(fullPath).Length };
    }

    [McpServerTool(Name = "talvora_delete", Destructive = true, OpenWorld = true), Description("Delete a file or directory tree from any path accessible to the Talvora service.")]
    public static object Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return new { path = fullPath, deleted = true, kind = "file" };
        }
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            return new { path = fullPath, deleted = true, kind = "directory" };
        }
        return new { path = fullPath, deleted = false, kind = "missing" };
    }

    [McpServerTool(Name = "talvora_list", ReadOnly = true, OpenWorld = true), Description("List files and directories at a path accessible to the Talvora service.")]
    public static IReadOnlyList<TalvoraPathEntry> List(string path, bool recursive = false)
    {
        var fullPath = Path.GetFullPath(path);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFileSystemEntries(fullPath, "*", option)
            .Select(item =>
            {
                var isDirectory = Directory.Exists(item);
                var info = isDirectory ? null : new FileInfo(item);
                return new TalvoraPathEntry(
                    item,
                    Path.GetFileName(item),
                    isDirectory,
                    info?.Length,
                    isDirectory ? Directory.GetLastWriteTimeUtc(item) : info!.LastWriteTimeUtc);
            })
            .ToArray();
    }
}
