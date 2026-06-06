@echo off
rem Zip the self-contained publish\ folder for a portable GitHub Release asset.
rem Prerequisite: scripts\publish_release.bat
rem Output: installer\output\GSBT_Portable_<version>.zip
setlocal EnableDelayedExpansion
cd /d "%~dp0\.."

set "PUBLISH=%CD%\src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
set "MAIN=%PUBLISH%\gsbt.exe"
set "OUTDIR=%CD%\installer\output"
set "ISS=%CD%\installer\GSBT_Setup.iss"

if not exist "%MAIN%" goto :nopublish

call "%CD%\scripts\validate_publish.bat" "%PUBLISH%"
if errorlevel 1 exit /b 1

for /f "tokens=3 delims= " %%V in ('findstr /C:"#define MyAppVersion" "%ISS%"') do set "VERSION=%%~V"
if not defined VERSION (
    echo ERROR: Could not read MyAppVersion from installer\GSBT_Setup.iss
    exit /b 1
)

set "ZIP=%OUTDIR%\GSBT_Portable_%VERSION%.zip"
set "STAGE=%OUTDIR%\portable_stage_%VERSION%"

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%ZIP%" del /f /q "%ZIP%"

echo Staging portable package...
robocopy "%PUBLISH%" "%STAGE%" /E /NFL /NDL /NJH /NJS /nc /ns /np >nul
if errorlevel 8 (
    echo ERROR: Failed to stage publish output.
    exit /b 1
)

if not exist "%STAGE%\gsbt-sandbox.exe" (
    echo ERROR: gsbt-sandbox.exe missing. Run scripts\publish_release.bat first.
    rd /s /q "%STAGE%"
    exit /b 1
)

copy /Y "%~dp0..\installer\PORTABLE.txt" "%STAGE%\README.txt" >nul

echo Compressing to %ZIP% ...
if exist "%ZIP%" del /f /q "%ZIP%"
tar -a -cf "%ZIP%" -C "%STAGE%" .
if errorlevel 1 (
    echo ERROR: Failed to create zip. Close any running gsbt.exe and retry.
    rd /s /q "%STAGE%"
    exit /b 1
)

rd /s /q "%STAGE%"

if not exist "%ZIP%" (
    echo ERROR: Zip was not created.
    exit /b 1
)

for %%A in ("%ZIP%") do set "ZIP_BYTES=%%~zA"
set /a ZIP_MB=!ZIP_BYTES!/1048576

echo.
echo Done. Portable package:
echo   %ZIP%
echo   ~!ZIP_MB! MB zip
echo.
echo Extract anywhere and run gsbt.exe. Settings go to %%AppData%%\GSBT as usual.
echo.
exit /b 0

:nopublish
echo ERROR: Publish output not found:
echo   %MAIN%
echo Run scripts\publish_release.bat first.
exit /b 1
