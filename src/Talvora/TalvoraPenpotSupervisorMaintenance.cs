using System.Globalization;
using System.IO;
using System.Text;
using Talvora.Shared;

namespace Talvora;

internal static class TalvoraPenpotSupervisorMaintenance
{
    private const int MaximumLegacyLogCandidates = 20_000;
    private const int RetainedLegacyLogs = 16;
    private static readonly TimeSpan LegacyLogRetention =
        TimeSpan.FromDays(1);
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    private const string CanonicalSupervisorScript =
        """
        $ErrorActionPreference = 'Stop'

        $penpotRoot = Join-Path $env:ProgramData 'Talvora\Penpot'
        $root = Join-Path $penpotRoot 'penpot-2.18.0\mcp'
        $node = Join-Path $env:ProgramFiles 'nodejs\node.exe'
        $serverScript = Join-Path $root 'packages\server\dist\index.js'
        $viteScript = Join-Path $root 'packages\plugin\node_modules\vite\bin\vite.js'
        $pluginRoot = Join-Path $root 'packages\plugin'
        $viteConfig = Join-Path $pluginRoot 'vite.config.ts'
        $logDir = Join-Path $penpotRoot 'logs'

        New-Item -ItemType Directory -Force -Path $logDir | Out-Null

        $env:PENPOT_MCP_SERVER_HOST = '127.0.0.1'
        $env:PENPOT_MCP_PLUGIN_SERVER_HOST = '127.0.0.1'
        $env:WS_URI = 'http://127.0.0.1:4402'

        function Stop-Tree([System.Diagnostics.Process]$Process) {
            if ($null -eq $Process -or $Process.HasExited) { return }
            & "$env:SystemRoot\System32\taskkill.exe" /PID $Process.Id /T /F 2>$null | Out-Null
        }

        function Rotate-Log([string]$Path, [long]$MaxBytes = 8388608) {
            if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
            $item = Get-Item -LiteralPath $Path -Force
            if ($item.Length -lt $MaxBytes) { return }
            Move-Item -LiteralPath $Path -Destination ($Path + '.1') -Force
        }

        function Write-SupervisorError([string]$Message) {
            $path = Join-Path $logDir 'supervisor-error.log'
            Rotate-Log -Path $path -MaxBytes 1048576
            Add-Content -LiteralPath $path -Encoding utf8 -Value ("{0:o} {1}" -f (Get-Date), $Message)
        }

        $serverOut = Join-Path $logDir 'local-mcp.out.log'
        $serverErr = Join-Path $logDir 'local-mcp.err.log'
        $pluginOut = Join-Path $logDir 'plugin.out.log'
        $pluginErr = Join-Path $logDir 'plugin.err.log'
        $backoffSeconds = 3

        while ($true) {
            foreach ($path in @($serverOut, $serverErr, $pluginOut, $pluginErr)) {
                Rotate-Log -Path $path
            }

            $startedAt = Get-Date
            $server = $null
            $plugin = $null
            try {
                $server = Start-Process -FilePath $node -ArgumentList @($serverScript) -WorkingDirectory (Split-Path $serverScript -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
                $plugin = Start-Process -FilePath $node -ArgumentList @($viteScript, 'preview', '--config', $viteConfig, '--host', '127.0.0.1', '--port', '4400') -WorkingDirectory $pluginRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $pluginOut -RedirectStandardError $pluginErr

                while (-not $server.HasExited -and -not $plugin.HasExited) {
                    Start-Sleep -Seconds 2
                    $server.Refresh()
                    $plugin.Refresh()
                }
            }
            catch {
                Write-SupervisorError -Message $_.Exception.Message
            }
            finally {
                Stop-Tree $server
                Stop-Tree $plugin
            }

            $runtimeSeconds = ((Get-Date) - $startedAt).TotalSeconds
            if ($runtimeSeconds -ge 120) {
                $backoffSeconds = 3
            }
            else {
                $backoffSeconds = [Math]::Min(60, [Math]::Max(3, $backoffSeconds * 2))
            }

            Start-Sleep -Seconds $backoffSeconds
        }
        """;

    internal static TalvoraOwnedTempCleanupResult Maintain(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var commonData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonData))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var penpotRoot = Path.Combine(
            commonData,
            "Talvora",
            "Penpot");
        var composePath = Path.Combine(
            penpotRoot,
            "docker-compose.yaml");
        if (!File.Exists(composePath))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        EnsureCanonicalSupervisorScript(
            penpotRoot,
            cancellationToken);

        return CleanupLegacyLogs(
            Path.Combine(
                penpotRoot,
                "logs"),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private static void EnsureCanonicalSupervisorScript(
        string penpotRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            penpotRoot,
            "Start-Talvora-Penpot-Mcp.ps1");
        var expected = CanonicalSupervisorScript;

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(
                path,
                Encoding.UTF8);
            if (string.Equals(
                    existing,
                    expected,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        AtomicFile.WriteAllTextAsync(
                path,
                expected,
                Utf8NoBom,
                createBackup: false,
                cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    private static TalvoraOwnedTempCleanupResult CleanupLegacyLogs(
        string logRoot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(logRoot))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var cutoffUtc = nowUtc - LegacyLogRetention;
        var candidates = new List<FileInfo>();
        var inspected = 0;

        foreach (var path in Directory.EnumerateFiles(
                     logRoot,
                     "*.log",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++inspected > MaximumLegacyLogCandidates)
            {
                break;
            }

            var name = Path.GetFileName(path);
            if (IsLegacyTimestampLogName(name))
            {
                candidates.Add(
                    new FileInfo(path));
            }
        }

        var ordered = candidates
            .OrderByDescending(file =>
                file.LastWriteTimeUtc)
            .ThenByDescending(file =>
                file.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var deleted = 0;
        long reclaimedBytes = 0;
        foreach (var file in ordered.Skip(RetainedLegacyLogs))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastWriteUtc = new DateTimeOffset(
                file.LastWriteTimeUtc,
                TimeSpan.Zero);
            if (lastWriteUtc >= cutoffUtc)
            {
                continue;
            }

            if (!TalvoraOwnedTempCleanup.IsPathUnderRoot(
                    file.FullName,
                    logRoot))
            {
                continue;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                deleted++;
                reclaimedBytes += length;
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
            }
        }

        return new TalvoraOwnedTempCleanupResult(
            deleted,
            reclaimedBytes);
    }

    private static bool IsLegacyTimestampLogName(
        string name)
    {
        var prefixLength = name.StartsWith(
            "local-mcp-",
            StringComparison.OrdinalIgnoreCase)
            ? "local-mcp-".Length
            : name.StartsWith(
                "plugin-",
                StringComparison.OrdinalIgnoreCase)
                ? "plugin-".Length
                : 0;
        if (prefixLength == 0)
        {
            return false;
        }

        const int StampLength = 15;
        if (name.Length <= prefixLength + StampLength)
        {
            return false;
        }

        var stamp = name.Substring(
            prefixLength,
            StampLength);
        if (!DateTime.TryParseExact(
                stamp,
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return false;
        }

        var suffix = name[
            (prefixLength + StampLength)..];
        return suffix is ".out.log" or ".err.log";
    }
}
