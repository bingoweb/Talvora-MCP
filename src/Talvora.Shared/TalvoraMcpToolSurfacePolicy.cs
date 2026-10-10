namespace Talvora.Shared;

public enum TalvoraMcpToolSurface
{
    Full,
    Development,
    Administration,
}

/// <summary>
/// Canonical focused-surface policy. The full surface always preserves every reviewed Talvora tool.
/// Focused surfaces only change discovery/availability for a dedicated endpoint; they never remove
/// capabilities from the full /mcp endpoint.
/// </summary>
public static class TalvoraMcpToolSurfacePolicy
{
    // The memory subsystem was intentionally removed (28 Dev-only tools).
    // Keep review counts pinned to the current canonical manifest.
    public const int ExpectedFullToolCount = 234;
    public const int ExpectedDevelopmentToolCount = 197;
    public const int ExpectedAdministrationToolCount = 88;
    public const int ExpectedSharedToolCount = 51;

    private static readonly HashSet<string> DevelopmentExcludedTools =
        new(StringComparer.Ordinal)
        {
            "talvora_eventlog_list",
            "talvora_eventlog_query",
            "talvora_registry_create_key",
            "talvora_registry_delete_key",
            "talvora_registry_delete_value",
            "talvora_registry_get",
            "talvora_registry_list",
            "talvora_registry_set",
            "talvora_service_get",
            "talvora_service_list",
            "talvora_service_restart",
            "talvora_service_start",
            "talvora_service_stop",
            "talvora_session_get",
            "talvora_session_list",
            "talvora_append_text",
            "talvora_replace_text",
            "talvora_write_text",
            "talvora_dotenv_delete",
            "talvora_dotenv_set",
            "talvora_ini_delete",
            "talvora_ini_set",
            "talvora_json_delete",
            "talvora_json_set",
            "talvora_toml_delete",
            "talvora_toml_set",
            "talvora_xml_delete",
            "talvora_xml_set",
            "talvora_yaml_delete",
            "talvora_yaml_set",
            "talvora_choco_install",
            "talvora_choco_upgrade",
            "talvora_choco_uninstall",
            "talvora_choco_run",
            "talvora_env_set",
            "talvora_env_delete",
            "talvora_process_kill",
        };

    private static readonly HashSet<string> AdministrationTools =
        new(StringComparer.Ordinal)
        {
            "search",
            "fetch",
            "talvora_run_powershell",
            "talvora_device_inspect",
            "talvora_camera_modes",
            "talvora_run_process",
            "talvora_process_get",
            "talvora_process_list",
            "talvora_process_kill",
            "talvora_service_get",
            "talvora_service_list",
            "talvora_service_restart",
            "talvora_service_start",
            "talvora_service_stop",
            "talvora_registry_create_key",
            "talvora_registry_delete_key",
            "talvora_registry_delete_value",
            "talvora_registry_get",
            "talvora_registry_list",
            "talvora_registry_set",
            "talvora_env_get",
            "talvora_env_list",
            "talvora_env_set",
            "talvora_env_delete",
            "talvora_eventlog_list",
            "talvora_eventlog_query",
            "talvora_session_get",
            "talvora_session_list",
            "talvora_system_info",
            "talvora_network_interfaces",
            "talvora_tcp_connections",
            "talvora_tcp_listeners",
            "talvora_dns_lookup",
            "talvora_ping",
            "talvora_tls_inspect",
            "talvora_wait_tcp",
            "talvora_tcp_exchange",
            "talvora_user_process_start",
            "talvora_choco_info",
            "talvora_choco_list",
            "talvora_choco_search",
            "talvora_choco_install",
            "talvora_choco_uninstall",
            "talvora_choco_upgrade",
            "talvora_choco_run",
            "talvora_path_info",
            "talvora_list",
            "talvora_find_files",
            "talvora_search_text",
            "talvora_read_text",
            "talvora_read_text_range",
            "talvora_tail_text",
            "talvora_read_bytes",
            "talvora_file_hash",
            "talvora_file_version_info",
            "talvora_write_text",
            "talvora_write_bytes",
            "talvora_append_text",
            "talvora_replace_text",
            "talvora_create_directory",
            "talvora_copy",
            "talvora_move",
            "talvora_delete",
            "talvora_archive_list",
            "talvora_archive_create",
            "talvora_archive_extract",
            "talvora_json_get",
            "talvora_json_set",
            "talvora_json_delete",
            "talvora_yaml_get",
            "talvora_yaml_set",
            "talvora_yaml_delete",
            "talvora_toml_get",
            "talvora_toml_set",
            "talvora_toml_delete",
            "talvora_ini_get",
            "talvora_ini_list",
            "talvora_ini_set",
            "talvora_ini_delete",
            "talvora_xml_query",
            "talvora_xml_set",
            "talvora_xml_delete",
            "talvora_dotenv_get",
            "talvora_dotenv_list",
            "talvora_dotenv_set",
            "talvora_dotenv_delete",
            "talvora_hostinger_mcp_status",
            "talvora_hostinger_mcp_call_tool",
        };

    private static readonly HashSet<string> SharedTools =
        new(StringComparer.Ordinal)
        {
            "search",
            "fetch",
            "talvora_run_powershell",
            "talvora_device_inspect",
            "talvora_camera_modes",
            "talvora_run_process",
            "talvora_process_get",
            "talvora_process_list",
            "talvora_env_get",
            "talvora_env_list",
            "talvora_system_info",
            "talvora_network_interfaces",
            "talvora_tcp_connections",
            "talvora_tcp_listeners",
            "talvora_dns_lookup",
            "talvora_ping",
            "talvora_tls_inspect",
            "talvora_wait_tcp",
            "talvora_tcp_exchange",
            "talvora_user_process_start",
            "talvora_choco_info",
            "talvora_choco_list",
            "talvora_choco_search",
            "talvora_path_info",
            "talvora_list",
            "talvora_find_files",
            "talvora_search_text",
            "talvora_read_text",
            "talvora_read_text_range",
            "talvora_tail_text",
            "talvora_read_bytes",
            "talvora_file_hash",
            "talvora_file_version_info",
            "talvora_write_bytes",
            "talvora_create_directory",
            "talvora_copy",
            "talvora_move",
            "talvora_delete",
            "talvora_archive_list",
            "talvora_archive_create",
            "talvora_archive_extract",
            "talvora_json_get",
            "talvora_yaml_get",
            "talvora_toml_get",
            "talvora_ini_get",
            "talvora_ini_list",
            "talvora_xml_query",
            "talvora_dotenv_get",
            "talvora_dotenv_list",
            "talvora_hostinger_mcp_status",
            "talvora_hostinger_mcp_call_tool",
        };

    public static TalvoraMcpToolSurface ResolvePath(string? path) =>
        path?.TrimEnd('/').ToLowerInvariant() switch
        {
            "/mcp/dev" => TalvoraMcpToolSurface.Development,
            "/mcp/admin" => TalvoraMcpToolSurface.Administration,
            _ => TalvoraMcpToolSurface.Full,
        };

    public static bool Includes(
        TalvoraMcpToolSurface surface,
        string toolName)
    {
        if (!TalvoraToolManifest.Names.Contains(
                toolName,
                StringComparer.Ordinal))
        {
            return false;
        }

        return surface switch
        {
            TalvoraMcpToolSurface.Full => true,
            TalvoraMcpToolSurface.Development =>
                !DevelopmentExcludedTools.Contains(toolName),
            TalvoraMcpToolSurface.Administration =>
                AdministrationTools.Contains(toolName),
            _ => false,
        };
    }

    public static IReadOnlyList<string> GetToolNames(
        TalvoraMcpToolSurface surface) =>
        TalvoraToolManifest.Names
            .Where(name => Includes(surface, name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    public static bool IsShared(string toolName) =>
        TalvoraToolManifest.Names.Contains(
            toolName,
            StringComparer.Ordinal) &&
        SharedTools.Contains(toolName);

    public static IReadOnlyList<string> GetSharedToolNames() =>
        TalvoraToolManifest.Names
            .Where(IsShared)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    public static bool IsFocusedSurfaceReviewComplete()
    {
        if (TalvoraToolManifest.Names.Count !=
            ExpectedFullToolCount)
        {
            return false;
        }

        var development =
            GetToolNames(TalvoraMcpToolSurface.Development);
        var administration =
            GetToolNames(TalvoraMcpToolSurface.Administration);
        var shared = GetSharedToolNames();

        if (development.Count != ExpectedDevelopmentToolCount ||
            administration.Count != ExpectedAdministrationToolCount ||
            shared.Count != ExpectedSharedToolCount)
        {
            return false;
        }

        var actualOverlap = development
            .Intersect(administration, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (!actualOverlap.SequenceEqual(
                shared,
                StringComparer.Ordinal))
        {
            return false;
        }

        return TalvoraToolManifest.Names.All(name =>
            Includes(TalvoraMcpToolSurface.Development, name) ||
            Includes(TalvoraMcpToolSurface.Administration, name));
    }
}
