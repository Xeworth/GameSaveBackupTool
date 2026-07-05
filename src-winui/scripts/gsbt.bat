@echo off
rem Build CLI and run gsbt without dotnet run (dev convenience).
rem Does NOT add PATH permanently: use installer or publish for that.
cd /d "%~dp0\.."
call "%~dp0_env.bat"

set "CLI_PROJ=%GSBT_ROOT%\src\GSBT.Cli\GSBT.Cli.csproj"
set "CLI_BIN=%GSBT_ROOT%\src\GSBT.Cli\bin\Debug\%GSBT_TFM%\win-x64"

"%DOTNET%" build "%CLI_PROJ%" -c Debug -r win-x64 -v q
if errorlevel 1 exit /b 1

if not exist "%CLI_BIN%\gsbt.exe" (
    echo ERROR: gsbt.exe not found at %CLI_BIN%
    exit /b 1
)

"%CLI_BIN%\gsbt.exe" %*
