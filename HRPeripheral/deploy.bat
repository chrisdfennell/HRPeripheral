@echo off
REM This batch file runs the deploy-only PowerShell script.
REM It skips the build process and installs the latest existing APK.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\deploy.ps1"

pause