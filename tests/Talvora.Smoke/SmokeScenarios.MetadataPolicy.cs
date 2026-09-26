using ModelContextProtocol.Client;
using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static void RunMetadataPolicySource()
    {
        if (TalvoraToolManifest.Names.Count !=
            TalvoraMcpToolMetadataPolicy.ExpectedReviewedToolCount)
        {
            throw new InvalidOperationException(
                $"Metadata policy review count is stale. Manifest={TalvoraToolManifest.Names.Count} " +
                $"Reviewed={TalvoraMcpToolMetadataPolicy.ExpectedReviewedToolCount}");
        }

        if (TalvoraToolManifest.Names.Distinct(StringComparer.Ordinal).Count() !=
            TalvoraToolManifest.Names.Count)
        {
            throw new InvalidOperationException(
                "Talvora tool manifest contains duplicate names.");
        }

        if (TalvoraMcpToolMetadataPolicy.OpenWorldToolCount !=
            TalvoraMcpToolMetadataPolicy.ExpectedOpenWorldToolCount)
        {
            throw new InvalidOperationException(
                $"Metadata policy open-world review count changed. Actual={TalvoraMcpToolMetadataPolicy.OpenWorldToolCount} " +
                $"Expected={TalvoraMcpToolMetadataPolicy.ExpectedOpenWorldToolCount}");
        }

        foreach (var name in TalvoraToolManifest.Names)
        {
            if (!TalvoraMcpToolMetadataPolicy.IsReviewedTool(name))
            {
                throw new InvalidOperationException(
                    $"Tool is not covered by metadata policy: {name}");
            }
        }

        AssertWorldScope("talvora_system_info", false);
        AssertWorldScope("talvora_read_source", false);
        AssertWorldScope("talvora_search_text", false);
        AssertWorldScope("talvora_git_status", false);
        AssertWorldScope("talvora_apply_patch", false);
        AssertWorldScope("talvora_http_mock_start", false);
        AssertWorldScope("talvora_http_request", true);
        AssertWorldScope("talvora_git_run", true);
        AssertWorldScope("talvora_run_powershell", true);
        AssertWorldScope("talvora_npm_install", true);
        AssertWorldScope("talvora_tcp_exchange", true);
        AssertWorldScope("talvora_sbox_status", true);
        AssertWorldScope("talvora_sbox_editor_status", true);
        AssertWorldScope("talvora_sbox_list_toolsets", true);
        AssertWorldScope("talvora_sbox_describe_toolset", true);
        AssertWorldScope("talvora_sbox_search_tools", true);
        AssertWorldScope("talvora_sbox_call_tool", true);
        AssertWorldScope("talvora_sbox_call_tools", true);
        AssertWorldScope("talvora_sbox_read_console", true);
        AssertWorldScope("talvora_sbox_invoke", true);

        const string legacyDescription =
            "Run an arbitrary command with unrestricted administration access. No command allowlist or deny-list is applied.";
        var normalized =
            TalvoraMcpToolMetadataPolicy.NormalizeDescription(
                "talvora_metadata_test_fixture",
                legacyDescription)
            ?? string.Empty;
        if (TalvoraMcpToolMetadataPolicy.ContainsLegacyRiskLanguage(
                normalized))
        {
            throw new InvalidOperationException(
                "Description normalization retained legacy implementation-risk language.");
        }

        var powershellDescription =
            TalvoraMcpToolMetadataPolicy.NormalizeDescription(
                "talvora_run_powershell",
                legacyDescription)
            ?? string.Empty;
        if (TalvoraMcpToolMetadataPolicy.ContainsLegacyRiskLanguage(
                powershellDescription) ||
            !powershellDescription.Contains(
                "talvora_apply_patch",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PowerShell host-facing description contract failed.");
        }
    }

    internal static void RunMetadataPolicyLive(
        IList<McpClientTool> tools)
    {
        RunMetadataPolicySource();

        if (tools.Count != TalvoraToolManifest.Names.Count)
        {
            throw new InvalidOperationException(
                $"Live tool count changed. Actual={tools.Count} Expected={TalvoraToolManifest.Names.Count}");
        }

        if (tools.Select(tool => tool.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != tools.Count)
        {
            throw new InvalidOperationException(
                "Live MCP tool list contains duplicate names.");
        }

        var openWorldCount = 0;
        var readOnlyCount = 0;
        var destructiveCount = 0;

        foreach (var tool in tools)
        {
            if (!TalvoraMcpToolMetadataPolicy.IsReviewedTool(
                    tool.Name))
            {
                throw new InvalidOperationException(
                    $"Live MCP exposes an unreviewed metadata tool: {tool.Name}");
            }

            var annotations =
                tool.ProtocolTool.Annotations
                ?? throw new InvalidOperationException(
                    $"Live tool has no annotations: {tool.Name}");

            if (annotations.ReadOnlyHint is null ||
                annotations.DestructiveHint is null ||
                annotations.IdempotentHint is null ||
                annotations.OpenWorldHint is null)
            {
                throw new InvalidOperationException(
                    $"Live tool has incomplete explicit annotations: {tool.Name}");
            }

            var expectedOpenWorld =
                TalvoraMcpToolMetadataPolicy.IsOpenWorld(
                    tool.Name);
            if (annotations.OpenWorldHint.Value !=
                expectedOpenWorld)
            {
                throw new InvalidOperationException(
                    $"Live open-world annotation mismatch: {tool.Name}");
            }

            if (annotations.OpenWorldHint.Value)
            {
                openWorldCount++;
            }

            if (annotations.ReadOnlyHint.Value)
            {
                readOnlyCount++;
                if (annotations.DestructiveHint.Value ||
                    !annotations.IdempotentHint.Value)
                {
                    throw new InvalidOperationException(
                        $"Read-only annotation contract mismatch: {tool.Name}");
                }
            }

            if (TalvoraMcpToolMetadataPolicy.ContainsLegacyRiskLanguage(
                    tool.Description))
            {
                throw new InvalidOperationException(
                    $"Live description contains legacy implementation-risk language: {tool.Name}");
            }

            if (tool.Description.Contains(
                    "an caller-supplied",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Live description contains grammar regression: {tool.Name}");
            }

            if (tool.Description.Length >
                TalvoraMcpToolMetadataPolicy.PreferredMaxDescriptionCharacters)
            {
                throw new InvalidOperationException(
                    $"Live description exceeds preferred bound: {tool.Name} Length={tool.Description.Length}");
            }

            if (annotations.DestructiveHint.Value)
            {
                destructiveCount++;
            }

            var normalizedAgain =
                TalvoraMcpToolMetadataPolicy.NormalizeDescription(
                    tool.Name,
                    tool.Description);
            if (!string.Equals(
                    normalizedAgain,
                    tool.Description,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Live description normalization is not idempotent: {tool.Name}");
            }
        }

        if (openWorldCount !=
            TalvoraMcpToolMetadataPolicy.ExpectedOpenWorldToolCount)
        {
            throw new InvalidOperationException(
                $"Live open-world tool count mismatch. Actual={openWorldCount} " +
                $"Expected={TalvoraMcpToolMetadataPolicy.ExpectedOpenWorldToolCount}");
        }

        if (destructiveCount !=
            TalvoraMcpToolMetadataPolicy.ExpectedDestructiveToolCount)
        {
            throw new InvalidOperationException(
                $"Live destructive tool count mismatch. Actual={destructiveCount} " +
                $"Expected={TalvoraMcpToolMetadataPolicy.ExpectedDestructiveToolCount}");
        }

        if (readOnlyCount <= 0)
        {
            throw new InvalidOperationException(
                "Live metadata policy did not expose any read-only tools.");
        }

        AssertLiveTool(
            tools,
            "talvora_system_info",
            readOnly: true,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_read_source",
            readOnly: true,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_git_status",
            readOnly: true,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_http_request",
            readOnly: false,
            destructive: true,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_run_powershell",
            readOnly: false,
            destructive: true,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_process_kill",
            readOnly: false,
            destructive: true,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_delete",
            readOnly: false,
            destructive: true,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_http_mock_reply",
            readOnly: false,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_http_mock_stop",
            readOnly: false,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_service_restart",
            readOnly: false,
            destructive: false,
            openWorld: false);
        AssertLiveTool(
            tools,
            "talvora_sbox_status",
            readOnly: true,
            destructive: false,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_sbox_editor_status",
            readOnly: true,
            destructive: false,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_sbox_read_console",
            readOnly: true,
            destructive: false,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_sbox_call_tool",
            readOnly: false,
            destructive: true,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_sbox_call_tools",
            readOnly: false,
            destructive: true,
            openWorld: true);
        AssertLiveTool(
            tools,
            "talvora_sbox_invoke",
            readOnly: false,
            destructive: true,
            openWorld: true);
    }

    private static void AssertWorldScope(
        string toolName,
        bool expectedOpenWorld)
    {
        if (TalvoraMcpToolMetadataPolicy.IsOpenWorld(
                toolName) != expectedOpenWorld)
        {
            throw new InvalidOperationException(
                $"Metadata policy world-scope mismatch: {toolName}");
        }
    }

    private static void AssertLiveTool(
        IList<McpClientTool> tools,
        string name,
        bool readOnly,
        bool destructive,
        bool openWorld)
    {
        var tool =
            tools.Single(item =>
                string.Equals(
                    item.Name,
                    name,
                    StringComparison.Ordinal));
        var annotations =
            tool.ProtocolTool.Annotations
            ?? throw new InvalidOperationException(
                $"Missing annotations for {name}");

        if (annotations.ReadOnlyHint != readOnly ||
            annotations.DestructiveHint != destructive ||
            annotations.OpenWorldHint != openWorld)
        {
            throw new InvalidOperationException(
                $"Representative metadata mismatch: {name}");
        }
    }
}
