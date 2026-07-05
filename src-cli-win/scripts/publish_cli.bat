@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_cli.ps1" %*
exit /b %ERRORLEVEL%
