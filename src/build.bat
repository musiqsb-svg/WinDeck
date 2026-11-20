@echo off
rem Double-click to build the project folder top: the two setup programs, app\
rem Windows tools are called by their full path (a file named powershell in the current folder or PATH must not run instead)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
pause
