$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Talvora.Local.psm1') -Force -DisableNameChecking
Write-Host '=== Talvora Local MCP ==='
Get-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\' -ErrorAction SilentlyContinue | Select-Object TaskName,State | Format-Table | Out-Host
$health = Get-TalvoraLocalHealth
if ($null -ne $health) { $health | ConvertTo-Json | Out-Host } else { Write-Host 'Gateway hazir oldugu dogrulanamadi.' }
Write-Host ('Gunlukler: ' + (Join-Path $env:LOCALAPPDATA 'Talvora\Local\logs'))
