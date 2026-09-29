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
            private long droppedBytes;

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
            public long DroppedBytes { get { return Interlocked.Read(ref droppedBytes); } }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { lock (gate) { return inner.Length; } } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

            private FileStream OpenCurrent()
            {
                return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
            }

            private bool RotateIfNeeded(int incomingBytes)
            {
                if (inner.Length + incomingBytes <= maxBytes) return true;
                inner.Flush(true);
                inner.Dispose();
                var rotated = false;
                try
                {
                    if (File.Exists(archivePath)) File.Delete(archivePath);
                    if (File.Exists(path))
                    {
                        File.Move(path, archivePath);
                    }
                    rotated = true;
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

                return rotated ||
                    inner.Length + incomingBytes <= maxBytes;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count > maxBytes)
                {
                    var retainedBytes = (int)maxBytes;
                    var dropped = count - retainedBytes;
                    offset += dropped;
                    count = retainedBytes;
                    Interlocked.Add(ref droppedBytes, dropped);
                }

                lock (gate)
                {
                    if (!RotateIfNeeded(count))
                    {
                        Interlocked.Add(ref droppedBytes, count);
                        return;
                    }
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
            $expectedExecutable = [IO.Path]::GetFullPath($node)
            Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
                Where-Object {
                    $commandLine = [string]$_.CommandLine
                    $executablePath = [string]$_.ExecutablePath
                    $_.ProcessId -ne $PID -and
                    -not [string]::IsNullOrWhiteSpace($commandLine) -and
                    -not [string]::IsNullOrWhiteSpace($executablePath) -and
                    [IO.Path]::GetFullPath($executablePath).Equals($expectedExecutable, [StringComparison]::OrdinalIgnoreCase) -and
                    ($markers | Where-Object { $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
                } |
                ForEach-Object {
                    & "$env:SystemRoot\System32\taskkill.exe" /PID ([int]$_.ProcessId) /T /F 2>$null | Out-Null
                }
        }

        function Rotate-Log([string]$Path, [long]$MaxBytes = 8388608) {
            try {
                if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $true }
                $item = Get-Item -LiteralPath $Path -Force
                if ($item.Length -lt $MaxBytes) { return $true }
                Move-Item -LiteralPath $Path -Destination ($Path + '.1') -Force
                return $true
            }
            catch {
                return $false
            }
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
                    ReportedDroppedBytes = 0
                    LastLogPressureReportUtc = [DateTime]::MinValue
                }
            }
            catch {
                try { Stop-Tree $process } catch { }
                if ($null -ne $outStream) { try { $outStream.Dispose() } catch { } }
                if ($null -ne $errStream) { try { $errStream.Dispose() } catch { } }
                try { $process.Dispose() } catch { }
                throw
            }
        }

        function Report-RollingLogFailures($Child, [string]$Label) {
            if ($null -eq $Child) { return }
            $current = $Child.OutStream.RotationFailures + $Child.ErrStream.RotationFailures
            $dropped = $Child.OutStream.DroppedBytes + $Child.ErrStream.DroppedBytes
            if ($current -gt $Child.ReportedRotationFailures -or
                $dropped -gt $Child.ReportedDroppedBytes) {
                $now = [DateTime]::UtcNow
                if (($now - $Child.LastLogPressureReportUtc).TotalSeconds -lt 60) {
                    return
                }
                Write-SupervisorError -Message ("{0} rolling log pressure: rotation failures +{1}; dropped bytes +{2}." -f $Label, ($current - $Child.ReportedRotationFailures), ($dropped - $Child.ReportedDroppedBytes))
                $Child.ReportedRotationFailures = $current
                $Child.ReportedDroppedBytes = $dropped
                $Child.LastLogPressureReportUtc = $now
            }
        }

        function Stop-LoggedChild($Child) {
            if ($null -eq $Child) { return }
            $cleanupErrors = New-Object System.Collections.Generic.List[string]
            try { Stop-Tree $Child.Process } catch { $cleanupErrors.Add($_.Exception.Message) }
            try { [void]$Child.Process.WaitForExit(5000) } catch { $cleanupErrors.Add($_.Exception.Message) }
            foreach ($task in @($Child.OutTask, $Child.ErrTask)) {
                try { [void]$task.Wait(5000) } catch { $cleanupErrors.Add($_.Exception.Message) }
            }
            if ($Child.OutTask.IsFaulted -or $Child.ErrTask.IsFaulted) {
                $cleanupErrors.Add('Child log drain ended with an error.')
            }
            try { $Child.OutStream.Dispose() } catch { $cleanupErrors.Add($_.Exception.Message) }
            try { $Child.ErrStream.Dispose() } catch { $cleanupErrors.Add($_.Exception.Message) }
            try { $Child.Process.Dispose() } catch { $cleanupErrors.Add($_.Exception.Message) }
            if ($cleanupErrors.Count -gt 0) {
                Write-SupervisorError -Message ('Child cleanup completed with recoverable errors: ' + ($cleanupErrors -join ' | '))
            }
        }

        function Write-SupervisorError([string]$Message) {
            try {
                $path = Join-Path $logDir 'supervisor-error.log'
                if (-not (Rotate-Log -Path $path -MaxBytes 1048576)) {
                    [Console]::Error.WriteLine(("{0:o} {1}" -f (Get-Date), $Message))
                    return
                }
                Add-Content -LiteralPath $path -Encoding utf8 -Value ("{0:o} {1}" -f (Get-Date), $Message)
            }
            catch {
                try { [Console]::Error.WriteLine(("{0:o} Penpot supervisor diagnostic write failed: {1}" -f (Get-Date), $_.Exception.Message)) } catch { }
            }
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

                $lockedLog = Join-Path $testRoot 'locked.log'
                $lockedArchive = $lockedLog + '.1'
                [IO.File]::WriteAllBytes($lockedLog, [byte[]]::new(128))
                [IO.File]::WriteAllText($lockedArchive, 'archive')
                $archiveLock = [IO.FileStream]::new($lockedArchive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
                try {
                    $rotationResult = Rotate-Log -Path $lockedLog -MaxBytes 64
                    if ($rotationResult) { throw 'Locked archive rotation unexpectedly succeeded.' }
                }
                finally {
                    $archiveLock.Dispose()
                }

                $boundedLog = Join-Path $testRoot 'bounded.log'
                $boundedArchive = $boundedLog + '.1'
                [IO.File]::WriteAllText($boundedArchive, 'archive')
                $boundedArchiveLock = [IO.FileStream]::new($boundedArchive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
                try {
                    $boundedStream = [TalvoraRollingLogStream]::new($boundedLog, [long]64)
                    try {
                        $boundedPayload = New-Object byte[] 48
                        $boundedStream.Write($boundedPayload, 0, $boundedPayload.Length)
                        $boundedStream.Write($boundedPayload, 0, $boundedPayload.Length)
                        if ($boundedStream.Length -gt 64 -or
                            $boundedStream.RotationFailures -lt 1 -or
                            $boundedStream.DroppedBytes -ne 48) {
                            throw 'Locked archive must preserve the hard child-log byte bound.'
                        }
                    }
                    finally {
                        $boundedStream.Dispose()
                    }
                }
                finally {
                    $boundedArchiveLock.Dispose()
                }

                $previousLogDir = $logDir
                try {
                    $logDir = Join-Path $testRoot 'error-log-failure'
                    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
                    New-Item -ItemType Directory -Force -Path (Join-Path $logDir 'supervisor-error.log') | Out-Null
                    Write-SupervisorError -Message 'self-test nonfatal logging failure'
                }
                finally {
                    $logDir = $previousLogDir
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
                [void](Rotate-Log -Path $path)
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
        CancellationToken cancellationToken,
        Action<string, Exception>? warning = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var commonData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonData))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var talvoraRoot = Path.Combine(
            commonData,
            "Talvora");
        var penpotRoot = Path.Combine(
            talvoraRoot,
            "Penpot");
        if (!Directory.Exists(penpotRoot))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        EnsureDirectoryIsNotReparsePoint(
            talvoraRoot,
            "Talvora ProgramData root");
        EnsureDirectoryIsNotReparsePoint(
            penpotRoot,
            "Penpot ProgramData root");
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
            cancellationToken,
            warning);
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
            if ((File.GetAttributes(path) &
                 FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Penpot supervisor script is a reparse point and will not be maintained: {path}");
            }

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
        CancellationToken cancellationToken,
        Action<string, Exception>? warning = null)
    {
        if (!Directory.Exists(logRoot))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        EnsureDirectoryIsNotReparsePoint(
            logRoot,
            "Penpot legacy log root");

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
                warning?.Invoke(
                    $"Penpot legacy log cleanup deferred. Path={file.FullName}",
                    ex);
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

    private static void EnsureDirectoryIsNotReparsePoint(
        string path,
        string description)
    {
        var directory = new DirectoryInfo(
            Path.GetFullPath(path));
        if (!directory.Exists)
        {
            return;
        }

        if ((directory.Attributes &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"{description} is a reparse point and maintenance was refused: {directory.FullName}");
        }
    }
}
