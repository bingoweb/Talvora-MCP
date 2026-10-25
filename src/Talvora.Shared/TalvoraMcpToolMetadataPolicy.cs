using System.Text.RegularExpressions;

namespace Talvora.Shared;

/// <summary>
/// Canonical host-facing MCP metadata policy.
/// Runtime capabilities are not changed by this policy; it only describes them accurately
/// to MCP clients so tool selection, confirmations, and safety handling can use the real behavior.
/// </summary>
public static class TalvoraMcpToolMetadataPolicy
{
    public const int ExpectedReviewedToolCount = 250;
    public const int ExpectedOpenWorldToolCount = 85;
    public const int ExpectedDestructiveToolCount = 100;
    public const int PreferredMaxDescriptionCharacters = 220;

    private static readonly HashSet<string> OpenWorldTools =
        new(StringComparer.Ordinal)
        {
            "talvora_adb_run",
            "talvora_android_cli_run",
            "talvora_bun_run",
            "talvora_cargo_run",
            "talvora_choco_install",
            "talvora_choco_run",
            "talvora_choco_search",
            "talvora_choco_upgrade",
            "talvora_dart_run",
            "talvora_dev_server_get",
            "talvora_dev_server_start",
            "talvora_dev_server_wait",
            "talvora_dns_lookup",
            "talvora_docker_compose_run",
            "talvora_docker_exec",
            "talvora_docker_run",
            "talvora_dotnet_build",
            "talvora_dotnet_publish",
            "talvora_dotnet_restore",
            "talvora_dotnet_run",
            "talvora_dotnet_test",
            "talvora_flutter_run",
            "talvora_gh_run",
            "talvora_git_run",
            "talvora_go_run",
            "talvora_gradle_run",
            "talvora_http_download",
            "talvora_http_request",
            "talvora_java_run",
            "talvora_javac_run",
            "talvora_job_start",
            "talvora_maven_run",
            "talvora_modal_app_list",
            "talvora_modal_endpoint_list",
            "talvora_modal_info",
            "talvora_modal_run",
            "talvora_stitch_find",
            "talvora_stitch_generate",
            "talvora_stitch_get",
            "talvora_stitch_info",
            "talvora_stitch_create",
            "talvora_stitch_edit",
            "talvora_stitch_delete",
            "talvora_stitch_capture",
            "talvora_stitch_upload",
            "talvora_stitch_url",
            "talvora_stitch_open",
            "talvora_stitch_serve_start",
            "talvora_stitch_agent_skills",
            "talvora_stitch_config",
            "talvora_stitch_mcp_status",
            "talvora_stitch_mcp_call_tool",
            "talvora_stitch_run",
            "talvora_stitch_schema",
            "talvora_hostinger_mcp_status",
            "talvora_hostinger_mcp_call_tool",
            "talvora_penpot_call_tool",
            "talvora_penpot_overview",
            "talvora_penpot_read_tool",
            "talvora_penpot_status",
            "talvora_msbuild_run",
            "talvora_npm_ci",
            "talvora_npm_install",
            "talvora_npm_run",
            "talvora_npm_run_script",
            "talvora_ping",
            "talvora_pip_install",
            "talvora_pip_run",
            "talvora_pnpm_run",
            "talvora_python_run",
            "talvora_python_venv_create",
            "talvora_remotion_run",
            "talvora_run_powershell",
            "talvora_run_process",
            "talvora_user_run",
            "talvora_rustc_run",
            "talvora_rustup_run",
            "talvora_signtool_run",
            "talvora_tcp_exchange",
            "talvora_toolchain_run",
            "talvora_tls_inspect",
            "talvora_user_process_start",
            "talvora_wait_tcp",
            "talvora_websocket_exchange",
            "talvora_yarn_run",
        };

    private static readonly HashSet<string> NonDestructiveMutatingTools =
        new(StringComparer.Ordinal)
        {
            "talvora_create_directory",
            "talvora_http_mock_reply",
            "talvora_http_mock_start",
            "talvora_http_mock_stop",
            "talvora_registry_create_key",
            "talvora_service_restart",
            "talvora_service_start",
            "talvora_service_stop",
            "talvora_watch_start",
            "talvora_watch_stop",
        };

    private static readonly IReadOnlyDictionary<string, string> DescriptionOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talvora_apply_patch"] =
                "Primary editor for ordinary development-workspace source, config and document changes. " +
                "Use talvora_structural_edit for syntax-wide edits or talvora_semantic_edit for symbol-aware C#.",
            ["talvora_read_source"] =
                "Read a bounded source/config text window with its SHA-256 revision and encoding details. " +
                "Use it before talvora_apply_patch or other revision-sensitive edits.",
            ["talvora_structural_edit"] =
                "Rewrite broad or repetitive source patterns using syntax structure and revision-checked transactions. " +
                "Use talvora_apply_patch for ordinary edits.",
            ["talvora_semantic_edit"] =
                "SPECIALIST Roslyn C# symbol rename from a revisioned source position. " +
                "Use talvora_apply_patch (PRIMARY/default) for ordinary edits or talvora_structural_edit for repetitive syntax changes.",
            ["talvora_run_powershell"] =
                "Execute a multiline PowerShell script under the Talvora Windows service for administration. " +
                "Use talvora_apply_patch for ordinary development-workspace source edits.",
            ["talvora_run_process"] =
                "Execute a program with caller-supplied arguments under the Talvora Windows service. " +
                "Use talvora_apply_patch for ordinary development-workspace source edits.",
            ["talvora_git_run"] =
                "Run Git with caller-supplied arguments in an accessible repository. " +
                "Use talvora_apply_patch for source edits; explicitAdmin=true selects direct Git administration.",
            ["talvora_http_request"] =
                "Send an HTTP request with caller-supplied method, headers and text or base64 body. " +
                "Use talvora_http_download when a response must be saved to a file.",
            ["talvora_user_process_start"] =
                "Start a process in the logged-on user's interactive Windows session. " +
                "Use talvora_user_run for a bounded command with captured output.",
            ["talvora_job_start"] =
                "Start a long-running development job under the Talvora Windows service. " +
                "Use talvora_user_run for bounded commands in the signed-in user context.",
            ["talvora_http_mock_start"] =
                "Start an in-process HTTP mock/webhook listener for integration testing. " +
                "Use talvora_http_mock_reply to answer captured requests manually.",
        };

    private static readonly Regex LegacyWorkspaceRoutingBlock = new(
        @"Inside recognized development workspaces,[^.]*\.(?:\s+For development-workspace source/config/text changes,[^.]*\.)?(?:\s+talvora_structural_edit[^.]*\.)?(?:\s+Do not trial-call this compatibility mutator\.)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NoAllowDenySentence = new(
        @"(?:^|(?<=\s))No [^.]*\b(?:allow-?list|allowlist|deny-?list|denylist)\b[^.]*\.\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WithNoAllowDenyClause = new(
        @"\s+with no [^.]*\b(?:allow-?list|allowlist|deny-?list|denylist)\b[^.]*\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WithoutAllowDenyClause = new(
        @"\s+without [^.]*\b(?:allow-?list|allowlist|deny-?list|denylist)\b[^.]*\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NoRestrictionSentence = new(
        @"(?:^|(?<=\s))No [^.]*\brestriction\b[^.]*\.\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RepeatedWhitespace = new(
        @"\s{2,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsReviewedTool(string toolName) =>
        TalvoraToolManifest.Names.Contains(toolName, StringComparer.Ordinal);

    public static bool IsOpenWorld(string toolName)
    {
        // Unknown tools fail conservative: preserve the safer open-world signal until review.
        return !IsReviewedTool(toolName) || OpenWorldTools.Contains(toolName);
    }

    public static bool ResolveDestructive(
        string toolName,
        bool readOnly,
        bool? declaredDestructive)
    {
        if (readOnly)
        {
            return false;
        }

        if (NonDestructiveMutatingTools.Contains(toolName))
        {
            return false;
        }

        return declaredDestructive ?? true;
    }

    public static bool ResolveIdempotent(
        bool readOnly,
        bool? declaredIdempotent) =>
        readOnly || declaredIdempotent == true;

    public static string? NormalizeDescription(
        string toolName,
        string? description)
    {
        if (DescriptionOverrides.TryGetValue(
                toolName,
                out var descriptionOverride))
        {
            description = descriptionOverride;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        var normalized = description
            .Replace(
                "an caller-supplied",
                "a caller-supplied",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "Talvora LocalSystem service",
                "Talvora Windows service",
                StringComparison.Ordinal)
            .Replace(
                "using the session's primary token",
                "using the logged-on session context",
                StringComparison.Ordinal)
            .Replace(
                "TLS certificate bypass",
                "certificate-validation override for development diagnostics",
                StringComparison.Ordinal)
            .Replace(
                "raw TCP",
                "TCP",
                StringComparison.Ordinal)
            .Replace(
                "any accessible",
                "accessible",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "any URI",
                "a URI",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "any host:port",
                "a host:port",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "arbitrary",
                "caller-supplied",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "unrestricted",
                "direct",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "escape-hatch",
                "general-purpose",
                StringComparison.OrdinalIgnoreCase);

        normalized = LegacyWorkspaceRoutingBlock.Replace(
            normalized,
            "Direct development-workspace source/config/text mutation is rejected; use talvora_apply_patch for ordinary edits and the specialist source editors for their documented cases.");
        normalized = WithNoAllowDenyClause.Replace(
            normalized,
            ".");
        normalized = WithoutAllowDenyClause.Replace(
            normalized,
            ".");
        normalized = NoAllowDenySentence.Replace(
            normalized,
            string.Empty);
        normalized = NoRestrictionSentence.Replace(
            normalized,
            string.Empty);
        normalized = RepeatedWhitespace.Replace(
            normalized,
            " ").Trim();

        if (normalized.Length > PreferredMaxDescriptionCharacters)
        {
            // Keep complete capability summaries; detailed routing already lives
            // in the MCP server instructions and the tool parameter schemas.
            var sentenceEnd = normalized.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceEnd >= 0 && sentenceEnd + 1 <= PreferredMaxDescriptionCharacters)
            {
                return normalized[..(sentenceEnd + 1)];
            }

            var limit = PreferredMaxDescriptionCharacters - 1;
            var wordEnd = normalized.LastIndexOf(' ', limit);
            return normalized[..(wordEnd > 0 ? wordEnd : limit)]
                .TrimEnd(' ', ',', ';', '.') + ".";
        }

        return normalized;
    }

    public static bool ContainsLegacyRiskLanguage(
        string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.Contains(
                   "unrestricted",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "escape-hatch",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "allowlist",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "allow-list",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "denylist",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "deny-list",
                   StringComparison.OrdinalIgnoreCase) ||
               description.Contains(
                   "arbitrary",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static int OpenWorldToolCount =>
        OpenWorldTools.Count;
}
