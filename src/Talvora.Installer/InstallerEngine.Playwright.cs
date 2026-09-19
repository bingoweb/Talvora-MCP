using System.Diagnostics;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const string PlaywrightTaskName = "Talvora Playwright MCP";
    private const string PlaywrightEndpoint = "http://127.0.0.1:8932/mcp";

    private sealed record PlaywrightInstallSnapshot(
        byte[]? LauncherBytes,
        byte[]? SupervisorBytes,
        string? TaskXml,
        bool TaskWasRunning);

    private sealed record NodeRuntimeResolution(
        string NodePath,
        string NpmPath,
        string NodeVersion);

    private static async Task InstallPlaywrightManagedMcpAsync(
        string payloadRoot,
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        var launcherSource = Path.Combine(
            payloadRoot,
            "Start-PlaywrightMcp.ps1");
        var supervisorSource = Path.Combine(
            payloadRoot,
            "supervisor.mjs");

        if (!File.Exists(launcherSource) ||
            !File.Exists(supervisorSource))
        {
            throw new InvalidDataException(
                "Playwright MCP installer payload is incomplete.");
        }

        var root = Path.Combine(
            installUser.LocalAppData,
            "Talvora",
            "PlaywrightMCP");
        var runtimeRoot = Path.Combine(root, "runtime");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(runtimeRoot);

        var launcherPath = Path.Combine(
            root,
            "Start-PlaywrightMcp.ps1");
        var supervisorPath = Path.Combine(
            runtimeRoot,
            "supervisor.mjs");

        var powerShell = ResolvePlaywrightPowerShell();
        var taskUser = ResolveTaskUser(installUser.Sid);
        var taskXmlPath = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Playwright-" +
            Guid.NewGuid().ToString("N") +
            ".xml");
        var rollbackTaskXmlPath = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Playwright-Rollback-" +
            Guid.NewGuid().ToString("N") +
            ".xml");

        var snapshot = await CapturePlaywrightInstallSnapshotAsync(
            launcherPath,
            supervisorPath,
            powerShell,
            cancellationToken);

        var mutationStarted = false;
        try
        {
            mutationStarted = true;
            _ = await RunSchtasksAsync(
                allowNonZero: true,
                cancellationToken,
                "/End",
                "/TN",
                PlaywrightTaskName);

            await StopPlaywrightRuntimeProcessesAsync(
                root,
                powerShell,
                cancellationToken);

            _ = await EnsureLatestNodeCurrentAsync(
                installUser,
                cancellationToken);

            await CopyFileAtomicallyAsync(
                launcherSource,
                launcherPath,
                cancellationToken);
            await CopyFileAtomicallyAsync(
                supervisorSource,
                supervisorPath,
                cancellationToken);

            var xml = BuildPlaywrightTaskXml(
                installUser.Sid,
                taskUser,
                powerShell,
                launcherPath,
                root);

            await File.WriteAllTextAsync(
                taskXmlPath,
                xml,
                Encoding.Unicode,
                cancellationToken);

            await RunSchtasksAsync(
                allowNonZero: false,
                cancellationToken,
                "/Create",
                "/TN",
                PlaywrightTaskName,
                "/XML",
                taskXmlPath,
                "/F");

            await RunSchtasksAsync(
                allowNonZero: false,
                cancellationToken,
                "/Run",
                "/TN",
                PlaywrightTaskName);

            await WaitForPlaywrightMcpReadinessAsync(
                cancellationToken);

            InstallerLog.Write(
                "Playwright MCP task/assets committed after MCP initialize, capability tools and isolated browser smoke passed.");
        }
        catch
        {
            if (mutationStarted)
            {
                try
                {
                    await RestorePlaywrightInstallSnapshotAsync(
                        snapshot,
                        launcherPath,
                        supervisorPath,
                        root,
                        powerShell,
                        rollbackTaskXmlPath,
                        CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    InstallerLog.Write(
                        "Playwright MCP transactional rollback failed",
                        rollbackError);
                }
            }

            throw;
        }
        finally
        {
            DeleteTemporaryFile(taskXmlPath);
            DeleteTemporaryFile(rollbackTaskXmlPath);
        }
    }

    private static async Task<PlaywrightInstallSnapshot>
        CapturePlaywrightInstallSnapshotAsync(
            string launcherPath,
            string supervisorPath,
            string powerShell,
            CancellationToken cancellationToken)
    {
        var launcherBytes = File.Exists(launcherPath)
            ? await File.ReadAllBytesAsync(
                launcherPath,
                cancellationToken)
            : null;
        var supervisorBytes = File.Exists(supervisorPath)
            ? await File.ReadAllBytesAsync(
                supervisorPath,
                cancellationToken)
            : null;

        var query = await RunSchtasksAsync(
            allowNonZero: true,
            cancellationToken,
            "/Query",
            "/TN",
            PlaywrightTaskName,
            "/XML");

        var taskXml = query.ExitCode == 0 &&
                      query.StandardOutput.Contains(
                          "<Task",
                          StringComparison.OrdinalIgnoreCase)
            ? query.StandardOutput
            : null;

        var taskWasRunning = false;
        if (!string.IsNullOrWhiteSpace(taskXml))
        {
            var stateResult = await ProcessRunner.RunAsync(
                powerShell,
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$t=Get-ScheduledTask -TaskName 'Talvora Playwright MCP' -ErrorAction SilentlyContinue; if($null -eq $t){exit 3}; [Console]::Write($t.State.ToString())",
                ],
                timeoutSeconds: 20,
                cancellationToken: cancellationToken);

            taskWasRunning =
                stateResult.ExitCode == 0 &&
                string.Equals(
                    stateResult.StandardOutput.Trim(),
                    "Running",
                    StringComparison.OrdinalIgnoreCase);
        }

        return new PlaywrightInstallSnapshot(
            launcherBytes,
            supervisorBytes,
            taskXml,
            taskWasRunning);
    }

    private static async Task RestorePlaywrightInstallSnapshotAsync(
        PlaywrightInstallSnapshot snapshot,
        string launcherPath,
        string supervisorPath,
        string root,
        string powerShell,
        string rollbackTaskXmlPath,
        CancellationToken cancellationToken)
    {
        _ = await RunSchtasksAsync(
            allowNonZero: true,
            cancellationToken,
            "/End",
            "/TN",
            PlaywrightTaskName);

        try
        {
            await StopPlaywrightRuntimeProcessesAsync(
                root,
                powerShell,
                cancellationToken);
        }
        catch (Exception ex)
        {
            InstallerLog.Write(
                "Playwright rollback runtime cleanup was incomplete",
                ex);
        }

        await RestoreFileSnapshotAsync(
            launcherPath,
            snapshot.LauncherBytes,
            cancellationToken);
        await RestoreFileSnapshotAsync(
            supervisorPath,
            snapshot.SupervisorBytes,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(snapshot.TaskXml))
        {
            await File.WriteAllTextAsync(
                rollbackTaskXmlPath,
                snapshot.TaskXml,
                Encoding.Unicode,
                cancellationToken);

            await RunSchtasksAsync(
                allowNonZero: false,
                cancellationToken,
                "/Create",
                "/TN",
                PlaywrightTaskName,
                "/XML",
                rollbackTaskXmlPath,
                "/F");

            if (snapshot.TaskWasRunning &&
                snapshot.LauncherBytes is not null &&
                snapshot.SupervisorBytes is not null)
            {
                _ = await RunSchtasksAsync(
                    allowNonZero: true,
                    cancellationToken,
                    "/Run",
                    "/TN",
                    PlaywrightTaskName);
            }
        }
        else
        {
            _ = await RunSchtasksAsync(
                allowNonZero: true,
                cancellationToken,
                "/Delete",
                "/TN",
                PlaywrightTaskName,
                "/F");
        }

        InstallerLog.Write(
            "Playwright MCP task/assets restored to the pre-install snapshot.");
    }

    private static async Task CopyFileAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            source,
            cancellationToken);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                $"Destination directory could not be resolved: {destination}");
        Directory.CreateDirectory(directory);

        var temp = destination + "." +
                   Guid.NewGuid().ToString("N") +
                   ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temp,
                bytes,
                cancellationToken);
            File.Move(
                temp,
                destination,
                overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temp);
        }
    }

    private static async Task RestoreFileSnapshotAsync(
        string destination,
        byte[]? bytes,
        CancellationToken cancellationToken)
    {
        if (bytes is null)
        {
            File.Delete(destination);
            return;
        }

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                $"Destination directory could not be resolved: {destination}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            destination,
            bytes,
            cancellationToken);
    }

    private static async Task<NodeRuntimeResolution>
        EnsureLatestNodeCurrentAsync(
            InstallUserContext installUser,
            CancellationToken cancellationToken)
    {
        const string ChocolateySource =
            "https://community.chocolatey.org/api/v2/";

        var chocolatey = ResolveExecutable(
            "choco.exe",
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "chocolatey",
                "bin",
                "choco.exe"))
            ?? throw new FileNotFoundException(
                "Chocolatey bulunamadı; Playwright MCP Node Current kurulumu için Chocolatey gereklidir.");

        var latestQuery = await ProcessRunner.RunAsync(
            chocolatey,
            Path.GetDirectoryName(chocolatey),
            [
                "search",
                "nodejs",
                "--exact",
                "--limit-output",
                "--source",
                ChocolateySource,
            ],
            timeoutSeconds: 90,
            cancellationToken: cancellationToken);

        if (latestQuery.ExitCode != 0 ||
            latestQuery.TimedOut)
        {
            throw new InvalidOperationException(
                "Chocolatey nodejs latest sürümü çözümlenemedi: " +
                Collapse(
                    latestQuery.StandardError,
                    latestQuery.StandardOutput));
        }

        var latestPackageVersion = latestQuery.StandardOutput
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 2))
            .Where(parts =>
                parts.Length == 2 &&
                string.Equals(
                    parts[0],
                    "nodejs",
                    StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latestPackageVersion) ||
            !Version.TryParse(
                latestPackageVersion,
                out var latestVersion))
        {
            throw new InvalidOperationException(
                "Chocolatey nodejs latest sürüm yanıtı geçersiz.");
        }

        InstallerLog.Write(
            $"Chocolatey latest Node Current resolved. Version={latestPackageVersion}");

        var upgrade = await ProcessRunner.RunAsync(
            chocolatey,
            Path.GetDirectoryName(chocolatey),
            [
                "upgrade",
                "nodejs",
                "-y",
                "--no-progress",
                "--limit-output",
                "--source",
                ChocolateySource,
            ],
            timeoutSeconds: 600,
            cancellationToken: cancellationToken);

        if (upgrade.ExitCode != 0 &&
            upgrade.ExitCode is not 1641 and not 3010)
        {
            throw new InvalidOperationException(
                "Chocolatey latest Node Current kurulumu başarısız oldu: " +
                Collapse(
                    upgrade.StandardError,
                    upgrade.StandardOutput));
        }

        var node = ResolveExecutable(
            "node.exe",
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "nodejs",
                "node.exe"));
        var npm = ResolveExecutable(
            "npm.cmd",
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "nodejs",
                "npm.cmd"));

        if (node is null || npm is null)
        {
            throw new FileNotFoundException(
                "Chocolatey Node Current kurulumu sonrasında node.exe/npm.cmd çözümlenemedi.");
        }

        var nodeVersionResult = await ProcessRunner.RunAsync(
            node,
            Path.GetDirectoryName(node),
            ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var normalizedNodeVersion =
            nodeVersionResult.StandardOutput
                .Trim()
                .TrimStart('v', 'V');

        if (nodeVersionResult.ExitCode != 0 ||
            !Version.TryParse(
                normalizedNodeVersion,
                out var installedNodeVersion) ||
            installedNodeVersion < latestVersion)
        {
            throw new InvalidOperationException(
                $"Node Current latest doğrulaması başarısız. Beklenen>={latestPackageVersion}; Gerçek={nodeVersionResult.StandardOutput.Trim()}");
        }

        var npmUpgrade = await RunInstallUserProcessAsync(
            installUser,
            npm,
            Path.GetDirectoryName(npm)!,
            [
                "install",
                "--global",
                "npm@latest",
                "--no-fund",
                "--no-audit",
            ],
            timeoutSeconds: 300,
            cancellationToken: cancellationToken);

        if (npmUpgrade.ExitCode != 0 ||
            npmUpgrade.TimedOut)
        {
            throw new InvalidOperationException(
                "npm@latest kurulumu başarısız oldu: " +
                Collapse(
                    npmUpgrade.StandardError,
                    npmUpgrade.StandardOutput));
        }

        var npmLatestQuery = await RunInstallUserProcessAsync(
            installUser,
            npm,
            Path.GetDirectoryName(npm)!,
            ["view", "npm", "version"],
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);
        var npmVersionResult = await RunInstallUserProcessAsync(
            installUser,
            npm,
            Path.GetDirectoryName(npm)!,
            ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);
        var npmPrefixResult = await RunInstallUserProcessAsync(
            installUser,
            npm,
            Path.GetDirectoryName(npm)!,
            ["prefix", "-g"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var expectedNpm = npmLatestQuery.StandardOutput.Trim();
        var actualNpm = npmVersionResult.StandardOutput.Trim();
        var actualNpmPrefix = npmPrefixResult.StandardOutput.Trim();
        var expectedNpmPrefix = Path.Combine(
            installUser.RoamingAppData,
            "npm");
        if (npmLatestQuery.ExitCode != 0 ||
            npmVersionResult.ExitCode != 0 ||
            npmPrefixResult.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(expectedNpm) ||
            !string.Equals(
                expectedNpm,
                actualNpm,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFullPath(expectedNpmPrefix),
                Path.GetFullPath(actualNpmPrefix),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"npm latest doğrulaması başarısız. Beklenen={expectedNpm}; Gerçek={actualNpm}; Prefix={actualNpmPrefix}; BeklenenPrefix={expectedNpmPrefix}");
        }

        var ltsInstalled = await ProcessRunner.RunAsync(
            chocolatey,
            Path.GetDirectoryName(chocolatey),
            [
                "list",
                "--local-only",
                "--exact",
                "nodejs-lts",
                "--limit-output",
            ],
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);

        if (ltsInstalled.ExitCode == 0 &&
            ltsInstalled.StandardOutput
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.StartsWith(
                    "nodejs-lts|",
                    StringComparison.OrdinalIgnoreCase)))
        {
            var removeLtsRegistration =
                await ProcessRunner.RunAsync(
                    chocolatey,
                    Path.GetDirectoryName(chocolatey),
                    [
                        "uninstall",
                        "nodejs-lts",
                        "-y",
                        "--skip-autouninstaller",
                        "--no-progress",
                        "--limit-output",
                    ],
                    timeoutSeconds: 120,
                    cancellationToken: cancellationToken);

            if (removeLtsRegistration.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Eski nodejs-lts Chocolatey paket kaydı güvenli biçimde temizlenemedi: " +
                    Collapse(
                        removeLtsRegistration.StandardError,
                        removeLtsRegistration.StandardOutput));
            }
        }

        InstallerLog.Write(
            $"Playwright Node/npm runtime latest verified. Node=v{installedNodeVersion}; npm={actualNpm}; npmPrefix={actualNpmPrefix}; Path={node}");

        return new NodeRuntimeResolution(
            node,
            npm,
            "v" + installedNodeVersion);
    }

    private static Task<ProcessExecutionResult>
        RunInstallUserProcessAsync(
            InstallUserContext installUser,
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            int timeoutSeconds,
            CancellationToken cancellationToken)
    {
        if (installUser.UseInteractiveSession)
        {
            return InteractiveUserProcessRunner.RunAsync(
                executable,
                workingDirectory,
                arguments,
                timeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken);
        }

        return ProcessRunner.RunAsync(
            executable,
            workingDirectory,
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);
    }

    private static string? ResolveExecutable(
        string commandName,
        params string[] preferredPaths)
    {
        foreach (var preferred in preferredPaths)
        {
            if (!string.IsNullOrWhiteSpace(preferred) &&
                File.Exists(preferred))
            {
                return Path.GetFullPath(preferred);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(
                        directory.Trim('"'),
                        commandName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (Exception ex) when (
                    ex is ArgumentException or
                    NotSupportedException)
                {
                }
            }
        }

        return null;
    }

    private static async Task WaitForPlaywrightMcpReadinessAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var transport = new HttpClientTransport(
                    new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(PlaywrightEndpoint),
                        TransportMode = HttpTransportMode.StreamableHttp,
                        ConnectionTimeout = TimeSpan.FromSeconds(8),
                    });

                await using var client = await McpClient.CreateAsync(
                    transport,
                    cancellationToken: cancellationToken);

                var tools = await client.ListToolsAsync(
                    cancellationToken: cancellationToken);
                var names = tools
                    .Select(tool => tool.Name)
                    .ToHashSet(StringComparer.Ordinal);

                var required = new[]
                {
                    "browser_tabs",
                    "browser_navigate",
                    "browser_snapshot",
                    "browser_run_code_unsafe",
                    "browser_file_upload",
                    "browser_take_screenshot",
                    "browser_pdf_save",
                    "browser_network_requests",
                    "browser_start_tracing",
                    "browser_stop_tracing",
                };

                var missing = required
                    .Where(name => !names.Contains(name))
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        "Playwright MCP capability tools eksik: " +
                        string.Join(", ", missing));
                }

                var code =
                    "async (page) => { " +
                    "const healthPage = await page.context().newPage(); " +
                    "try { " +
                    "await healthPage.goto('data:text/html,<h1>Talvora Installer Health</h1>'); " +
                    "const snapshot = await healthPage.locator('body').ariaSnapshot(); " +
                    "return { snapshot }; " +
                    "} finally { await healthPage.close(); } " +
                    "}";

                var smoke = await client.CallToolAsync(
                    "browser_run_code_unsafe",
                    new Dictionary<string, object?>
                    {
                        ["code"] = code,
                    },
                    cancellationToken: cancellationToken);

                if (smoke.IsError is true)
                {
                    throw new InvalidOperationException(
                        "Playwright installer browser smoke failed.");
                }

                var smokeText = string.Join(
                    Environment.NewLine,
                    smoke.Content
                        .OfType<TextContentBlock>()
                        .Select(block => block.Text));

                if (!smokeText.Contains(
                        "Talvora Installer Health",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Playwright installer browser smoke expected ARIA content was missing.");
                }

                return;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or
                InvalidOperationException or
                OperationCanceledException)
            {
                lastError = ex;
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
        }

        throw new TimeoutException(
            "Playwright MCP task started but did not pass MCP/browser readiness within 120 seconds.",
            lastError);
    }

    private static async Task StopPlaywrightRuntimeProcessesAsync(
        string root,
        string powerShell,
        CancellationToken cancellationToken)
    {
        var marker = root.Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
$ErrorActionPreference = 'Stop'
$marker = '{{marker}}'
$self = $PID
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProcessId -ne $self -and
        $_.CommandLine -and
        $_.CommandLine.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0
    } |
    Sort-Object ProcessId -Descending |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

$deadline = (Get-Date).AddSeconds(12)
do {
    $listeners = @(
        Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $_.LocalPort -in @(8931, 8932) }
    )
    if ($listeners.Count -eq 0) {
        exit 0
    }

    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)

exit 4
""";

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
                "Playwright MCP önceki process ağacı deterministik olarak kapatılamadı.");
        }
    }

    private static string ResolvePlaywrightPowerShell()
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
                "PowerShell could not be resolved for Playwright MCP startup.");
        }

        return windowsPowerShell;
    }

    private static string ResolveTaskUser(string sid)
    {
        try
        {
            var securityIdentifier =
                new SecurityIdentifier(sid);
            return securityIdentifier
                .Translate(typeof(NTAccount))
                .Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid;
        }
    }

    private static string BuildPlaywrightTaskXml(
        string sid,
        string taskUser,
        string powerShell,
        string launcherPath,
        string workingDirectory)
    {
        static string Escape(string value) =>
            System.Security.SecurityElement.Escape(value)
            ?? throw new InvalidOperationException(
                "Scheduled-task XML escaping failed.");

        var arguments =
            $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{launcherPath}\"";

        return $$"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Talvora managed Playwright MCP server</Description>
    <URI>\Talvora Playwright MCP</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{{Escape(taskUser)}}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{{Escape(sid)}}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
    <RestartOnFailure>
      <Interval>PT1M</Interval>
      <Count>3</Count>
    </RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{{Escape(powerShell)}}</Command>
      <Arguments>{{Escape(arguments)}}</Arguments>
      <WorkingDirectory>{{Escape(workingDirectory)}}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
""";
    }

    private static async Task<ProcessExecutionResult> RunSchtasksAsync(
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
                $"Playwright scheduled-task operation failed ({string.Join(' ', arguments)}): " +
                Collapse(
                    result.StandardError,
                    result.StandardOutput));
        }

        return result;
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            InstallerLog.Write(
                $"Temporary installer file cleanup skipped. Path={path}",
                ex);
        }
    }
}
