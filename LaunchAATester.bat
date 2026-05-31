@echo off
setlocal

REM Adjust if your SDK lives elsewhere
if "%ANDROID_HOME%"=="" set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"
set "DHU_DIR=%ANDROID_HOME%\extras\google\auto"
set "ADB=%ANDROID_HOME%\platform-tools\adb.exe"

echo Checking for connected device...
"%ADB%" devices
echo.

set "SERIAL=4A280DLAQ005QD"


echo Forwarding port 5277...
"%ADB%" -s %SERIAL% forward tcp:5277 tcp:5277
if errorlevel 1 (
    echo adb forward failed - is the phone connected and head unit server started?
    pause
    exit /b 1
)

echo Launching Desktop Head Unit...
cd /d "%DHU_DIR%"
desktop-head-unit.exe

endlocal
