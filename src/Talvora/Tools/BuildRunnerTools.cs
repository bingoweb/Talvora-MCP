using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraCliCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    long ElapsedMilliseconds);

public sealed record TalvoraDotnetInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    IReadOnlyList<string> Sdks,
    IReadOnlyList<string> Runtimes,
    string? Info);

public sealed record TalvoraNodeInfoResponse(
    bool NodeFound,
    string? NodeExecutable,
    string? NodeVersion,
    bool NpmFound,
    string? NpmExecutable,
    string? NpmVersion);

public sealed record TalvoraJavaInfoResponse(
    bool JavaFound,
    string? JavaExecutable,
    string? JavaVersion,
    bool JavacFound,
    string? JavacExecutable,
    string? JavacVersion,
    string? JavaHome);

public sealed record TalvoraBuildToolInfoResponse(
    string Tool,
    bool Found,
    string? Executable,
    string? Version,
    bool UsingWrapper,
    string? WrapperDistribution,
    string WorkingDirectory);

[McpServerToolType]
public static partial class BuildRunnerTools
{
}
