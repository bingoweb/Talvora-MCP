using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Abstractions;
using Talvora.Modules.FileSystem;

namespace Talvora.Adapter.Mcp;

[McpServerToolType]
public sealed class FileSystemTools(
    IFileSystemService fileSystem,
    IOperationExecutor executor)
{
    [McpServerTool, Description("Reads a text file from any path available to the Windows account running Talvora.")]
    public async Task<ToolEnvelope<string>> ReadFile(
        [Description("Absolute or relative file path.")] string path,
        CancellationToken cancellationToken)
    {
        var result = await executor.ExecuteAsync(
            "filesystem.read",
            token => fileSystem.ReadTextAsync(path, token),
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool, Description("Writes text to a file using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> WriteFile(
        [Description("Absolute or relative file path.")] string path,
        [Description("Text to write.")] string content,
        [Description("Create missing parent directories when true.")] bool createParentDirectory = false,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "filesystem.write",
            async token =>
            {
                await fileSystem.WriteTextAsync(path, content, createParentDirectory, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }

    [McpServerTool, Description("Deletes a file using the Windows account running Talvora.")]
    public async Task<ToolEnvelope<bool>> DeleteFile(
        [Description("Absolute or relative file path.")] string path,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.ExecuteAsync(
            "filesystem.delete",
            async token =>
            {
                await fileSystem.DeleteFileAsync(path, token);
                return true;
            },
            cancellationToken);

        return ToolEnvelope.From(result);
    }
}
