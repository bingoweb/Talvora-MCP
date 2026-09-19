using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraSessionInfo(
    int SessionId,
    string StationName,
    string State,
    string UserName,
    string DomainName,
    string User,
    string ClientName,
    bool IsActive);

public sealed record TalvoraSessionListResponse(
    int Count,
    IReadOnlyList<TalvoraSessionInfo> Sessions);

public sealed record TalvoraSessionGetResponse(
    bool Found,
    TalvoraSessionInfo? Session);

public sealed record TalvoraUserProcessStartResponse(
    int SessionId,
    string User,
    int ProcessId,
    int ThreadId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string CommandLine,
    bool Visible,
    bool NewConsole);

[McpServerToolType]
public static class SessionTools
{
    [McpServerTool(
        Name = "talvora_session_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSessionListResponse)),
     Description("Enumerate local Windows interactive/RDP sessions with session ID, station, state, user/domain, and client name. No user/session allowlist is applied.")]
    public static TalvoraSessionListResponse List()
    {
        var sessions = WindowsSessionLauncher.ListSessions()
            .Select(ToInfo)
            .ToArray();

        return new TalvoraSessionListResponse(
            sessions.Length,
            sessions);
    }

    [McpServerTool(
        Name = "talvora_session_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSessionGetResponse)),
     Description("Return one local Windows session by session ID. Missing session IDs return found=false.")]
    public static TalvoraSessionGetResponse Get(int sessionId)
    {
        var session = WindowsSessionLauncher.ListSessions()
            .FirstOrDefault(value => value.SessionId == sessionId);

        return new TalvoraSessionGetResponse(
            session is not null,
            session is null ? null : ToInfo(session));
    }

    [McpServerTool(
        Name = "talvora_user_process_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraUserProcessStartResponse)),
     Description("Start any executable inside a logged-on Windows user's interactive session using the session's primary token. If sessionId is omitted, Talvora selects an active session with a logged-on user. Supports arbitrary argument vectors, working directory, and environment overrides. No executable, path, user, or session allowlist is applied.")]
    public static TalvoraUserProcessStartResponse StartUserProcess(
        string executable,
        string[]? arguments = null,
        int? sessionId = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool visible = true,
        bool newConsole = false)
    {
        var result = WindowsSessionLauncher.StartProcess(
            executable,
            arguments,
            sessionId,
            workingDirectory,
            environment,
            visible,
            newConsole);

        return new TalvoraUserProcessStartResponse(
            result.SessionId,
            result.User,
            result.ProcessId,
            result.ThreadId,
            result.Executable,
            result.Arguments,
            result.WorkingDirectory,
            result.CommandLine,
            visible,
            newConsole);
    }

    private static TalvoraSessionInfo ToInfo(WindowsSessionInfo session) =>
        new(
            session.SessionId,
            session.StationName,
            session.State,
            session.UserName,
            session.DomainName,
            session.User,
            session.ClientName,
            session.IsActive);
}
