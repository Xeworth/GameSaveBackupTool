@echo off
cd /d "%~dp0\.."

rem Build then run the unpackaged exe (good default for WinUI).
rem   scripts\launch.bat
rem   scripts\launch.bat sandbox
rem   scripts\launch.bat nopause     - no pause after failed build
rem From src-winui/ you can also use: launch.bat (wrapper)

set "ROOT=%CD%"
set "LOGDIR=%ROOT%\logs"
set "LOG=%LOGDIR%\launch-last.txt"
set "PROJ=%ROOT%\src\GSBT.WinUI\GSBT.WinUI.csproj"
set "EXE=%ROOT%\src\GSBT.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\gsbt.exe"

set "ARGS="
set "PAUSE_ON_FAIL=1"

if /i "%~1"=="sandbox" set "ARGS=-s"
if /i "%~1"=="nopause" set "PAUSE_ON_FAIL=0"
if /i "%~2"=="sandbox" set "ARGS=-s"
if /i "%~2"=="nopause" set "PAUSE_ON_FAIL=0"

if not exist "%LOGDIR%" mkdir "%LOGDIR%"

echo ======================================== > "%LOG%"
echo GSBT WinUI launch log >> "%LOG%"
echo Repo: %ROOT% >> "%LOG%"
echo Exe args: %ARGS% >> "%LOG%"
echo ======================================== >> "%LOG%"
echo. >> "%LOG%"

dotnet --version >> "%LOG%" 2>&1
echo. >> "%LOG%"
dotnet build "%PROJ%" -c Debug -r win-x64 -p:Platform=x64 -v minimal >> "%LOG%" 2>&1
if errorlevel 1 goto :buildfail

if not exist "%EXE%" goto :nofile

echo. >> "%LOG%"
echo Running exe >> "%LOG%"
"%EXE%" %ARGS%
set RUNEXIT=%ERRORLEVEL%
echo Process exit code: %RUNEXIT% >> "%LOG%"

echo.
echo Build and run log:
echo   %LOG%
echo.
echo Crash dumps if the window closes immediately:
echo   %%LOCALAPPDATA%%\GSBT\winui_last_error.txt
echo   %%TEMP%%\gsbt_winui_last_error.txt
echo.

exit /b %RUNEXIT%

:buildfail
echo.
echo BUILD FAILED - full log:
echo   %LOG%
echo.
type "%LOG%"
if "%PAUSE_ON_FAIL%"=="1" pause
exit /b 1

:nofile
echo ERROR: Exe not found:
echo   %EXE%
echo Edit EXE= in scripts\launch.bat if your build layout differs.
echo Log: %LOG%
if "%PAUSE_ON_FAIL%"=="1" pause
exit /b 1
