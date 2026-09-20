namespace Talvora.SourceEditing;

internal static class SourceMutationPolicy
{
    private static readonly HashSet<string> GitWorkingTreeMutationCommands =
        new(
            [
                "am",
                "apply",
                "checkout",
                "cherry-pick",
                "clean",
                "merge",
                "mv",
                "pull",
                "rebase",
                "reset",
                "restore",
                "revert",
                "rm",
                "stash",
                "switch",
            ],
            StringComparer.OrdinalIgnoreCase);

    public static void EnsureLegacyTextMutationAllowed(
        string path,
        string toolName)
    {
        var classification =
            SourceWorkspaceClassifier.Classify(path);

        if (!classification.ShouldGuardLegacyTextMutation)
        {
            return;
        }

        ThrowPolicyViolation(
            path,
            toolName,
            classification);
    }

    public static void EnsureLegacyByteMutationAllowed(
        string path,
        ReadOnlySpan<byte> payload,
        string toolName)
    {
        var classification =
            SourceWorkspaceClassifier.Classify(path);

        if (!classification.IsDevelopmentWorkspace ||
            classification.IsGeneratedLocation)
        {
            return;
        }

        var textLike =
            classification.IsKnownSourcePath ||
            classification.IsLikelyText ||
            SourceTextCodec.IsLikelyTextPayload(payload);

        if (!textLike)
        {
            return;
        }

        ThrowPolicyViolation(
            path,
            toolName,
            classification);
    }

    public static void EnsureLegacySourceFileDeleteAllowed(
        string path,
        string toolName)
    {
        var classification =
            SourceWorkspaceClassifier.Classify(path);

        if (!classification.ShouldGuardLegacyTextMutation)
        {
            return;
        }

        ThrowPolicyViolation(
            path,
            toolName,
            classification);
    }

    public static void EnsureLegacySameWorkspaceSourceMoveAllowed(
        string source,
        string destination,
        string toolName)
    {
        var sourceClassification =
            SourceWorkspaceClassifier.Classify(source);
        if (!sourceClassification.ShouldGuardLegacyTextMutation ||
            string.IsNullOrWhiteSpace(
                sourceClassification.WorkspaceRoot))
        {
            return;
        }

        var destinationClassification =
            SourceWorkspaceClassifier.Classify(destination);
        if (!destinationClassification.IsDevelopmentWorkspace ||
            !string.Equals(
                sourceClassification.WorkspaceRoot,
                destinationClassification.WorkspaceRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ThrowPolicyViolation(
            source,
            toolName,
            sourceClassification);
    }

    public static void EnsureGenericDestinationMutationAllowed(
        string destination,
        string toolName,
        bool explicitAdmin)
    {
        if (explicitAdmin)
        {
            return;
        }

        var classification =
            SourceWorkspaceClassifier.Classify(destination);
        if (!classification.ShouldGuardLegacyTextMutation)
        {
            return;
        }

        ThrowPolicyViolation(
            destination,
            toolName,
            classification);
    }

    public static void EnsureGenericFileMoveAllowed(
        string source,
        string destination,
        string toolName,
        bool explicitAdmin)
    {
        if (explicitAdmin)
        {
            return;
        }

        var sourceClassification =
            SourceWorkspaceClassifier.Classify(source);
        if (sourceClassification.ShouldGuardLegacyTextMutation)
        {
            ThrowPolicyViolation(
                source,
                toolName,
                sourceClassification);
        }

        var destinationClassification =
            SourceWorkspaceClassifier.Classify(destination);
        if (destinationClassification.ShouldGuardLegacyTextMutation)
        {
            ThrowPolicyViolation(
                destination,
                toolName,
                destinationClassification);
        }
    }

    public static void EnsureGitWorkingTreeMutationAllowed(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        string toolName,
        bool explicitAdmin)
    {
        if (explicitAdmin)
        {
            return;
        }

        var command =
            FindGitSubcommand(arguments);
        if (command is null ||
            !GitWorkingTreeMutationCommands.Contains(command))
        {
            return;
        }

        var policyProbe =
            Path.Combine(
                Path.GetFullPath(repositoryPath),
                ".talvora-source-policy.cs");
        var classification =
            SourceWorkspaceClassifier.Classify(policyProbe);
        if (!classification.IsDevelopmentWorkspace)
        {
            return;
        }

        ThrowPolicyViolation(
            repositoryPath,
            toolName,
            classification);
    }

    private static string? FindGitSubcommand(
        IReadOnlyList<string> arguments)
    {
        var skipNext = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument =
                arguments[index];
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (argument is "-C" or "-c" or
                "--git-dir" or "--work-tree" or
                "--namespace" or "--config-env" or
                "--exec-path")
            {
                skipNext = true;
                continue;
            }

            if (argument.StartsWith(
                    "-",
                    StringComparison.Ordinal))
            {
                continue;
            }

            return argument;
        }

        return null;
    }

    private static void ThrowPolicyViolation(
        string path,
        string toolName,
        SourcePathClassification classification)
    {
        throw new SourceEditDomainException(
            SourceEditCodes.PolicyViolation,
            $"{SourceEditCodes.PolicyViolation}: '{toolName}' is not the canonical source-edit route for this development-workspace source mutation. Use talvora_apply_patch as the PRIMARY/default editor for ordinary source, config, repository-document, ordinary C# changes, and source-file lifecycle changes; use talvora_structural_edit only for broad repetitive AST-shaped transformations; use talvora_semantic_edit only when a C# operation genuinely requires Roslyn symbol/semantic identity; use talvora_apply_edits only when exact zero-based UTF-16 ranges/revisions are already known or generated. If tool choice is unclear, call talvora_source_edit_guide instead of trial-calling mutators. General process/PowerShell capability remains unrestricted; generic mutators retain full capability when their explicitAdmin option is deliberately enabled for administration.",
            Path.GetFullPath(path),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tool"] = toolName,
                ["classification"] =
                    classification.Classification,
                ["workspaceRoot"] =
                    classification.WorkspaceRoot ??
                    string.Empty,
            });
    }
}
