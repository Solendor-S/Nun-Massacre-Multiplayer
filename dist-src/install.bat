@echo off
rem Double-click me. Runs install.ps1 for this one run without changing Windows' script policy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
