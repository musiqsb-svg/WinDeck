@echo off
rem Double-click to build the release attachments into src\release (package zip, setup, portable zip, SHA-256 list)
rem Windows tools are called by their full path (a file named powershell in the current folder or PATH must not run instead)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
pause
