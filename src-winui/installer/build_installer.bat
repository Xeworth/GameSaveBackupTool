@echo off
rem Compile GSBT_Setup.iss with Inno Setup 6.
rem Prerequisite: scripts\publish_release.bat (produces gsbt-main.exe and gsbt.exe in publish\).
cd /d "%~dp0\.."
call "%CD%\scripts\_env.bat"

set "PUBLISH=%GSBT_ROOT%\src\GSBT.WinUI\bin\Release\%GSBT_TFM%\win-x64\publish"
set "MAIN=%PUBLISH%\gsbt-main.exe"
set "ISS=%GSBT_ROOT%\installer\GSBT_Setup.iss"
set /p GSBT_VERSION=<"%GSBT_ROOT%\..\VERSION"
if not defined GSBT_VERSION (
    echo ERROR: Could not read version from %GSBT_ROOT%\..\VERSION
    exit /b 1
)

if not exist "%MAIN%" goto :nopublish

call "%GSBT_ROOT%\scripts\validate_publish.bat" "%PUBLISH%"
if errorlevel 1 exit /b 1

if defined ISCC goto :have_iscc
set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ISCC_EXE%" goto :have_iscc
set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%ISCC_EXE%" goto :have_iscc
echo ERROR: ISCC.exe not found. Install Inno Setup 6 or set ISCC to its path.
exit /b 1

:have_iscc
if not defined ISCC_EXE set "ISCC_EXE=%ISCC%"

if not exist "%CD%\installer\output" mkdir "%CD%\installer\output"

echo Compiling installer with:
echo   %ISCC_EXE%
echo.
echo NOTE: WizardStyle=modern dynamic requires Inno Setup 6.5.4 or newer.
echo.
"%ISCC_EXE%" /DMyAppVersion="%GSBT_VERSION%" "%ISS%"
if errorlevel 1 exit /b 1

echo.
echo Done. Setup package:
echo   %CD%\installer\output\
echo.
echo Silent install: GSBT_Setup_*.exe /VERYSILENT
echo.
exit /b 0

:nopublish
echo ERROR: Publish output not found:
echo   %MAIN%
echo Run scripts\publish_release.bat first.
exit /b 1
