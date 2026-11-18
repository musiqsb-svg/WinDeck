@echo off
rem Double-click to build the release attachments into src\release (package zip, setup, portable zip, SHA-256 list)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
pause
