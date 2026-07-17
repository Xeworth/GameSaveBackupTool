@echo off
rem Publish the standalone terminal CLI payload.
rem Output:
rem   src\GSBT.Cli\bin\Release\<tfm>\win-x64\publish\gsbt.exe
setlocal

call "%~dp0_env.bat"
if errorlevel 1 goto fail

set "CLI_PROJ=%GSBT_ROOT%\src\GSBT.Cli\GSBT.Cli.csproj"
set "CLI_OUT=%GSBT_ROOT%\src\GSBT.Cli\bin\Release\%GSBT_TFM%\win-x64\publish"

echo Publishing GSBT CLI win-x64 ^(self-contained^)...
"%DOTNET%" publish "%CLI_PROJ%" -c Release -r win-x64 -p:SelfContained=true
if errorlevel 1 goto fail

if not exist "%CLI_OUT%\gsbt.exe" (
    echo ERROR: CLI publish missing gsbt.exe
    goto fail
)
if not exist "%CLI_OUT%\7z.dll" (
    echo ERROR: CLI publish missing 7z.dll
    goto fail
)
if not exist "%CLI_OUT%\data\ludusavi-save-manifest.json" (
    echo ERROR: CLI publish missing data\ludusavi-save-manifest.json
    goto fail
)

echo CLI publish OK:
echo   %CLI_OUT%
exit /b 0

:fail
echo.
echo GSBT CLI publish failed.
echo Working directory:
echo   %CD%
echo.
echo If you opened this by double-clicking, the window is paused so the error stays visible.
pause
exit /b 1
