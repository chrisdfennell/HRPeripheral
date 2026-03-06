@echo off
REM ===================================================================
REM This batch file launches the builddeploy.ps1 PowerShell script.
REM It ensures that the script has the necessary execution policy
REM to run correctly.
REM
REM You can run this by simply double-clicking it.
REM ===================================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0builddeploy.ps1"

pause