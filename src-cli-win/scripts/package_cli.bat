@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0package_cli.ps1" %*
exit /b %ERRORLEVEL%
