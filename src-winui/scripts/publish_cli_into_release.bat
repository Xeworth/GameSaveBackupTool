@echo off
rem Merge framework-dependent CLI publish into the WinUI release folder (same install dir).
rem Usage: publish_cli_into_release.bat [publish-dir]
setlocal EnableDelayedExpansion

set "OUT=%~1"
if "%OUT%"=="" (
    echo ERROR: publish_cli_into_release.bat requires a publish directory argument.
    exit /b 1
)

call "%~dp0_env.bat"
set "ROOT=%GSBT_ROOT%"
set "CLI_PROJ=%ROOT%\src\GSBT.Cli\GSBT.Cli.csproj"
set "CLI_OUT=%ROOT%\src\GSBT.Cli\bin\Release\%GSBT_TFM%\win-x64\publish"

echo Publishing CLI win-x64 ^(self-contained, shares install folder with WinUI^)...
"%DOTNET%" publish "%CLI_PROJ%" -c Release -r win-x64 -p:SelfContained=true
if errorlevel 1 exit /b 1

if not exist "%CLI_OUT%\gsbt.exe" (
    echo ERROR: CLI publish missing gsbt.exe
    exit /b 1
)

echo Merging CLI into %OUT% ...
xcopy /E /I /Y /Q "%CLI_OUT%\*" "%OUT%\" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy CLI publish into release folder.
    exit /b 1
)

if not exist "%OUT%\gsbt.exe" (
    echo ERROR: gsbt.exe missing after merge.
    exit /b 1
)
if not exist "%OUT%\gsbt-main.exe" (
    echo ERROR: gsbt-main.exe missing - WinUI publish must run before CLI merge.
    exit /b 1
)

echo CLI merge OK: gsbt.exe ^(terminal^) + gsbt-main.exe ^(GUI^) in %OUT%
exit /b 0
