@echo off
rem Create gsbt-sandbox.exe (apphost copy + gsbt-s.ico) and gsbt-sandbox.pri beside gsbt.exe.
rem Usage: publish_sandbox_entry.bat [publish-dir]
setlocal EnableDelayedExpansion

set "OUT=%~1"
if "%OUT%"=="" (
    echo ERROR: publish_sandbox_entry.bat requires a publish directory argument.
    exit /b 1
)

set "SCRIPTS=%~dp0"
set "RCEDIT=%SCRIPTS%tools\rcedit-x64.exe"
set "MAIN_EXE=%OUT%\gsbt.exe"
set "MAIN_PRI=%OUT%\gsbt.pri"
set "SANDBOX_EXE=%OUT%\gsbt-sandbox.exe"
set "SANDBOX_PRI=%OUT%\gsbt-sandbox.pri"
set "SANDBOX_ICO=%OUT%\branding\gsbt-s.ico"

if not exist "%MAIN_EXE%" (
    echo ERROR: Missing %MAIN_EXE%
    exit /b 1
)
if not exist "%MAIN_PRI%" (
    echo ERROR: Missing %MAIN_PRI%
    exit /b 1
)
if not exist "%SANDBOX_ICO%" (
    echo ERROR: Missing %SANDBOX_ICO%
    exit /b 1
)

if not exist "%RCEDIT%" (
    echo Downloading rcedit for sandbox icon embedding...
    if not exist "%SCRIPTS%tools" mkdir "%SCRIPTS%tools"
    powershell -NoProfile -Command ^
      "$u='https://github.com/electron/rcedit/releases/download/v2.0.0/rcedit-x64.exe';" ^
      "Invoke-WebRequest -Uri $u -OutFile '%RCEDIT%' -UseBasicParsing"
    if not exist "%RCEDIT%" (
        echo ERROR: Could not download rcedit-x64.exe
        exit /b 1
    )
)

if exist "%SANDBOX_EXE%" del /f /q "%SANDBOX_EXE%"
if exist "%SANDBOX_PRI%" del /f /q "%SANDBOX_PRI%"

copy /b /y "%MAIN_EXE%" "%SANDBOX_EXE%" >nul
if not exist "%SANDBOX_EXE%" (
    echo ERROR: Could not copy gsbt-sandbox.exe
    exit /b 1
)

"%RCEDIT%" "%SANDBOX_EXE%" --set-icon "%SANDBOX_ICO%"
if errorlevel 1 (
    echo ERROR: rcedit failed to set gsbt-s.ico on gsbt-sandbox.exe
    del /f /q "%SANDBOX_EXE%"
    exit /b 1
)

cmd /c mklink /H "%SANDBOX_PRI%" "%MAIN_PRI%" >nul 2>&1
if not exist "%SANDBOX_PRI%" copy /b /y "%MAIN_PRI%" "%SANDBOX_PRI%" >nul
if not exist "%SANDBOX_PRI%" (
    echo ERROR: Could not create gsbt-sandbox.pri
    del /f /q "%SANDBOX_EXE%"
    exit /b 1
)

echo Sandbox entry OK in %OUT% ^(gsbt-s.ico embedded in gsbt-sandbox.exe^)
exit /b 0
