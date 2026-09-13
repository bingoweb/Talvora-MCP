@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "TALVORA_PS="
set "TALVORA_WINGET="

call :FindPowerShell
if defined TALVORA_PS (
  echo(!TALVORA_PS!
  exit /b 0
)

if /I not "%~1"=="--install" exit /b 9009

call :FindWinget
if not defined TALVORA_WINGET (
  >&2 echo Talvora: PowerShell bulunamadi ve WinGet de erisilebilir degil.
  >&2 echo Talvora: Windows App Installer/WinGet kurulumunu kontrol et.
  exit /b 9009
)

>&2 echo Talvora: PowerShell bulunamadi. PowerShell 7 kuruluyor...
"!TALVORA_WINGET!" install --id Microsoft.PowerShell --exact --source winget --accept-package-agreements --accept-source-agreements --silent 1>&2
if errorlevel 1 exit /b %ERRORLEVEL%

call :FindPowerShell
if defined TALVORA_PS (
  echo(!TALVORA_PS!
  exit /b 0
)

>&2 echo Talvora: PowerShell 7 kuruldu ancak calistirilabilir dosya bulunamadi.
exit /b 9009

:FindPowerShell
if defined ProgramFiles if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" (
  set "TALVORA_PS=%ProgramFiles%\PowerShell\7\pwsh.exe"
  goto :eof
)
if defined ProgramW6432 if exist "%ProgramW6432%\PowerShell\7\pwsh.exe" (
  set "TALVORA_PS=%ProgramW6432%\PowerShell\7\pwsh.exe"
  goto :eof
)
if defined LOCALAPPDATA if exist "%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe" (
  set "TALVORA_PS=%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe"
  goto :eof
)
for /f "delims=" %%P in ('where.exe pwsh.exe 2^>nul') do (
  if not defined TALVORA_PS set "TALVORA_PS=%%P"
)
if defined TALVORA_PS goto :eof
if defined SystemRoot if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" (
  set "TALVORA_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
  goto :eof
)
if defined SystemRoot if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" (
  set "TALVORA_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  goto :eof
)
for /f "delims=" %%P in ('where.exe powershell.exe 2^>nul') do (
  if not defined TALVORA_PS set "TALVORA_PS=%%P"
)
goto :eof

:FindWinget
if defined LOCALAPPDATA if exist "%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe" (
  set "TALVORA_WINGET=%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe"
  goto :eof
)
for /f "delims=" %%P in ('where.exe winget.exe 2^>nul') do (
  if not defined TALVORA_WINGET set "TALVORA_WINGET=%%P"
)
goto :eof
