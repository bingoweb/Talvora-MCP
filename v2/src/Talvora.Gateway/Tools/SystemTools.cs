using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Server;

namespace Talvora.Gateway.Tools;

public sealed record SystemSnapshot(
    string MachineName,
    string UserName,
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    int ProcessorCount,
    string CurrentDirectory,
    int ProcessId);

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "talvora_system_info", ReadOnly = true, OpenWorld = false), Description("Return basic information about the Windows machine and current Talvora process context.")]
    public static SystemSnapshot GetSystemInfo() => new(
        Environment.MachineName,
        Environment.UserName,
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        Environment.CurrentDirectory,
        Environment.ProcessId);
}
