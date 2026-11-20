@echo off
REM Double-click this file to install Ditechs PC Check.
REM This INSTALLER asks for administrator rights (it writes to Program Files).
REM The tool itself never does - it only reads, and never asks for permission.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DitechsPcCheck.ps1" %*
if errorlevel 1 (
  echo.
  echo Installation reported an error. See the message above.
  pause
)
