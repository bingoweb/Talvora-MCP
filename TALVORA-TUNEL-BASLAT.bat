@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title Talvora - OpenAI MCP Tuneli

set "TALVORA_PS="
for /f "usebackq delims=" %%P in (`call "%~dp0scripts\Resolve-PowerShell.cmd"`) do if not defined TALVORA_PS set "TALVORA_PS=%%P"

if not defined TALVORA_PS (
  echo PowerShell 7 bulunamadi. Once TALVORA-KUR.bat calistir.
  pause
  exit /b 9009
)

"%TALVORA_PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-OpenAITunnel.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo Talvora OpenAI MCP tuneli hata ile sonlandi. Kod: %EXITCODE%
  pause
)

exit /b %EXITCODE%
