using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraCodexPreferencesResponse(
    string User,
    string DefaultProfile,
    string EffectiveSandbox,
    string Description);

[McpServerToolType]
public static class CodexPreferencesTools
{
    [McpServerTool(
        Name = "talvora_codex_preferences",
        ReadOnly = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexPreferencesResponse)),
     Description("Show the Codex developer profile for the logged-on Windows user. A trusted profile lets Codex perform real development with the user's normal filesystem and network permissions.")]
    public static TalvoraCodexPreferencesResponse Get()
    {
        var context = WindowsSessionLauncher.GetActiveInteractiveUser();
        var profile = CodexPreferencesStore.Read(
            context.LocalAppData ?? throw new InvalidOperationException(
                "Interactive LOCALAPPDATA is unavailable."));
        return CreateResponse(context.User, profile);
    }

    [McpServerTool(
        Name = "talvora_codex_configure",
        Destructive = true,
        Idempotent = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexPreferencesResponse)),
     Description("Set the logged-on user's persistent Codex development profile: balanced uses workspace-write with automatic review; trusted uses unrestricted normal-user filesystem and network access for future tasks. This never grants SYSTEM or administrator privileges.")]
    public static async Task<TalvoraCodexPreferencesResponse> Configure(
        string defaultProfile,
        CancellationToken cancellationToken = default)
    {
        var context = WindowsSessionLauncher.GetActiveInteractiveUser();
        var path = context.LocalAppData ?? throw new InvalidOperationException(
            "Interactive LOCALAPPDATA is unavailable.");
        await CodexPreferencesStore.WriteAsync(
            path, defaultProfile, cancellationToken).ConfigureAwait(false);
        return CreateResponse(context.User, defaultProfile);
    }

    private static TalvoraCodexPreferencesResponse CreateResponse(
        string user,
        string profile) =>
        new(
            user,
            profile,
            profile == CodexPreferencesStore.Trusted
                ? "danger-full-access"
                : "workspace-write",
            profile == CodexPreferencesStore.Trusted
                ? "Codex runs as the logged-on Windows user with full user-level filesystem/network access."
                : "Codex runs in a writable project workspace with automatic approval review.");
}
