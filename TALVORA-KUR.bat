@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title Talvora - Kurulum ve Dogrulama

set "TALVORA_PS="
for /f "usebackq delims=" %%P in (`call "%~dp0scripts\Resolve-PowerShell.cmd" --install`) do if not defined TALVORA_PS set "TALVORA_PS=%%P"

if not defined TALVORA_PS (
  echo.
  echo Talvora PowerShell calistiricisini bulamadi veya kuramadi.
  echo scripts\Resolve-PowerShell.cmd --install komutunun ciktisini kontrol et.
  pause
  exit /b 9009
)

"%TALVORA_PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bootstrap-windows.ps1"
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" (
  echo Talvora kurulumu/dogrulamasi hata ile sonlandi. Kod: %EXITCODE%
) else (
  echo Talvora kurulumu/dogrulamasi tamamlandi.
)
pause
exit /b %EXITCODE%
