using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraRuntimeCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public sealed record TalvoraPythonInfoResponse(
    bool Found,
    string? LauncherExecutable,
    string? InterpreterExecutable,
    string? Version,
    string? Prefix,
    string? BasePrefix,
    bool InVirtualEnvironment,
    bool PipAvailable,
    string? PipVersion);

public sealed record TalvoraPythonVenvResponse(
    string Path,
    string? PythonExecutable,
    TalvoraRuntimeCommandResponse Command);

public sealed record TalvoraDockerInfoResponse(
    bool Found,
    string? Executable,
    string? ClientVersion,
    bool EngineAvailable,
    string? ServerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? InfoJson,
    string? Error);

public sealed record TalvoraDockerRowsResponse(
    string Command,
    int ExitCode,
    bool TimedOut,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    string StandardOutput,
    string StandardError);

[McpServerToolType]
public static partial class RuntimeTools
{
}
