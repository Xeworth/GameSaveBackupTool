@echo off
rem Build all WinUI release assets for GitHub Releases:
rem   1. self-contained publish\
rem   2. installer\output\GSBT_Setup_*.exe
rem   3. installer\output\GSBT_Portable_*.zip
cd /d "%~dp0\.."

call "%~dp0publish_release.bat"
if errorlevel 1 exit /b 1

call "%~dp0package_portable.bat"
if errorlevel 1 exit /b 1

call "%CD%\installer\build_installer.bat"
if errorlevel 1 exit /b 1

echo.
echo All release assets are in:
echo   %CD%\installer\output\
echo.
echo Upload these to a GitHub Release (see installer\README.md).
echo.
exit /b 0
