using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class WindowsToolchainTools
{
[McpServerTool(
        Name = "talvora_visual_studio_instances",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVisualStudioInstancesResponse)),
     Description("Return Visual Studio / Build Tools instances discovered by vswhere. Missing vswhere returns count=0 and an empty instance list.")]
    public static async Task<TalvoraVisualStudioInstancesResponse> VisualStudioInstancesCompat(
        CancellationToken cancellationToken = default)
    {
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        return new TalvoraVisualStudioInstancesResponse(instances.Count, instances);
    }

    [McpServerTool(
        Name = "talvora_vs_dev_environment",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVsDevEnvironmentCompatResponse)),
     Description("Capture the Visual Studio developer command-prompt environment. This compatibility surface returns scriptPath/count plus the complete environment dictionary.")]
    public static async Task<TalvoraVsDevEnvironmentCompatResponse> VsDevEnvironmentCompat(
        string? installationPath = null,
        string architecture = "x64",
        string hostArchitecture = "x64",
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await CaptureVsDevEnvironmentAsync(
            installationPath,
            architecture,
            hostArchitecture,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraVsDevEnvironmentCompatResponse(
            result.InstallationPath,
            result.Architecture,
            result.HostArchitecture,
            result.DeveloperCommandScript,
            result.Environment.Count,
            result.Environment);
    }

    private static async Task<TalvoraVsDevEnvironmentResponse> CaptureVsDevEnvironmentAsync(
        string? installationPath = null,
        string architecture = "x64",
        string hostArchitecture = "x64",
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        var selectedPath = string.IsNullOrWhiteSpace(installationPath)
            ? instances.FirstOrDefault()?.InstallationPath
            : Path.GetFullPath(installationPath);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new FileNotFoundException("No Visual Studio or Build Tools installation was found.");
        }

        var script = Path.Combine(
            selectedPath,
            "Common7",
            "Tools",
            "VsDevCmd.bat");

        if (!File.Exists(script))
        {
            throw new FileNotFoundException("VsDevCmd.bat was not found.", script);
        }

        var wrapper = Path.Combine(
            Path.GetTempPath(),
            $"talvora-vsdev-{Guid.NewGuid():N}.cmd");

        ProcessExecutionResult result;
        try
        {
            var wrapperText =
                "@echo off\r\n" +
                $"call \"{script}\" -arch=%~1 -host_arch=%~2 >nul\r\n" +
                "if errorlevel 1 exit /b %errorlevel%\r\n" +
                "set\r\n";

            await File.WriteAllTextAsync(
                wrapper,
                wrapperText,
                cancellationToken);

            result = await ProcessRunner.RunAsync(
                wrapper,
                Environment.CurrentDirectory,
                [architecture, hostArchitecture],
                timeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(wrapper);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (result.TimedOut)
        {
            throw new TimeoutException("VsDevCmd.bat environment capture timed out.");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"VsDevCmd.bat failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in TextLines.Split(result.StandardOutput))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator];
            if (IsProcessRunnerTransportVariable(name))
            {
                continue;
            }

            environment[name] = line[(separator + 1)..];
        }

        return new TalvoraVsDevEnvironmentResponse(
            selectedPath,
            architecture,
            hostArchitecture,
            script,
            environment);
    }

    private static bool IsProcessRunnerTransportVariable(string name)
    {
        const string prefix = "TALVORA_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Length <= prefix.Length + 32 + 1)
        {
            return false;
        }

        var fingerprint = name.AsSpan(prefix.Length, 32);
        foreach (var character in fingerprint)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        var suffix = name[(prefix.Length + 32)..];
        if (string.Equals(suffix, "_SCRIPT", StringComparison.Ordinal))
        {
            return true;
        }

        if (!suffix.StartsWith("_ARG_", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            suffix.AsSpan("_ARG_".Length),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
    }

    private static async Task<IReadOnlyList<TalvoraVisualStudioInstance>> DiscoverVisualStudioInstancesAsync(
        CancellationToken cancellationToken)
    {
        var vsWhere = ResolveVsWhere();
        return vsWhere is null
            ? []
            : await ReadVisualStudioInstancesAsync(vsWhere, cancellationToken);
    }

    private static string? ResolveVsWhere()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
            @"C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe",
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<IReadOnlyList<TalvoraVisualStudioInstance>> ReadVisualStudioInstancesAsync(
        string vsWhere,
        CancellationToken cancellationToken)
    {
        var result = await RunCliAsync(
            vsWhere,
            Environment.CurrentDirectory,
            [
                "-all",
                "-products",
                "*",
                "-format",
                "json",
                "-utf8",
            ],
            null,
            60,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"vswhere failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var instances = new List<TalvoraVisualStudioInstance>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            instances.Add(new TalvoraVisualStudioInstance(
                GetString(item, "instanceId") ?? string.Empty,
                GetString(item, "installationName") ?? string.Empty,
                GetString(item, "installationPath") ?? string.Empty,
                GetString(item, "installationVersion") ?? string.Empty,
                GetString(item, "productId") ?? string.Empty,
                GetBoolean(item, "isComplete"),
                GetBoolean(item, "isLaunchable"),
                GetNestedString(item, "catalog", "productDisplayVersion")
                    ?? GetNestedString(item, "properties", "displayName"),
                GetNestedString(item, "properties", "description")));
        }

        return instances;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static string? GetNestedString(
        JsonElement element,
        string objectProperty,
        string valueProperty)
    {
        if (!element.TryGetProperty(objectProperty, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nested, valueProperty);
    }
}
