@echo off
rem Remove local build/publish/installer artifacts (already gitignored).
cd /d "%~dp0\.."

echo Cleaning .NET build outputs...
dotnet clean "%CD%\GSBT.sln" -c Debug -v q 2>nul
dotnet clean "%CD%\GSBT.sln" -c Release -v q 2>nul

if exist "%CD%\src\GSBT.WinUI\bin" (
    echo Removing GSBT.WinUI\bin ...
    rd /s /q "%CD%\src\GSBT.WinUI\bin"
)
if exist "%CD%\src\GSBT.WinUI\obj" (
    echo Removing GSBT.WinUI\obj ...
    rd /s /q "%CD%\src\GSBT.WinUI\obj"
)
if exist "%CD%\src\GSBT.Core\bin" rd /s /q "%CD%\src\GSBT.Core\bin"
if exist "%CD%\src\GSBT.Core\obj" rd /s /q "%CD%\src\GSBT.Core\obj"
if exist "%CD%\tests\GSBT.Core.Tests\bin" rd /s /q "%CD%\tests\GSBT.Core.Tests\bin"
if exist "%CD%\tests\GSBT.Core.Tests\obj" rd /s /q "%CD%\tests\GSBT.Core.Tests\obj"
if exist "%CD%\installer\output" (
    echo Removing installer\output ...
    rd /s /q "%CD%\installer\output"
)

echo Done. Source and docs were not removed.
exit /b 0
