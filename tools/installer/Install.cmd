@echo off
rem Runs Install.ps1 whatever the PowerShell execution policy; double-click to install Firmly.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
