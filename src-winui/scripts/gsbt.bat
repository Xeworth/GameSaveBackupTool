@echo off
rem Build CLI and run gsbt without dotnet run (dev convenience).
rem Does NOT add PATH permanently: use installer or publish for that.
cd /d "%~dp0\.."
call "%~dp0_env.bat"
if errorlevel 1 goto fail

set "CLI_PROJ=%GSBT_ROOT%\src\GSBT.Cli\GSBT.Cli.csproj"
set "CLI_BIN=%GSBT_ROOT%\src\GSBT.Cli\bin\Debug\%GSBT_TFM%\win-x64"

"%DOTNET%" build "%CLI_PROJ%" -c Debug -r win-x64 -v q
if errorlevel 1 goto fail

if not exist "%CLI_BIN%\gsbt.exe" (
    echo ERROR: gsbt.exe not found at %CLI_BIN%
    goto fail
)

"%CLI_BIN%\gsbt.exe" %*
set "RUNEXIT=%ERRORLEVEL%"
exit /b %RUNEXIT%

:fail
echo.
echo GSBT CLI script failed.
echo Working directory:
echo   %CD%
echo.
echo If you opened this by double-clicking, the window is paused so the error stays visible.
pause
exit /b 1
