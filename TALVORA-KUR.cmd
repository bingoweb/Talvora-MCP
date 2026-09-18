@echo off
setlocal EnableExtensions

for %%I in ("%~dp0.") do set "REPO=%%~fI"
set "SCRIPT=%REPO%\scripts\Install.ps1"

if not exist "%SCRIPT%" (
  echo Talvora install script not found: %SCRIPT%
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -RepoRoot "%REPO%"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo Talvora installation failed with exit code %RC%.
)
exit /b %RC%
