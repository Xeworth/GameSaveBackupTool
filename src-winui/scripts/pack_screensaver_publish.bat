@echo off
rem Pack screen saver video/audio into data\screensaver.7z for release publish.
rem Usage: pack_screensaver_publish.bat <publish-dir>
setlocal

set "OUT=%~1"
if "%OUT%"=="" (
    echo ERROR: pack_screensaver_publish.bat requires publish directory.
    exit /b 1
)

set "ROOT=%~dp0.."
set "ASSETS=%ROOT%\assets"
set "ARCHIVE=%OUT%\data\screensaver.7z"
set "SEVEN=%ROOT%\native\win-x64\7z.dll"
set "PACKER=%ROOT%\tools\ScreenSaverAssetPacker\ScreenSaverAssetPacker.csproj"

if not exist "%ASSETS%\video" (
    echo ERROR: Missing %ASSETS%\video
    exit /b 1
)
if not exist "%ASSETS%\audio" (
    echo ERROR: Missing %ASSETS%\audio
    exit /b 1
)
if not exist "%SEVEN%" (
    echo ERROR: Missing %SEVEN%
    exit /b 1
)

if not exist "%OUT%\data" mkdir "%OUT%\data"

echo Packing screen saver media into data\screensaver.7z ...
dotnet run --project "%PACKER%" -c Release --no-launch-profile -- "%ASSETS%" "%ARCHIVE%" "%SEVEN%"
if errorlevel 1 exit /b 1

if not exist "%ARCHIVE%" (
    echo ERROR: Archive was not created: %ARCHIVE%
    exit /b 1
)

rem Remove loose media from install folder (dev loose files may still be copied on some builds).
if exist "%OUT%\assets\video" rd /s /q "%OUT%\assets\video"
if exist "%OUT%\assets\audio" rd /s /q "%OUT%\assets\audio"
if exist "%OUT%\Assets\video" rd /s /q "%OUT%\Assets\video"
if exist "%OUT%\Assets\audio" rd /s /q "%OUT%\Assets\audio"

echo Screen saver archive ready.
exit /b 0
