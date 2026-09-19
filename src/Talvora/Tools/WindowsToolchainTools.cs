using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraVisualStudioInstance(
    string InstanceId,
    string InstallationName,
    string InstallationPath,
    string InstallationVersion,
    string ProductId,
    bool IsComplete,
    bool IsLaunchable,
    string? DisplayName,
    string? Description);

public sealed record TalvoraWindowsSdkEntry(
    string Version,
    string BinPath,
    bool HasX64,
    bool HasX86,
    bool HasArm64,
    bool HasRc,
    bool HasMt,
    bool HasSigntool);

public sealed record TalvoraWindowsToolchainInfoResponse(
    string? VsWhereExecutable,
    IReadOnlyList<TalvoraVisualStudioInstance> VisualStudioInstances,
    string? MsBuildExecutable,
    IReadOnlyList<TalvoraWindowsSdkEntry> WindowsSdks,
    string? CMakeExecutable,
    string? CMakeVersion,
    string? NinjaExecutable,
    string? NinjaVersion,
    TalvoraToolInfoResponse Msbuild,
    TalvoraWindowsSdkInfoResponse WindowsSdk,
    TalvoraToolInfoResponse CMake,
    TalvoraToolInfoResponse Ninja);

public sealed record TalvoraVsDevEnvironmentResponse(
    string InstallationPath,
    string Architecture,
    string HostArchitecture,
    string DeveloperCommandScript,
    IReadOnlyDictionary<string, string> Environment);

public sealed record TalvoraVisualStudioInstancesResponse(
    int Count,
    IReadOnlyList<TalvoraVisualStudioInstance> Instances);

public sealed record TalvoraVsDevEnvironmentCompatResponse(
    string InstallationPath,
    string Architecture,
    string HostArchitecture,
    string ScriptPath,
    int Count,
    IReadOnlyDictionary<string, string> Environment);

public sealed record TalvoraToolInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    string? Invocation);

public sealed record TalvoraWindowsSdkInfoResponse(
    bool Found,
    string? KitsRoot10,
    string? LatestVersion,
    IReadOnlyList<TalvoraWindowsSdkEntry> Sdks);

public sealed record TalvoraPeInfoResponse(
    string Path,
    bool IsPe,
    long Length,
    string? Machine,
    bool IsDll,
    bool IsExe,
    bool IsManaged,
    string? Subsystem,
    int SectionCount);

public sealed record TalvoraFileVersionInfoResponse(
    string Path,
    string? FileVersion,
    string? ProductVersion,
    string? ProductName,
    string? CompanyName,
    string? FileDescription,
    string? OriginalFilename,
    string? InternalName,
    string? LegalCopyright,
    bool IsDebug,
    bool IsPatched,
    bool IsPreRelease,
    bool IsPrivateBuild,
    bool IsSpecialBuild);

[McpServerToolType]
public static partial class WindowsToolchainTools
{
}
