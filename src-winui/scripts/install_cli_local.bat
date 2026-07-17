@echo off
rem Build and install the local GSBT CLI into the per-user app folder.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_cli_local.ps1" %*
set "RUNEXIT=%ERRORLEVEL%"
if "%RUNEXIT%"=="0" exit /b 0

echo.
echo GSBT CLI local install failed.
echo If you opened this by double-clicking, the window is paused so the error stays visible.
pause
exit /b %RUNEXIT%
