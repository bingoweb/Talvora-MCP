using System.Text.RegularExpressions;

namespace Talvora.Shared;

/// <summary>
/// Canonical host-facing MCP metadata policy.
/// Runtime capabilities are not changed by this policy; it only describes them accurately
/// to MCP clients so tool selection, confirmations, and safety handling can use the real behavior.
/// </summary>
public static class TalvoraMcpToolMetadataPolicy
{
    public const int ExpectedReviewedToolCount = 204;
    public const int ExpectedOpenWorldToolCount = 56;

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
            "talvora_cmake_run",
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
            "talvora_msbuild_run",
            "talvora_ninja_run",
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
            "talvora_run_powershell",
            "talvora_run_process",
            "talvora_rustc_run",
            "talvora_rustup_run",
            "talvora_signtool_run",
            "talvora_tcp_exchange",
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
            "talvora_http_mock_start",
            "talvora_registry_create_key",
            "talvora_watch_start",
            "talvora_watch_stop",
        };

    private static readonly IReadOnlyDictionary<string, string> DescriptionOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talvora_run_powershell"] =
                "Execute a multiline PowerShell script under the Talvora Windows service for local development and administration. " +
                "This is a general administration tool, not the normal source editor. For development-workspace source changes, " +
                "use talvora_apply_patch; use talvora_apply_edits only when exact ranges are already available. " +
                "The script is transported with UTF-16LE Base64 encoding to preserve quoting.",
            ["talvora_run_process"] =
                "Execute a program available to the Talvora Windows service with caller-supplied arguments for local development and administration. " +
                "This is not the normal source editor. For development-workspace source changes, use talvora_apply_patch; " +
                "use talvora_apply_edits only when exact ranges are already available.",
            ["talvora_git_run"] =
                "Run Git with caller-supplied arguments in an accessible repository or working directory. " +
                "Known working-tree mutation commands in recognized development workspaces route to talvora_apply_patch by default; " +
                "explicitAdmin=true selects direct Git administration. Read/status/history/fetch/push workflows remain available. " +
                "HTTPS pushes to github.com use the logged-on Windows user session so Git Credential Manager can use the signed-in user context.",
            ["talvora_http_request"] =
                "Send an HTTP request to a requested URI for application development, integration testing, and service diagnostics. " +
                "Supports caller-supplied method, headers, text or base64 request bodies, redirect control, an optional certificate-validation override " +
                "for development diagnostics, and text/base64/none response modes. maxResponseBytes=0 requests the finite server capture maximum.",
            ["talvora_user_process_start"] =
                "Start an executable inside a logged-on Windows user's interactive session. If sessionId is omitted, Talvora selects an active logged-on session. " +
                "Supports caller-supplied arguments, working directory, and environment overrides.",
            ["talvora_job_start"] =
                "Start an executable as a long-running background development job under the Talvora Windows service. " +
                "Supports caller-supplied arguments, working directory, and environment overrides. stdout/stderr are persisted under ProgramData for incremental reading.",
            ["talvora_http_mock_start"] =
                "Start an in-process HTTP mock/webhook listener on caller-supplied HttpListener prefixes for integration testing. " +
                "Supports automatic or manual replies, caller-supplied response headers/body, and bounded capture/concurrency/pending-request resources. " +
                "A zero resource limit selects Talvora's high emergency ceiling.",
        };

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
            return descriptionOverride;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        var normalized = description
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
