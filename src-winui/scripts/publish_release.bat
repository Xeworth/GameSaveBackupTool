@echo off
rem Release publish for smoke tests / shipping (trim OFF: required for WinUI + JSON).
rem   scripts\publish_release.bat
rem Optional dev-only (often breaks at runtime): scripts\publish_release.bat trimmed
cd /d "%~dp0\.."
call "%~dp0_env.bat"

set "PROJ=%GSBT_ROOT%\src\GSBT.WinUI\GSBT.WinUI.csproj"
set "OUT=%GSBT_ROOT%\src\GSBT.WinUI\bin\Release\%GSBT_TFM%\win-x64\publish"

if /i "%~1"=="trimmed" (
    echo WARNING: PublishTrimmed is disabled for releases. Forcing trim ON for this experimental build only.
    echo Expect IL2026 warnings; app may crash or fail to load settings/catalog.
    "%DOTNET%" publish "%PROJ%" -c Release -r win-x64 -p:Platform=x64 -p:PublishTrimmed=true
) else (
    echo Publishing Release win-x64 ^(self-contained, trim off, English locales only^)...
    rem Remove stale publish output: incremental publishes can leave broken WinRT runtime DLLs.
    if exist "%OUT%" rd /s /q "%OUT%"
    "%DOTNET%" publish "%PROJ%" -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile=win-x64
)

if errorlevel 1 exit /b 1

rem WinApp SDK copies many locale folders; keep English only + required native/app dirs.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prune_publish_locales.ps1" -PublishDir "%OUT%"
if errorlevel 1 exit /b 1

rem WinUI unpackaged installs require the app Assets folder beside gsbt-main.exe (not only gsbt-main.pri).
set "BUILD_ASSETS=%GSBT_ROOT%\src\GSBT.WinUI\bin\x64\Release\%GSBT_TFM%\win-x64\Assets"
if exist "%BUILD_ASSETS%" (
    if not exist "%OUT%\Assets" mkdir "%OUT%\Assets"
    xcopy /E /I /Y /Q "%BUILD_ASSETS%\*" "%OUT%\Assets\" >nul
)

call "%~dp0publish_sandbox_entry.bat" "%OUT%"
if errorlevel 1 exit /b 1

call "%~dp0publish_cli_into_release.bat" "%OUT%"
if errorlevel 1 exit /b 1

rem CLI dependencies can add satellite folders after the WinUI prune.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prune_publish_locales.ps1" -PublishDir "%OUT%"
if errorlevel 1 exit /b 1

call "%~dp0pack_screensaver_publish.bat" "%OUT%"
if errorlevel 1 exit /b 1

call "%~dp0validate_publish.bat" "%OUT%"
if errorlevel 1 exit /b 1

echo.
echo Done. Run the GUI from:
echo   %OUT%\gsbt-main.exe
echo Terminal CLI:
echo   %OUT%\gsbt.exe list
echo.
echo Package portable zip: scripts\package_portable.bat
echo Build installer: installer\build_installer.bat
echo Build all release assets: scripts\package_release.bat
echo.
exit /b 0
