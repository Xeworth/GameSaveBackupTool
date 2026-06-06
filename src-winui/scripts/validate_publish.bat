@echo off
rem Validates publish\ before packaging the installer.
rem Usage: validate_publish.bat [publish-dir]
setlocal EnableDelayedExpansion

set "OUT=%~1"
if "%OUT%"=="" (
    echo ERROR: validate_publish.bat requires a publish directory argument.
    exit /b 1
)

set "ERR=0"
call :require_file "%OUT%\gsbt.exe" "gsbt.exe"
call :require_file "%OUT%\gsbt.pri" "gsbt.pri"
call :require_file "%OUT%\Assets\StoreLogo.png" "Assets\StoreLogo.png"
call :require_file "%OUT%\branding\gsbt.ico" "branding\gsbt.ico"
call :require_file "%OUT%\WinRT.Runtime.dll" "WinRT.Runtime.dll"
call :require_min_size "%OUT%\System.Runtime.InteropServices.dll" 90000 "System.Runtime.InteropServices.dll"
call :require_min_size "%OUT%\WinRT.Runtime.dll" 500000 "WinRT.Runtime.dll"

call :require_file "%OUT%\gsbt-sandbox.exe" "gsbt-sandbox.exe"
call :require_file "%OUT%\gsbt-sandbox.pri" "gsbt-sandbox.pri"

if "%ERR%"=="1" (
    echo.
    echo Publish validation failed. Run scripts\clean.bat then scripts\publish_release.bat.
    exit /b 1
)

for /f "usebackq delims=" %%S in (`powershell -NoProfile -Command ^
  "$s=(Get-ChildItem -LiteralPath '%OUT%' -Recurse -File ^| Measure-Object -Property Length -Sum).Sum; [math]::Round($s/1MB,1)"`) do set "PUBLISH_MB=%%S"

echo Publish validation OK ^(~!PUBLISH_MB! MB in %OUT%^).
exit /b 0

:require_file
if not exist "%~1" (
    echo ERROR: Missing %~2
    set "ERR=1"
)
exit /b 0

:require_min_size
if not exist "%~1" (
    echo ERROR: Missing %~3
    set "ERR=1"
    exit /b 0
)
for %%A in ("%~1") do set "SZ=%%~zA"
if !SZ! LSS %~2 (
    echo ERROR: %~3 is too small ^(!SZ! bytes^). Stale publish output can break WinRT startup.
    set "ERR=1"
)
exit /b 0
