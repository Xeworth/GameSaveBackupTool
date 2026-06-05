@echo off
cd /d "%~dp0\.."

rem Build and run WinUI with sandbox monitor + main window (-s).
rem Same as: scripts\launch.bat sandbox

call "%~dp0launch.bat" sandbox
