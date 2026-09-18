using Talvora;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "talvora_system_info", ReadOnly = true, OpenWorld = false), Description("Return operating system, process and Windows identity information for the Talvora service.")]
    public static object GetSystemInfo()
    {
        using var identity = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent() : null;
        var runtime = TalvoraRuntimeMetadata.Load();
        return new
        {
            runtime.SourceCommit,
            runtime.InstalledAtUtc,
            Environment.MachineName,
            Environment.UserName,
            OperatingSystem = RuntimeInformation.OSDescription,
            OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Environment.CurrentDirectory,
            Environment.ProcessId,
            Sid = identity?.User?.Value,
        };
    }
}