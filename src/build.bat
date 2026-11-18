@echo off
rem Double-click to build the project folder top: the two setup programs, app\
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
pause
