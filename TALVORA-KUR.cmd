@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Reset-And-Install.ps1"
exit /b %errorlevel%
