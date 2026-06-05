@echo off
rem Compile GSBT_Setup.iss with Inno Setup 6.
rem Prerequisite: scripts\publish_release.bat (produces gsbt.exe in publish\).
cd /d "%~dp0\.."

set "PUBLISH=%CD%\src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
set "MAIN=%PUBLISH%\gsbt.exe"
set "ISS=%CD%\installer\GSBT_Setup.iss"

if not exist "%MAIN%" goto :nopublish

call "%CD%\scripts\validate_publish.bat" "%PUBLISH%"
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
"%ISCC_EXE%" "%ISS%"
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
