using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora.Shared;
using Talvora.Tools;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private enum OperationCategory
    {
        SourceEdit,
        Verification,
        PackageBuild,
        Deployment,
        VersionControl,
        ServiceOperation,
        PackageOperation,
        FileMutation,
        NetworkOperation,
        ProcessOperation,
        Inspection,
        GenericExecution,
    }

    private sealed record OperationNarrative(
        OperationCategory Category,
        WorklogTitles Titles,
        string? Subject = null);

    /// <summary>
    /// Category, tense-correct titles and the step's subject (DESIGN-WORKLOG §6.1). Every title names
    /// what it works on when the arguments say so; the tool's own Turkish title replaces "İşlem yürütülüyor".
    /// </summary>
    private static OperationNarrative BuildNarrative(
        string? toolName,
        IDictionary<string, JsonElement>? arguments,
        DesktopProgressEvidence? initialEvidence = null)
    {
        var normalized = NormalizeToolName(toolName);
        var toolTitle = TalvoraToolTitles.Get(toolName ?? string.Empty);
        if (normalized is "claude_exec" or "claude_run" or "claude_start")
        {
            return new(OperationCategory.GenericExecution, WorklogCopy.Claude);
        }

        if (normalized == "run_powershell")
        {
            var script = GetArgumentText(arguments, "script");
            if (WorklogSubject.ScriptFileName(script) is { } scriptName)
            {
                return FromDescription(WorklogSubject.ForScript(scriptName, script));
            }
            if (IsCanonicalPackageBuildScript(script))
            {
                return new(OperationCategory.PackageBuild, WorklogCopy.PackageBuild);
            }
            return FromToolTitle(toolTitle);
        }

        if (normalized is "user_run" or "run_process" or "job_start" or "user_process_start")
        {
            var executable = GetArgumentText(arguments, "executable");
            return string.IsNullOrWhiteSpace(executable)
                ? FromToolTitle(toolTitle)
                : FromDescription(WorklogSubject.ForExecutable(
                    executable, WorklogSubject.Strings(arguments, "arguments")));
        }

        if (normalized is "dotnet_build" or "dotnet_test" or "dotnet_restore" or "dotnet_publish")
        {
            var target = WorklogSubject.FileName(
                WorklogSubject.Text(arguments, "target") ?? WorklogSubject.Text(arguments, "project"));
            var titles = normalized switch
            {
                "dotnet_build" => WorklogCopy.Build,
                "dotnet_test" => WorklogCopy.Tests,
                "dotnet_publish" => WorklogCopy.Publish,
                _ => WorklogCopy.Restore,
            };
            return new(OperationCategory.Verification, WorklogSubject.WithSubject(titles, target), target);
        }

        if (normalized is "apply_patch" or "apply_edits" or "structural_edit" or "semantic_edit")
        {
            var files = initialEvidence?.Files is { Count: > 0 } changed
                ? WorklogSubject.Shorten(DescribeFactFiles(changed))
                : null;
            return new(OperationCategory.SourceEdit, WorklogSubject.WithSubject(WorklogCopy.SourceEdit, files), files);
        }

        if (normalized == "git_run")
        {
            var gitArgs = GetArgumentArray(arguments, "arguments");
            var subcommand = GitArgumentParser.GetSubcommand(gitArgs)?.ToLowerInvariant();
            var index = subcommand is null ? -1 : gitArgs.ToList().FindIndex(
                argument => string.Equals(argument, subcommand, StringComparison.OrdinalIgnoreCase));
            var operands = index < 0 ? [] : gitArgs.Skip(index + 1).Where(argument => !argument.StartsWith('-')).ToArray();
            var target = operands.Length >= 2 ? $"{operands[0]}/{operands[1]}" : operands.FirstOrDefault();
            return subcommand switch
            {
                "push" => new(OperationCategory.VersionControl, WorklogSubject.WithSubject(WorklogCopy.Push, target), target),
                "commit" => new(OperationCategory.VersionControl, WorklogCopy.Commit),
                "pull" or "fetch" => new(OperationCategory.VersionControl,
                    WorklogSubject.WithSubject(WorklogCopy.OtherGit, $"{subcommand} {target}".Trim()), target),
                null => new(OperationCategory.VersionControl, WorklogCopy.OtherGit),
                _ => new(OperationCategory.VersionControl, WorklogSubject.WithSubject(WorklogCopy.OtherGit, subcommand), subcommand),
            };
        }

        if (normalized.Contains("service_", StringComparison.OrdinalIgnoreCase))
        {
            var service = WorklogSubject.Shorten(WorklogSubject.Text(arguments, "name") ?? WorklogSubject.Text(arguments, "serviceName"));
            return new(OperationCategory.ServiceOperation, WorklogSubject.WithSubject(WorklogCopy.Service, service), service);
        }
        if (normalized.Contains("choco_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("package_", StringComparison.OrdinalIgnoreCase))
        {
            var package = WorklogSubject.Shorten(WorklogSubject.Text(arguments, "packageName") ??
                WorklogSubject.Text(arguments, "package") ?? WorklogSubject.Text(arguments, "name"));
            return new(OperationCategory.PackageOperation, WorklogSubject.WithSubject(WorklogCopy.Package, package), package);
        }

        if (normalized.Contains("file_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("directory_", StringComparison.OrdinalIgnoreCase) ||
            normalized is "write_text" or "append_text" or "move" or "copy" or "delete")
        {
            var file = WorklogSubject.FileName(WorklogSubject.Text(arguments, "path") ??
                WorklogSubject.Text(arguments, "source") ?? WorklogSubject.Text(arguments, "destination"));
            return new(OperationCategory.FileMutation, WorklogSubject.WithSubject(WorklogCopy.FileMutation, file), file);
        }

        if (normalized.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("http_", StringComparison.OrdinalIgnoreCase))
        {
            var host = WorklogSubject.Host(WorklogSubject.Text(arguments, "url") ?? WorklogSubject.Text(arguments, "uri"));
            return new(OperationCategory.NetworkOperation, WorklogSubject.WithSubject(WorklogCopy.Network, host), host);
        }

        if (normalized.Contains("process_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("job_", StringComparison.OrdinalIgnoreCase))
        {
            return new(OperationCategory.ProcessOperation, WorklogCopy.Process);
        }
        return FromToolTitle(toolTitle);
    }

    private static OperationNarrative FromDescription(WorklogSubject.Description description) =>
        new(description.Kind switch
            {
                WorklogSubject.OperationKind.Build or WorklogSubject.OperationKind.Tests or
                    WorklogSubject.OperationKind.Restore or WorklogSubject.OperationKind.Publish =>
                    OperationCategory.Verification,
                WorklogSubject.OperationKind.Git => OperationCategory.VersionControl,
                WorklogSubject.OperationKind.PackageBuild => OperationCategory.PackageBuild,
                WorklogSubject.OperationKind.Deploy => OperationCategory.Deployment,
                _ => OperationCategory.ProcessOperation,
            },
            description.Titles,
            description.Subject);

    /// <summary>Unknown steps say what the tool is (its approved Turkish MCP title), never "İşlem yürütülüyor".</summary>
    private static OperationNarrative FromToolTitle(string? toolTitle) =>
        string.IsNullOrWhiteSpace(toolTitle)
            ? new(OperationCategory.GenericExecution, WorklogCopy.Generic)
            : new(OperationCategory.GenericExecution, WorklogSubject.ForTool(toolTitle.Trim()));

    private static bool TryBuildDeferredCompletion<T>(
        OperationNarrative narrative,
        T result,
        out string title,
        out string message)
    {
        if (narrative.Category != OperationCategory.Deployment ||
            !ResultContainsText(result, "TALVORA_UPDATE_DETACHED"))
        {
            title = string.Empty;
            message = string.Empty;
            return false;
        }

        title = WorklogCopy.Deploy.Succeeded;
        message =
            "Güncellemeyi bağımsız kurucuya aktardım. " +
            "Talvora servisi güvenli biçimde yeniden başlatılıyor.\n\n" +
            "Bu mesaj yeni sürümün hazır olduğu anlamına gelmiyor. " +
            "Yeni çalışan sürüm ancak ayrı sağlık doğrulaması başarılı olduğunda hazır sayılacak.";
        return true;
    }

    private static bool ResultContainsText<T>(
        T result,
        string expected)
    {
        try
        {
            JsonElement json;
            if (result is CallToolResult callToolResult &&
                callToolResult.StructuredContent is JsonElement structured)
            {
                json = structured;
            }
            else
            {
                json = JsonSerializer.SerializeToElement(result);
            }

            return TryGetPropertyIgnoreCase(
                       json,
                       "standardOutput",
                       out var output) &&
                   output.ValueKind == JsonValueKind.String &&
                   (output.GetString() ?? string.Empty).Contains(
                       expected,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string GetArgumentText(
        IDictionary<string, JsonElement>? arguments,
        string key)
    {
        if (arguments is null ||
            !arguments.TryGetValue(key, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static IReadOnlyList<string> GetArgumentArray(
        IDictionary<string, JsonElement>? arguments,
        string key)
    {
        if (arguments is null ||
            !arguments.TryGetValue(key, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(static item => item.ToString())
            .ToArray();
    }
}
