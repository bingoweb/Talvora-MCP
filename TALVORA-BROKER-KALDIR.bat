@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title Talvora - Elevated Broker Kaldir

set "TALVORA_PS="
for /f "usebackq delims=" %%P in (`call "%~dp0scripts\Resolve-PowerShell.cmd"`) do if not defined TALVORA_PS set "TALVORA_PS=%%P"

if not defined TALVORA_PS (
  echo PowerShell 7 bulunamadi.
  pause
  exit /b 9009
)

"%TALVORA_PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Uninstall-BrokerService.ps1"
set "EXITCODE=%ERRORLEVEL%"
echo.
pause
exit /b %EXITCODE%
