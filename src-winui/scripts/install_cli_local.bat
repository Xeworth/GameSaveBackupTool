@echo off
rem Build and install the local GSBT CLI into the per-user app folder.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_cli_local.ps1" %*
exit /b %ERRORLEVEL%
