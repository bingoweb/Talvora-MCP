using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunWorkspaceAsync(
        IReadOnlyDictionary<string, McpClientTool> byName,
        string smokeId,
        string repositoryPath)
    {
        var publicDocuments = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDocuments);
        if (string.IsNullOrWhiteSpace(publicDocuments))
        {
            publicDocuments = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Talvora",
                "SmokeDocuments");
        }

        var root = Path.Combine(publicDocuments, "Talvora-Smoke-" + smokeId);
        var context = new SmokeWorkspaceContext(
            byName,
            smokeId,
            repositoryPath,
            root);

        const string knowledgeRootsEnvironmentName = "TALVORA_KNOWLEDGE_ROOTS";
        var knowledgeRootsCaptured = false;
        var knowledgeRootsWasPresent = false;
        string? knowledgeRootsOriginalValue = null;

        try
        {
            var knowledgeRootsBefore = await EnsureSuccess(
                byName["talvora_env_get"],
                new()
                {
                    ["name"] = knowledgeRootsEnvironmentName,
                    ["target"] = "process",
                });

            if (knowledgeRootsBefore.StructuredContent is not { } knowledgeRootsBeforeJson ||
                !knowledgeRootsBeforeJson.TryGetProperty(
                    "found",
                    out var knowledgeRootsFoundJson))
            {
                throw new InvalidOperationException(
                    "environment get did not return the knowledge roots state.");
            }

            knowledgeRootsWasPresent = knowledgeRootsFoundJson.GetBoolean();
            if (knowledgeRootsWasPresent)
            {
                if (!knowledgeRootsBeforeJson.TryGetProperty(
                        "value",
                        out var knowledgeRootsValueJson))
                {
                    throw new InvalidOperationException(
                        "environment get did not return the knowledge roots value.");
                }

                knowledgeRootsOriginalValue =
                    knowledgeRootsValueJson.GetString() ?? string.Empty;
            }

            knowledgeRootsCaptured = true;

            await EnsureSuccess(
                byName["talvora_env_set"],
                new()
                {
                    ["name"] = knowledgeRootsEnvironmentName,
                    ["value"] = root,
                    ["target"] = "process",
                });

            await RunWorkspaceCoreAsync(context);
            await RunWorkspaceRuntimeAsync(context);
            await RunWorkspaceHttpNetworkAsync(context);
            await RunWorkspaceWatchJobGitAsync(context);
            await RunWorkspaceConfigAssetsAsync(context);
            await RunWorkspaceCopyMoveAsync(context);
            await RunWorkspaceProcessSearchAsync(context);
        }
        finally
        {
            try
            {
                if (knowledgeRootsCaptured)
                {
                    if (knowledgeRootsWasPresent)
                    {
                        await EnsureSuccess(
                            byName["talvora_env_set"],
                            new()
                            {
                                ["name"] = knowledgeRootsEnvironmentName,
                                ["value"] =
                                    knowledgeRootsOriginalValue ?? string.Empty,
                                ["target"] = "process",
                            });
                    }
                    else
                    {
                        await EnsureSuccess(
                            byName["talvora_env_delete"],
                            new()
                            {
                                ["name"] = knowledgeRootsEnvironmentName,
                                ["target"] = "process",
                            });
                    }
                }
            }
            finally
            {
                if (Directory.Exists(context.ReparseLoop))
                {
                    Directory.Delete(context.ReparseLoop);
                }

                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
