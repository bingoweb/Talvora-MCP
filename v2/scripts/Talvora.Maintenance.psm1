Set-StrictMode -Version Latest

function Test-TalvoraLegacyAction {
    param($Action,[Parameter(Mandatory)][string]$LegacyExe)
    if ($null -eq $Action) { return $false }
    $property=$Action.PSObject.Properties['Execute']
    if ($null -eq $property) { return $false }
    return [Environment]::ExpandEnvironmentVariables([string]$property.Value).Trim().Trim('"') -ieq $LegacyExe
}

function Disable-TalvoraLegacyTasks {
    param([Parameter(Mandatory)][string]$LegacyExe,[Parameter(Mandatory)][string]$BackupRoot)
    foreach ($task in (Get-ScheduledTask -ErrorAction Stop)) {
        $matches=@($task.Actions | Where-Object { Test-TalvoraLegacyAction -Action $_ -LegacyExe $LegacyExe })
        if (-not $matches.Count) { continue }
        $alreadyDisabled=([int]$task.State -eq 1)
        $backupPath=$null
        if (-not $alreadyDisabled) {
            New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
            $backupPath=Join-Path $BackupRoot ([Guid]::NewGuid().ToString('N')+'.xml')
            Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath | Set-Content -LiteralPath $backupPath -Encoding Unicode
            Disable-ScheduledTask -InputObject $task | Out-Null
        }
        Stop-ScheduledTask -InputObject $task
        [pscustomobject]@{TaskName=$task.TaskName;TaskPath=$task.TaskPath;AlreadyDisabled=$alreadyDisabled;BackupPath=$backupPath}
    }
}

function Move-TalvoraLegacyGateway {
    param([Parameter(Mandatory)][string]$GatewayDirectory,[Parameter(Mandatory)][string]$ArchiveRoot)
    $source=[IO.Path]::GetFullPath($GatewayDirectory).TrimEnd('\')
    $archive=[IO.Path]::GetFullPath($ArchiveRoot).TrimEnd('\')
    if ([IO.Path]::GetFileName($source) -ine 'Gateway') { throw 'Only the identified legacy Gateway directory can be retired.' }
    if ($archive -ieq $source -or $archive.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must be outside the legacy directory.' }
    if (-not (Test-Path -LiteralPath $source)) { return [pscustomobject]@{Status='Absent';OriginalPath=$source;ArchivePath=$null} }
    if (-not (Test-Path -LiteralPath (Join-Path $source 'Talvora.Gateway.exe') -PathType Leaf)) { throw 'Legacy Gateway executable marker is absent; directory was left unchanged.' }
    # Walk explicitly so junctions are never traversed into the new installation.
    $pending=[Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push([IO.DirectoryInfo]::new($source))
    while ($pending.Count) {
        $directory=$pending.Pop()
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Legacy directory contains a filesystem link; it was not moved.' }
        foreach ($entry in $directory.EnumerateFileSystemInfos()) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Legacy directory contains a filesystem link; it was not moved.' }
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry) }
        }
    }
    $batch=Join-Path $archive ((Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $batch -Force | Out-Null
    $destination=Join-Path $batch 'Gateway'
    $record=[pscustomobject]@{Status='Archived';OriginalPath=$source;ArchivePath=$destination;TimeUtc=[DateTime]::UtcNow.ToString('o')}
    $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $batch 'restore-location.json') -Encoding UTF8
    Move-Item -LiteralPath $source -Destination $destination -ErrorAction Stop
    return $record
}

Export-ModuleMember -Function Test-TalvoraLegacyAction,Disable-TalvoraLegacyTasks,Move-TalvoraLegacyGateway
