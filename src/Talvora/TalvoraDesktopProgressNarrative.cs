using System.Text.Json;
using ModelContextProtocol.Protocol;
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
        WorklogTitles Titles);

    private static OperationNarrative BuildNarrative(
        string? toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        var normalized = NormalizeToolName(toolName);
        if (normalized is "claude_exec" or "claude_run" or "claude_start")
        {
            return new(OperationCategory.GenericExecution, WorklogCopy.Claude);
        }

        if (normalized == "run_powershell")
        {
            var script = GetArgumentText(arguments, "script");
            if (IsCanonicalPackageBuildScript(script))
            {
                return new(OperationCategory.PackageBuild, WorklogCopy.PackageBuild);
            }
            if (script.Contains("Install.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(OperationCategory.Deployment, WorklogCopy.Deploy);
            }
            return new(OperationCategory.GenericExecution, WorklogCopy.Generic);
        }

        if (normalized is "dotnet_build" or "dotnet_test" or "dotnet_restore")
        {
            return normalized switch
            {
                "dotnet_build" => new(OperationCategory.Verification, WorklogCopy.Build),
                "dotnet_test" => new(OperationCategory.Verification, WorklogCopy.Tests),
                _ => new(OperationCategory.Verification, WorklogCopy.Restore),
            };
        }

        if (normalized is "apply_patch" or "apply_edits" or "structural_edit" or "semantic_edit")
        {
            return new(OperationCategory.SourceEdit, WorklogCopy.SourceEdit);
        }

        if (normalized == "git_run")
        {
            var gitArgs = GetArgumentArray(arguments, "arguments");
            return GitArgumentParser.GetSubcommand(gitArgs)?.ToLowerInvariant() switch
            {
                "push" => new(OperationCategory.VersionControl, WorklogCopy.Push),
                "commit" => new(OperationCategory.VersionControl, WorklogCopy.Commit),
                _ => new(OperationCategory.VersionControl, WorklogCopy.OtherGit),
            };
        }

        if (normalized.Contains("service_", StringComparison.OrdinalIgnoreCase))
        {
            return new(OperationCategory.ServiceOperation, WorklogCopy.Service);
        }
        if (normalized.Contains("choco_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("package_", StringComparison.OrdinalIgnoreCase))
        {
            return new(OperationCategory.PackageOperation, WorklogCopy.Package);
        }

        if (normalized.Contains("file_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("directory_", StringComparison.OrdinalIgnoreCase) ||
            normalized is "write_text" or "append_text" or "move" or "copy" or "delete")
        {
            return new(OperationCategory.FileMutation, WorklogCopy.FileMutation);
        }

        if (normalized.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("http_", StringComparison.OrdinalIgnoreCase))
        {
            return new(OperationCategory.NetworkOperation, WorklogCopy.Network);
        }

        if (normalized.Contains("process_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("job_", StringComparison.OrdinalIgnoreCase) ||
            normalized is "run_process" or "user_process_start")
        {
            return new(OperationCategory.ProcessOperation, WorklogCopy.Process);
        }
        return new(OperationCategory.GenericExecution, WorklogCopy.Generic);
    }

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
