using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraWorkspaceProject(
    string Directory,
    string Ecosystem,
    string Name,
    IReadOnlyList<string> Markers,
    string? PackageManager,
    string? Toolchain,
    IReadOnlyList<string> Scripts);

public sealed record TalvoraWorkspaceInspectResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraWorkspaceProject> Projects,
    IReadOnlyList<string> Errors,
    long ProjectOffset = 0,
    long? NextProjectOffset = null);

public sealed record TalvoraWorkspaceCommand(
    string WorkingDirectory,
    string Ecosystem,
    string Purpose,
    string Executable,
    IReadOnlyList<string> Arguments,
    string Source);

public sealed record TalvoraWorkspaceCommandsResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraWorkspaceCommand> Commands,
    IReadOnlyList<string> Errors,
    long ProjectOffset = 0,
    long? NextProjectOffset = null,
    long CommandOffset = 0,
    long? NextCommandOffset = null);
