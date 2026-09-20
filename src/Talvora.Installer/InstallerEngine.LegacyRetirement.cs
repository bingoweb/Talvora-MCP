using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Talvora.Shared;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const string LegacyPlaywrightTaskName =
        "Talvora Playwright MCP";
    private const string LegacyPlaywrightRetirementMarkerName =
        "playwright-mcp-retired-v1.json";

    private sealed record LegacyPlaywrightTaskOwnership(
        bool Exists,
        bool Owned);

    private static async Task RetireLegacyPlaywrightMcpAsync(
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        var talvoraUserRoot = Path.Combine(
            installUser.LocalAppData,
            "Talvora");
        var legacyRoot = Path.Combine(
            talvoraUserRoot,
            "PlaywrightMCP");
        var launcherPath = Path.Combine(
            legacyRoot,
            "Start-PlaywrightMcp.ps1");
        var markerPath = Path.Combine(
            talvoraUserRoot,
            "Migrations",
            LegacyPlaywrightRetirementMarkerName);

        if (File.Exists(markerPath))
        {
            return;
        }

        var task = await QueryLegacyPlaywrightTaskOwnershipAsync(
            legacyRoot,
            launcherPath,
            cancellationToken);

        if (task.Owned)
        {
            _ = await RunLegacyScheduledTaskAsync(
                allowNonZero: true,
                cancellationToken,
                "/End",
                "/TN",
                LegacyPlaywrightTaskName);
        }
        else if (task.Exists)
        {
            InstallerLog.Write(
                "Scheduled task named 'Talvora Playwright MCP' was preserved because legacy Talvora ownership could not be proven.");
        }

        if (task.Owned || Directory.Exists(legacyRoot))
        {
            await StopLegacyPlaywrightProcessesAsync(
                legacyRoot,
                cancellationToken);
        }

        if (task.Owned)
        {
            var delete = await RunLegacyScheduledTaskAsync(
                allowNonZero: true,
                cancellationToken,
                "/Delete",
                "/TN",
                LegacyPlaywrightTaskName,
                "/F");

            if (delete.ExitCode != 0 &&
                File.Exists(GetLegacyPlaywrightTaskFilePath()))
            {
                throw new InvalidOperationException(
                    "Owned legacy Playwright scheduled task could not be deleted.");
            }
        }

        if (Directory.Exists(legacyRoot))
        {
            await TryDeleteDirectoryAsync(
                legacyRoot,
                cancellationToken);
        }

        var markerDirectory = Path.GetDirectoryName(markerPath)
            ?? throw new InvalidOperationException(
                "Legacy Playwright retirement marker directory could not be resolved.");
        Directory.CreateDirectory(markerDirectory);

        var marker = new
        {
            SchemaVersion = 1,
            Migration = "retired-playwright-mcp-v1",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            LegacyRoot = legacyRoot,
            TaskName = LegacyPlaywrightTaskName,
            TaskWasPresent = task.Exists,
            TaskWasOwned = task.Owned,
            TaskPreservedBecauseUnowned =
                task.Exists && !task.Owned,
            LocalTunnelStateRemoved = true,
        };

        _ = await AtomicFile.WriteAllTextAsync(
            markerPath,
            JsonSerializer.Serialize(
                marker,
                IndentedJsonOptions),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            createBackup: false,
            cancellationToken);

        InstallerLog.Write(
            $"Legacy Playwright MCP retirement completed. Root={legacyRoot}; TaskOwned={task.Owned}");
    }

    private static async Task<LegacyPlaywrightTaskOwnership>
        QueryLegacyPlaywrightTaskOwnershipAsync(
            string legacyRoot,
            string launcherPath,
            CancellationToken cancellationToken)
    {
        var result = await RunLegacyScheduledTaskAsync(
            allowNonZero: true,
            cancellationToken,
            "/Query",
            "/TN",
            LegacyPlaywrightTaskName,
            "/XML");

        if (result.ExitCode != 0)
        {
            if (File.Exists(GetLegacyPlaywrightTaskFilePath()))
            {
                throw new InvalidOperationException(
                    "Legacy Playwright scheduled task exists but its configuration could not be queried.");
            }

            return new LegacyPlaywrightTaskOwnership(
                Exists: false,
                Owned: false);
        }

        return new LegacyPlaywrightTaskOwnership(
            Exists: true,
            Owned: IsOwnedLegacyPlaywrightTaskXml(
                result.StandardOutput,
                legacyRoot,
                launcherPath));
    }

    private static bool IsOwnedLegacyPlaywrightTaskXml(
        string taskXml,
        string legacyRoot,
        string launcherPath)
    {
        try
        {
            var document = XDocument.Parse(taskXml);
            var uri = document
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "URI",
                        StringComparison.Ordinal))
                ?.Value
                .Trim();
            var arguments = document
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "Arguments",
                        StringComparison.Ordinal))
                ?.Value;
            var workingDirectory = document
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "WorkingDirectory",
                        StringComparison.Ordinal))
                ?.Value;

            return string.Equals(
                       uri,
                       @"\Talvora Playwright MCP",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(arguments) &&
                   arguments.Contains(
                       launcherPath,
                       StringComparison.OrdinalIgnoreCase) &&
                   PathsEqualForLegacyOwnership(
                       workingDirectory,
                       legacyRoot);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool PathsEqualForLegacyOwnership(
        string? left,
        string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(
                        Environment.ExpandEnvironmentVariables(
                            left.Trim().Trim('"')))),
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            IOException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static async Task StopLegacyPlaywrightProcessesAsync(
        string legacyRoot,
        CancellationToken cancellationToken)
    {
        var launcherPath = Path.Combine(
            legacyRoot,
            "Start-PlaywrightMcp.ps1");
        var runtimeRoot = Path.Combine(
            legacyRoot,
            "runtime");
        var profilePath = Path.Combine(
            legacyRoot,
            "profile");

        static string QuotePowerShellLiteral(string value) =>
            value.Replace(
                "'",
                "''",
                StringComparison.Ordinal);

        var launcherMarker = QuotePowerShellLiteral(launcherPath);
        var runtimeMarker = QuotePowerShellLiteral(runtimeRoot);
        var profileMarker = QuotePowerShellLiteral(profilePath);

        var script = $$"""
$ErrorActionPreference = 'Stop'
$markers = @(
    '{{launcherMarker}}',
    '{{runtimeMarker}}',
    '{{profileMarker}}'
)
$self = $PID
$deadline = (Get-Date).AddSeconds(12)

do {
    $all = @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue
    )
    $ownedIds = @(
        $all |
            Where-Object {
                $_.ProcessId -ne $self -and
                $_.CommandLine
            } |
            Where-Object {
                $commandLine = $_.CommandLine
                @($markers | Where-Object {
                    $commandLine.IndexOf(
                        $_,
                        [StringComparison]::OrdinalIgnoreCase) -ge 0
                }).Count -gt 0
            } |
            Select-Object -ExpandProperty ProcessId
    )

    $frontier = @($ownedIds)
    while ($frontier.Count -gt 0) {
        $children = @(
            $all |
                Where-Object {
                    $frontier -contains $_.ParentProcessId -and
                    $ownedIds -notcontains $_.ProcessId
                } |
                Select-Object -ExpandProperty ProcessId
        )
        if ($children.Count -eq 0) {
            break
        }

        $ownedIds += $children
        $frontier = $children
    }

    if ($ownedIds.Count -eq 0) {
        exit 0
    }

    $ownedIds |
        Sort-Object -Descending |
        ForEach-Object {
            Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)

exit 4
""";

        var powerShell = GetLegacyRetirementPowerShellPath();
        var result = await ProcessRunner.RunAsync(
            powerShell,
            Environment.GetFolderPath(
                Environment.SpecialFolder.System),
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-WindowStyle",
                "Hidden",
                "-Command",
                script,
            ],
            timeoutSeconds: 20,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 ||
            result.TimedOut)
        {
            throw new InvalidOperationException(
                "Owned legacy Playwright MCP process tree could not be stopped deterministically.");
        }
    }

    private static string GetLegacyRetirementPowerShellPath()
    {
        var pwsh = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");
        if (File.Exists(pwsh))
        {
            return pwsh;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            throw new FileNotFoundException(
                "PowerShell could not be resolved for legacy Playwright retirement.",
                windowsPowerShell);
        }

        return windowsPowerShell;
    }

    private static string GetLegacyPlaywrightTaskFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows),
            "System32",
            "Tasks",
            LegacyPlaywrightTaskName);

    private static async Task<ProcessExecutionResult>
        RunLegacyScheduledTaskAsync(
            bool allowNonZero,
            CancellationToken cancellationToken,
            params string[] arguments)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.System),
            "schtasks.exe");
        var result = await ProcessRunner.RunAsync(
            executable,
            Environment.GetFolderPath(
                Environment.SpecialFolder.System),
            arguments,
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);

        if (!allowNonZero &&
            (result.ExitCode != 0 || result.TimedOut))
        {
            throw new InvalidOperationException(
                $"Legacy Playwright scheduled-task operation failed ({string.Join(' ', arguments)}): " +
                Collapse(
                    result.StandardError,
                    result.StandardOutput));
        }

        return result;
    }
}
