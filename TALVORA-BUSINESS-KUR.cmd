@echo off
setlocal EnableExtensions

if /I not "%~1"=="__TEMP__" (
  set "BOOT=%TEMP%\Talvora-business-bootstrap-%RANDOM%-%RANDOM%.cmd"
  copy /Y "%~f0" "%BOOT%" >nul
  if /I "%~1"=="reconnect" (
    start "Talvora ChatGPT Business" "%ComSpec%" /D /C ""%BOOT%" __TEMP__ reconnect"
  ) else (
    start "Talvora ChatGPT Business" "%ComSpec%" /D /C ""%BOOT%" __TEMP__"
  )
  exit /b 0
)

cd /D "%TEMP%"
set "SETUP=%TEMP%\Talvora-business-%RANDOM%-%RANDOM%.ps1"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/bingoweb/Talvora-MCP/main/scripts/Configure-ChatGPT-Business.ps1' -OutFile '%SETUP%'"
if errorlevel 1 (
  echo Talvora ChatGPT Business setup script could not be downloaded.
  pause
  exit /b 1
)

set "EXTRA="
if /I "%~2"=="reconnect" set "EXTRA=-Reconnect"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SETUP%" %EXTRA%
set "RC=%ERRORLEVEL%"
del /F /Q "%SETUP%" >nul 2>&1
if not "%RC%"=="0" pause
exit /b %RC%
