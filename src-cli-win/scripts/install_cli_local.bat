@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_cli_local.ps1" %*
exit /b %ERRORLEVEL%
