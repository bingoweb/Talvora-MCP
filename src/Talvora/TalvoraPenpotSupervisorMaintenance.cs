using System.Globalization;
using System.IO;
using System.Text;
using Talvora.Shared;

namespace Talvora;

internal static class TalvoraPenpotSupervisorMaintenance
{
    private const int MaximumLegacyLogEntriesPerPattern = 10_000;
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

        if (-not ('TalvoraRollingLogStream' -as [type])) {
            Add-Type -TypeDefinition @'
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;

        public sealed class TalvoraRollingLogStream : Stream
        {
            private readonly string path;
            private readonly string archivePath;
            private readonly long maxBytes;
            private readonly object gate = new object();
            private FileStream inner;
            private int rotationFailures;

            public TalvoraRollingLogStream(string path, long maxBytes)
            {
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
                if (maxBytes <= 0) throw new ArgumentOutOfRangeException("maxBytes");
                this.path = Path.GetFullPath(path);
                this.archivePath = this.path + ".1";
                this.maxBytes = maxBytes;
                Directory.CreateDirectory(Path.GetDirectoryName(this.path));
                this.inner = OpenCurrent();
            }

            public int RotationFailures { get { return Volatile.Read(ref rotationFailures); } }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { lock (gate) { return inner.Length; } } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

            private FileStream OpenCurrent()
            {
                return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
            }

            private void RotateIfNeeded(int incomingBytes)
            {
                if (inner.Length == 0 || inner.Length + incomingBytes <= maxBytes) return;
                inner.Flush(true);
                inner.Dispose();
                try
                {
                    if (File.Exists(archivePath)) File.Delete(archivePath);
                    if (File.Exists(path)) File.Move(path, archivePath);
                }
                catch (IOException)
                {
                    Interlocked.Increment(ref rotationFailures);
                }
                catch (UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref rotationFailures);
                }
                finally
                {
                    inner = OpenCurrent();
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (gate)
                {
                    RotateIfNeeded(count);
                    inner.Write(buffer, offset, count);
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Write(buffer, offset, count);
                return Task.FromResult(0);
            }

            public override void Flush() { lock (gate) { inner.Flush(); } }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (gate)
                    {
                        if (inner != null)
                        {
                            inner.Flush();
                            inner.Dispose();
                            inner = null;
                        }
                    }
                }
                base.Dispose(disposing);
            }
        }
        '@
        }

        function Stop-Tree([System.Diagnostics.Process]$Process) {
            if ($null -eq $Process -or $Process.HasExited) { return }
            & "$env:SystemRoot\System32\taskkill.exe" /PID $Process.Id /T /F 2>$null | Out-Null
        }

        function Stop-OrphanedPenpotProcesses {
            $markers = @($serverScript, $viteScript)
            Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
                Where-Object {
                    $commandLine = [string]$_.CommandLine
                    $_.ProcessId -ne $PID -and
                    -not [string]::IsNullOrWhiteSpace($commandLine) -and
                    ($markers | Where-Object { $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
                } |
                ForEach-Object {
                    & "$env:SystemRoot\System32\taskkill.exe" /PID ([int]$_.ProcessId) /T /F 2>$null | Out-Null
                }
        }

        function Rotate-Log([string]$Path, [long]$MaxBytes = 8388608) {
            if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
            $item = Get-Item -LiteralPath $Path -Force
            if ($item.Length -lt $MaxBytes) { return }
            Move-Item -LiteralPath $Path -Destination ($Path + '.1') -Force
        }

        function Start-LoggedChild(
            [string]$FilePath,
            [string]$Arguments,
            [string]$WorkingDirectory,
            [string]$StdOutPath,
            [string]$StdErrPath) {
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $FilePath
            $startInfo.Arguments = $Arguments
            $startInfo.WorkingDirectory = $WorkingDirectory
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true

            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            $outStream = $null
            $errStream = $null
            try {
                if (-not $process.Start()) { throw 'Child process did not start.' }
                $outStream = [TalvoraRollingLogStream]::new($StdOutPath, [long]8388608)
                $errStream = [TalvoraRollingLogStream]::new($StdErrPath, [long]8388608)
                $outTask = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
                $errTask = $process.StandardError.BaseStream.CopyToAsync($errStream)
                return [pscustomobject]@{
                    Process = $process
                    OutStream = $outStream
                    ErrStream = $errStream
                    OutTask = $outTask
                    ErrTask = $errTask
                    ReportedRotationFailures = 0
                }
            }
            catch {
                Stop-Tree $process
                if ($null -ne $outStream) { $outStream.Dispose() }
                if ($null -ne $errStream) { $errStream.Dispose() }
                $process.Dispose()
                throw
            }
        }

        function Report-RollingLogFailures($Child, [string]$Label) {
            if ($null -eq $Child) { return }
            $current = $Child.OutStream.RotationFailures + $Child.ErrStream.RotationFailures
            if ($current -gt $Child.ReportedRotationFailures) {
                Write-SupervisorError -Message ("{0} rolling log rotation was deferred {1} time(s)." -f $Label, ($current - $Child.ReportedRotationFailures))
                $Child.ReportedRotationFailures = $current
            }
        }

        function Stop-LoggedChild($Child) {
            if ($null -eq $Child) { return }
            Stop-Tree $Child.Process
            try { [void]$Child.Process.WaitForExit(5000) } catch { }
            foreach ($task in @($Child.OutTask, $Child.ErrTask)) {
                try { [void]$task.Wait(5000) } catch { }
            }
            if ($Child.OutTask.IsFaulted -or $Child.ErrTask.IsFaulted) {
                Write-SupervisorError -Message 'Child log drain ended with an error.'
            }
            $Child.OutStream.Dispose()
            $Child.ErrStream.Dispose()
            $Child.Process.Dispose()
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

        if ($env:TALVORA_PENPOT_SUPERVISOR_SELFTEST -eq '1') {
            $testRoot = Join-Path $env:TEMP ('Talvora-Penpot-Rolling-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
            try {
                $testPath = Join-Path $testRoot 'rolling.log'
                $testStream = [TalvoraRollingLogStream]::new($testPath, [long]64)
                try {
                    $payload = New-Object byte[] 48
                    $testStream.Write($payload, 0, $payload.Length)
                    $testStream.Write($payload, 0, $payload.Length)
                }
                finally {
                    $testStream.Dispose()
                }

                $archivePath = $testPath + '.1'
                if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
                    (Get-Item -LiteralPath $archivePath).Length -ne 48 -or
                    (Get-Item -LiteralPath $testPath).Length -ne 48 -or
                    $testStream.RotationFailures -ne 0) {
                    throw 'Rolling log stream self-test failed.'
                }

                $childOut = Join-Path $testRoot 'child.out.log'
                $childErr = Join-Path $testRoot 'child.err.log'
                $cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'
                $child = Start-LoggedChild -FilePath $cmd -Arguments '/d /c "echo child-ok & echo child-err 1>&2"' -WorkingDirectory $testRoot -StdOutPath $childOut -StdErrPath $childErr
                try {
                    if (-not $child.Process.WaitForExit(10000)) {
                        throw 'Redirected child did not exit during self-test.'
                    }
                }
                finally {
                    Stop-LoggedChild $child
                }

                if ((Get-Content -LiteralPath $childOut -Raw).Trim() -ne 'child-ok' -or
                    (Get-Content -LiteralPath $childErr -Raw).Trim() -ne 'child-err') {
                    throw 'Redirected child stream self-test failed.'
                }

                Write-Output 'PENPOT_SUPERVISOR_ROLLING_SELFTEST_GREEN'
            }
            finally {
                Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            exit 0
        }

        $backoffSeconds = 3
        Stop-OrphanedPenpotProcesses

        while ($true) {
            foreach ($path in @($serverOut, $serverErr, $pluginOut, $pluginErr)) {
                Rotate-Log -Path $path
            }

            $startedAt = Get-Date
            $server = $null
            $plugin = $null
            try {
                $serverArguments = '"{0}"' -f $serverScript
                $pluginArguments = '"{0}" preview --config "{1}" --host 127.0.0.1 --port 4400' -f $viteScript, $viteConfig
                $server = Start-LoggedChild -FilePath $node -Arguments $serverArguments -WorkingDirectory (Split-Path $serverScript -Parent) -StdOutPath $serverOut -StdErrPath $serverErr
                $plugin = Start-LoggedChild -FilePath $node -Arguments $pluginArguments -WorkingDirectory $pluginRoot -StdOutPath $pluginOut -StdErrPath $pluginErr

                while (-not $server.Process.HasExited -and -not $plugin.Process.HasExited) {
                    Start-Sleep -Seconds 2
                    $server.Process.Refresh()
                    $plugin.Process.Refresh()
                    Report-RollingLogFailures $server 'Penpot MCP'
                    Report-RollingLogFailures $plugin 'Penpot plugin'
                    if ($server.OutTask.IsFaulted -or $server.ErrTask.IsFaulted -or
                        $plugin.OutTask.IsFaulted -or $plugin.ErrTask.IsFaulted) {
                        throw 'Penpot child log drain failed.'
                    }
                }
            }
            catch {
                Write-SupervisorError -Message $_.Exception.Message
            }
            finally {
                Stop-LoggedChild $server
                Stop-LoggedChild $plugin
                Stop-OrphanedPenpotProcesses
            }

            $runtimeSeconds = ((Get-Date) - $startedAt).TotalSeconds
            $delaySeconds = $backoffSeconds
            if ($runtimeSeconds -ge 120) {
                $backoffSeconds = 3
                $delaySeconds = 3
            }
            else {
                $backoffSeconds = [Math]::Min(60, [Math]::Max(3, $backoffSeconds * 2))
            }

            Start-Sleep -Seconds $delaySeconds
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
        var expectedByteLength =
            Utf8NoBom.GetByteCount(expected);

        if (File.Exists(path))
        {
            using var existingStream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (existingStream.Length == expectedByteLength)
            {
                using var reader = new StreamReader(
                    existingStream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
                var existing = reader.ReadToEnd();
                if (string.Equals(
                        existing,
                        expected,
                        StringComparison.Ordinal))
                {
                    return;
                }
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
        var scanLimitReached = false;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in new[]
                 {
                     "local-mcp-*.log",
                     "plugin-*.log",
                 })
        {
            var inspectedEntries = 0;
            foreach (var path in Directory.EnumerateFiles(
                         logRoot,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++inspectedEntries >
                    MaximumLegacyLogEntriesPerPattern)
                {
                    scanLimitReached = true;
                    break;
                }
                var fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath) ||
                    !IsLegacyTimestampLogName(
                        Path.GetFileName(fullPath)))
                {
                    continue;
                }

                candidates.Add(
                    new FileInfo(fullPath));
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
            reclaimedBytes,
            scanLimitReached);
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
