@echo off
setlocal EnableExtensions

if /I not "%~1"=="__TEMP__" (
  set "BOOT=%TEMP%\Talvora-bootstrap-%RANDOM%-%RANDOM%.cmd"
  copy /Y "%~f0" "%BOOT%" >nul
  start "Talvora Setup" "%ComSpec%" /D /C ""%BOOT%" __TEMP__"
  exit /b 0
)

cd /D "%TEMP%"
set "RESET=%TEMP%\Talvora-reset-%RANDOM%-%RANDOM%.ps1"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/bingoweb/Talvora-MCP/main/scripts/Reset-And-Install.ps1' -OutFile '%RESET%'"
if errorlevel 1 (
  echo Talvora reset script could not be downloaded.
  pause
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RESET%"
set "RC=%ERRORLEVEL%"
del /F /Q "%RESET%" >nul 2>&1
if not "%RC%"=="0" pause
exit /b %RC%
