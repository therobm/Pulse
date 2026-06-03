
@echo off
setlocal
 
if "%ANDROID_HOME%"=="" set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"
set "ADB=%ANDROID_HOME%\platform-tools\adb.exe"
set "SERIAL=4A280DLAQ005QD"
 
"%ADB%" -s %SERIAL% shell am force-stop com.google.android.projection.gearhead
"%ADB%" -s %SERIAL% shell pm clear com.google.android.projection.gearhead
 
echo Done.
pause
 
endlocal
 