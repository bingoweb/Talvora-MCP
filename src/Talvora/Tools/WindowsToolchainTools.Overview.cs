using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class WindowsToolchainTools
{
[McpServerTool(
        Name = "talvora_windows_toolchain_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsToolchainInfoResponse)),
     Description("Discover Visual Studio/vswhere, MSBuild, installed Windows SDKs, CMake, and Ninja on the machine. Missing components are reported structurally instead of throwing.")]
    public static async Task<TalvoraWindowsToolchainInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        var vsWhere = ResolveVsWhere();
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);

        var msBuild = ResolveMsBuild(instances);
        var sdks = DiscoverWindowsSdks();

        var cmake = CommandResolver.Resolve(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]);
        var ninja = CommandResolver.Resolve(
            ["ninja.exe", "ninja"],
            [
                @"C:\ProgramData\chocolatey\bin\ninja.exe",
            ]);

        var cmakeVersion = cmake is null
            ? null
            : await ReadFirstLineAsync(cmake, ["--version"], cancellationToken);
        var ninjaVersion = ninja is null
            ? null
            : await ReadFirstLineAsync(ninja, ["--version"], cancellationToken);

        var msbuildInfo = await MsBuildInfo(cancellationToken);
        var sdkInfo = WindowsSdkInfo();
        var cmakeInfo = await CMakeInfo(cancellationToken);
        var ninjaInfo = await NinjaInfo(cancellationToken);

        return new TalvoraWindowsToolchainInfoResponse(
            vsWhere,
            instances,
            msBuild,
            sdks,
            cmake,
            cmakeVersion,
            ninja,
            ninjaVersion,
            msbuildInfo,
            sdkInfo,
            cmakeInfo,
            ninjaInfo);
    }
}
