using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
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
    long ElapsedMilliseconds)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TalvoraDotnetRunSummary? Summary { get; init; }
}

public sealed record TalvoraDotnetDiagnostic(
    string File,
    int Line,
    string Code,
    string Message);

public sealed record TalvoraDotnetRunSummary(
    int Errors,
    int Warnings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TestsPassed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TestsFailed,
    IReadOnlyList<string> FailedTests,
    IReadOnlyList<TalvoraDotnetDiagnostic> Diagnostics);

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
