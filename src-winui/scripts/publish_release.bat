@echo off
rem Release publish for smoke tests / shipping (trim OFF — required for WinUI + JSON).
rem   scripts\publish_release.bat
rem Optional dev-only (often breaks at runtime): scripts\publish_release.bat trimmed
cd /d "%~dp0\.."

set "PROJ=%CD%\src\GSBT.WinUI\GSBT.WinUI.csproj"
set "OUT=%CD%\src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

if /i "%~1"=="trimmed" (
    echo WARNING: PublishTrimmed is disabled for releases. Forcing trim ON for this experimental build only.
    echo Expect IL2026 warnings; app may crash or fail to load settings/catalog.
    dotnet publish "%PROJ%" -c Release -r win-x64 -p:Platform=x64 -p:PublishTrimmed=true
) else (
    echo Publishing Release win-x64 ^(self-contained, trim off, English satellites only^)...
    rem Remove stale publish output — incremental publishes can leave broken WinRT runtime DLLs.
    if exist "%OUT%" rd /s /q "%OUT%"
    dotnet publish "%PROJ%" -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile=win-x64
)

if errorlevel 1 exit /b 1

rem WinApp SDK may still copy many locale folders; keep English (en-us) + app folders only.
powershell -NoProfile -Command ^
  "$out='%OUT%'; $keep=@('en-us','branding','data','Microsoft.UI.Xaml','NpuDetect','Assets'); Get-ChildItem -LiteralPath $out -Directory | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"

rem WinUI unpackaged installs require the app Assets folder beside gsbt.exe (not only gsbt.pri).
set "BUILD_ASSETS=%CD%\src\GSBT.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Assets"
if exist "%BUILD_ASSETS%" (
    if not exist "%OUT%\Assets" mkdir "%OUT%\Assets"
    xcopy /E /I /Y /Q "%BUILD_ASSETS%\*" "%OUT%\Assets\" >nul
)

call "%~dp0publish_sandbox_entry.bat" "%OUT%"
if errorlevel 1 exit /b 1

call "%~dp0validate_publish.bat" "%OUT%"
if errorlevel 1 exit /b 1

echo.
echo Done. Run the app from:
echo   %OUT%\gsbt.exe
echo.
echo Package portable zip: scripts\package_portable.bat
echo Build installer: installer\build_installer.bat
echo Build all release assets: scripts\package_release.bat
echo.
exit /b 0
