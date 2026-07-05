@echo off
rem Shared build/publish settings for GSBT scripts.

if not defined GSBT_ROOT set "GSBT_ROOT=%~dp0.."
for %%I in ("%GSBT_ROOT%") do set "GSBT_ROOT=%%~fI"
set "GSBT_REPO_ROOT=%GSBT_ROOT%\.."
for %%I in ("%GSBT_REPO_ROOT%") do set "GSBT_REPO_ROOT=%%~fI"

set "GSBT_TFM=net10.0-windows10.0.19041.0"

if not defined DOTNET set "DOTNET=dotnet"
if exist "%GSBT_REPO_ROOT%\.dotnet\dotnet.exe" (
    set "DOTNET=%GSBT_REPO_ROOT%\.dotnet\dotnet.exe"
    set "DOTNET_ROOT=%GSBT_REPO_ROOT%\.dotnet"
    set "PATH=%DOTNET_ROOT%;%PATH%"
) else if exist "%USERPROFILE%\.dotnet10\dotnet.exe" (
    set "DOTNET=%USERPROFILE%\.dotnet10\dotnet.exe"
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet10"
    set "PATH=%DOTNET_ROOT%;%PATH%"
)

exit /b 0
