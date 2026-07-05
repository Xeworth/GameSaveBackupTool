@echo off
cd /d "%~dp0\.."
call "%~dp0_env.bat"

rem Fast launcher: no build, run last compiled WinUI exe.
rem   scripts\launch_fast.bat
rem   scripts\launch_fast.bat sandbox

set "ROOT=%GSBT_ROOT%"
set "EXE=%ROOT%\src\GSBT.WinUI\bin\x64\Debug\%GSBT_TFM%\win-x64\gsbt-main.exe"
set "ARGS="

if /i "%~1"=="sandbox" set "ARGS=-s"

if not exist "%EXE%" goto :nofile

echo Launching (no build): %EXE%
"%EXE%" %ARGS%
exit /b %ERRORLEVEL%

:nofile
echo ERROR: Exe not found:
echo   %EXE%
echo.
echo Build once first:
echo   scripts\launch.bat
echo   or: dotnet build "src\GSBT.WinUI\GSBT.WinUI.csproj" -c Debug -r win-x64 -p:Platform=x64
pause
exit /b 1
