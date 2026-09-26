using ModelContextProtocol.Client;
using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static void RunSurfacePolicySource()
    {
        var full =
            TalvoraMcpToolSurfacePolicy.GetToolNames(
                TalvoraMcpToolSurface.Full);
        var development =
            TalvoraMcpToolSurfacePolicy.GetToolNames(
                TalvoraMcpToolSurface.Development);
        var administration =
            TalvoraMcpToolSurfacePolicy.GetToolNames(
                TalvoraMcpToolSurface.Administration);

        if (full.Count !=
            TalvoraMcpToolSurfacePolicy.ExpectedFullToolCount)
        {
            throw new InvalidOperationException(
                $"Full surface count mismatch. Actual={full.Count} Expected={TalvoraMcpToolSurfacePolicy.ExpectedFullToolCount}");
        }

        if (development.Count !=
            TalvoraMcpToolSurfacePolicy.ExpectedDevelopmentToolCount)
        {
            throw new InvalidOperationException(
                $"Development surface count mismatch. Actual={development.Count} Expected={TalvoraMcpToolSurfacePolicy.ExpectedDevelopmentToolCount}");
        }

        if (administration.Count !=
            TalvoraMcpToolSurfacePolicy.ExpectedAdministrationToolCount)
        {
            throw new InvalidOperationException(
                $"Administration surface count mismatch. Actual={administration.Count} Expected={TalvoraMcpToolSurfacePolicy.ExpectedAdministrationToolCount}");
        }

        if (!TalvoraMcpToolSurfacePolicy.IsFocusedSurfaceReviewComplete())
        {
            throw new InvalidOperationException(
                "Focused surface review is incomplete.");
        }

        foreach (var name in TalvoraToolManifest.Names)
        {
            if (!TalvoraMcpToolSurfacePolicy.Includes(
                    TalvoraMcpToolSurface.Full,
                    name))
            {
                throw new InvalidOperationException(
                    $"Full surface lost tool: {name}");
            }

            if (!TalvoraMcpToolSurfacePolicy.Includes(
                    TalvoraMcpToolSurface.Development,
                    name) &&
                !TalvoraMcpToolSurfacePolicy.Includes(
                    TalvoraMcpToolSurface.Administration,
                    name))
            {
                throw new InvalidOperationException(
                    $"Tool has no focused-surface review decision: {name}");
            }
        }

        AssertSurface(
            "talvora_apply_patch",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_dotnet_build",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_run_powershell",
            development: true,
            administration: true);
        AssertSurface(
            "talvora_service_restart",
            development: false,
            administration: true);
        AssertSurface(
            "talvora_registry_set",
            development: false,
            administration: true);
        AssertSurface(
            "talvora_json_set",
            development: false,
            administration: true);
        AssertSurface(
            "talvora_modal_info",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_modal_endpoint_list",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_modal_run",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_status",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_editor_status",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_list_toolsets",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_describe_toolset",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_search_tools",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_call_tool",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_call_tools",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_read_console",
            development: true,
            administration: false);
        AssertSurface(
            "talvora_sbox_invoke",
            development: true,
            administration: false);

        foreach (var evalCase in TalvoraToolSelectionPolicy.Cases)
        {
            if (!evalCase.ExpectTalvora)
            {
                if (evalCase.ExpectedTools.Count != 0 ||
                    evalCase.RequiredSurface is not null)
                {
                    throw new InvalidOperationException(
                        $"Negative selection eval unexpectedly routes to Talvora: {evalCase.Id}");
                }
                continue;
            }

            if (evalCase.ExpectedTools.Count == 0 ||
                evalCase.RequiredSurface is null)
            {
                throw new InvalidOperationException(
                    $"Positive selection eval is incomplete: {evalCase.Id}");
            }

            foreach (var toolName in evalCase.ExpectedTools)
            {
                if (!TalvoraToolManifest.Names.Contains(
                        toolName,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Selection eval references unknown tool: {evalCase.Id} -> {toolName}");
                }

                if (!TalvoraMcpToolSurfacePolicy.Includes(
                        evalCase.RequiredSurface.Value,
                        toolName))
                {
                    throw new InvalidOperationException(
                        $"Selection eval tool is missing from required surface: {evalCase.Id} -> {toolName}");
                }
            }
        }
    }

    internal static async Task RunSurfacePolicyLiveAsync(
        string serverRoot)
    {
        RunSurfacePolicySource();

        var root =
            serverRoot.TrimEnd('/');
        await using var fullClient =
            await CreateClientAsync(
                root + "/mcp");
        await using var developmentClient =
            await CreateClientAsync(
                root + "/mcp/dev");
        await using var administrationClient =
            await CreateClientAsync(
                root + "/mcp/admin");

        var full =
            await fullClient.ListToolsAsync();
        var development =
            await developmentClient.ListToolsAsync();
        var administration =
            await administrationClient.ListToolsAsync();

        AssertLiveSurface(
            TalvoraMcpToolSurface.Full,
            full);
        AssertLiveSurface(
            TalvoraMcpToolSurface.Development,
            development);
        AssertLiveSurface(
            TalvoraMcpToolSurface.Administration,
            administration);

        AssertSameToolMetadata(
            full,
            development,
            "talvora_run_powershell");
        AssertSameToolMetadata(
            full,
            administration,
            "talvora_run_powershell");
        AssertSameToolMetadata(
            full,
            development,
            "talvora_system_info");
        AssertSameToolMetadata(
            full,
            administration,
            "talvora_system_info");

        var blocked = false;
        try
        {
            var bypassResult =
                await developmentClient.CallToolAsync(
                    "talvora_service_list",
                    new Dictionary<string, object?>(),
                    cancellationToken: CancellationToken.None);
            blocked =
                bypassResult.IsError is true;
        }
        catch (ModelContextProtocol.McpProtocolException ex)
            when (ex.Message.Contains(
                "Unknown tool",
                StringComparison.OrdinalIgnoreCase))
        {
            blocked = true;
        }

        if (!blocked)
        {
            throw new InvalidOperationException(
                "Development surface allowed direct invocation of an excluded administration tool.");
        }
    }

    private static async Task<McpClient> CreateClientAsync(
        string endpoint)
    {
        var transport =
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint =
                        new Uri(endpoint),
                    TransportMode =
                        HttpTransportMode.StreamableHttp,
                    ConnectionTimeout =
                        TimeSpan.FromSeconds(20),
                });
        return await McpClient.CreateAsync(
            transport);
    }

    private static void AssertLiveSurface(
        TalvoraMcpToolSurface surface,
        IList<McpClientTool> tools)
    {
        var expected =
            TalvoraMcpToolSurfacePolicy.GetToolNames(
                surface);
        var actual =
            tools.Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        if (!expected.SequenceEqual(
                actual,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Live surface mismatch: {surface}. Actual={actual.Length} Expected={expected.Count}");
        }
    }

    private static void AssertSameToolMetadata(
        IList<McpClientTool> left,
        IList<McpClientTool> right,
        string name)
    {
        var first =
            left.Single(tool =>
                string.Equals(
                    tool.Name,
                    name,
                    StringComparison.Ordinal));
        var second =
            right.Single(tool =>
                string.Equals(
                    tool.Name,
                    name,
                    StringComparison.Ordinal));

        if (!string.Equals(
                first.Description,
                second.Description,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Focused surface changed tool description: {name}");
        }

        if (!string.Equals(
                first.ProtocolTool.Title,
                second.ProtocolTool.Title,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Focused surface changed tool title: {name}");
        }

        if (!string.Equals(
                first.ProtocolTool.InputSchema.GetRawText(),
                second.ProtocolTool.InputSchema.GetRawText(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Focused surface changed input schema: {name}");
        }

        var firstOutputSchema =
            first.ProtocolTool.OutputSchema;
        var secondOutputSchema =
            second.ProtocolTool.OutputSchema;
        if (firstOutputSchema.HasValue !=
            secondOutputSchema.HasValue ||
            firstOutputSchema.HasValue &&
            !string.Equals(
                firstOutputSchema.Value.GetRawText(),
                secondOutputSchema!.Value.GetRawText(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Focused surface changed output schema: {name}");
        }

        var firstAnnotations =
            first.ProtocolTool.Annotations;
        var secondAnnotations =
            second.ProtocolTool.Annotations;
        if (firstAnnotations?.ReadOnlyHint !=
                secondAnnotations?.ReadOnlyHint ||
            firstAnnotations?.DestructiveHint !=
                secondAnnotations?.DestructiveHint ||
            firstAnnotations?.IdempotentHint !=
                secondAnnotations?.IdempotentHint ||
            firstAnnotations?.OpenWorldHint !=
                secondAnnotations?.OpenWorldHint)
        {
            throw new InvalidOperationException(
                $"Focused surface changed tool annotations: {name}");
        }
    }

    private static void AssertSurface(
        string toolName,
        bool development,
        bool administration)
    {
        if (TalvoraMcpToolSurfacePolicy.Includes(
                TalvoraMcpToolSurface.Development,
                toolName) != development ||
            TalvoraMcpToolSurfacePolicy.Includes(
                TalvoraMcpToolSurface.Administration,
                toolName) != administration)
        {
            throw new InvalidOperationException(
                $"Focused surface decision mismatch: {toolName}");
        }
    }
}
