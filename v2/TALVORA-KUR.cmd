@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-local.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" echo Talvora kurulumu tamamlanamadi. Hata kodu: %result%
if "%result%"=="0" echo TALVORA LOCAL READY - Yerel istemciyi yeniden acin, MCP adi: talvora_local
pause
exit /b %result%
