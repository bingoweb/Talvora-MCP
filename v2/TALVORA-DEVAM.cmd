@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\continue-local.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" echo Talvora bakimi tamamlanamadi. Yerel raporda ayrintilar bulunur.
if "%result%"=="0" echo TALVORA LOCAL READY - Yerel kontrol tamamlandi.
pause
exit /b %result%
