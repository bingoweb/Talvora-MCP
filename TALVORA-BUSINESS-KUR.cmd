@echo off
setlocal EnableExtensions

for %%I in ("%~dp0.") do set "REPO=%%~fI"
set "SCRIPT=%REPO%\scripts\Configure-ChatGPT-Business.ps1"

if not exist "%SCRIPT%" (
  echo Talvora Business script not found: %SCRIPT%
  exit /b 1
)

if /I "%~1"=="reconnect" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Reconnect
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
)

set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo Talvora ChatGPT Business setup failed with exit code %RC%.
)
exit /b %RC%
