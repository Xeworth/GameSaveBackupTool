@echo off
rem Double-click friendly CLI development shell.
rem Builds Debug gsbt.exe, adds it to PATH for this terminal, and keeps the window open.
setlocal

cd /d "%~dp0\.."
call "%~dp0_env.bat"
if errorlevel 1 goto fail

set "CLI_PROJ=%GSBT_ROOT%\src\GSBT.Cli\GSBT.Cli.csproj"
set "CLI_BIN=%GSBT_ROOT%\src\GSBT.Cli\bin\Debug\%GSBT_TFM%\win-x64"

echo Building GSBT CLI...
"%DOTNET%" build "%CLI_PROJ%" -c Debug -r win-x64 -v q
if errorlevel 1 goto fail

if not exist "%CLI_BIN%\gsbt.exe" (
    echo ERROR: gsbt.exe not found at %CLI_BIN%
    goto fail
)

set "PATH=%CLI_BIN%;%PATH%"
cls
echo GSBT CLI development shell
echo.
echo gsbt.exe:
echo   %CLI_BIN%\gsbt.exe
echo.
echo Try:
echo   gsbt status
echo   gsbt list
echo   gsbt help
echo.
echo This terminal stays open. Type exit to close it.
echo.
if defined GSBT_CLI_SHELL_NO_KEEPALIVE exit /b 0
cmd /k
exit /b 0

:fail
echo.
echo GSBT CLI shell failed to start.
echo Working directory:
echo   %CD%
echo.
pause
exit /b 1
