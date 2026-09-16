using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Gateway.Tools;

public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long? Length, DateTimeOffset LastWriteTimeUtc);

[McpServerToolType]
public static class FileTools
{
    [McpServerTool(Name = "talvora_read_text", ReadOnly = true, OpenWorld = true), Description("Read a UTF-8 text file from any path accessible to the Talvora process.")]
    public static Task<string> ReadText([Description("Absolute or relative file path.")] string path) =>
        File.ReadAllTextAsync(path);

    [McpServerTool(Name = "talvora_write_text", Destructive = true, Idempotent = true, OpenWorld = true), Description("Write UTF-8 text to any path accessible to the Talvora process, creating parent directories as needed.")]
    public static async Task WriteText(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(fullPath, content);
    }

    [McpServerTool(Name = "talvora_delete", Destructive = true, OpenWorld = true), Description("Delete a file or directory recursively from any path accessible to the Talvora process.")]
    public static void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            return;
        }

        throw new FileNotFoundException("Path does not exist.", fullPath);
    }

    [McpServerTool(Name = "talvora_list", ReadOnly = true, OpenWorld = true), Description("List entries in any directory accessible to the Talvora process.")]
    public static IReadOnlyList<FileEntry> List(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        return directory.EnumerateFileSystemInfos()
            .Select(entry => entry switch
            {
                FileInfo file => new FileEntry(file.Name, file.FullName, false, file.Length, file.LastWriteTimeUtc),
                DirectoryInfo dir => new FileEntry(dir.Name, dir.FullName, true, null, dir.LastWriteTimeUtc),
                _ => throw new InvalidOperationException($"Unsupported filesystem entry: {entry.FullName}")
            })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
