@echo off
rem Builds Firmly.Service and runs it in an elevated console, for trying it against real hardware.
rem   tools\start-service.cmd [Debug^|Release]
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
if /i not "%CONFIG%"=="Debug" if /i not "%CONFIG%"=="Release" (
    echo Usage: %~nx0 [Debug^|Release]
    exit /b 1
)
set "ROOT=%~dp0.."
set "EXE=%ROOT%\src\Firmly.Service\bin\%CONFIG%\net10.0-windows\Firmly.Service.exe"

if /i "%~2"=="--elevated" goto run

rem A running dev service locks its bin folder, so the build below would fail. An installed Firmly's service
rem runs the same exe name, but from its package; that one is stopped below while this one runs.
sc query Firmly 2>nul | find "RUNNING" >nul
if not errorlevel 1 goto build
tasklist /fi "imagename eq Firmly.Service.exe" | find /i "Firmly.Service.exe" >nul
if not errorlevel 1 (
    echo Firmly.Service is already running. Stop it ^(Ctrl+C in its window^) first.
    exit /b 1
)

rem Build before elevating, so the build output stays owned by you.
:build
dotnet build "%ROOT%\src\Firmly.Service" -c %CONFIG% -v q -nologo || exit /b 1

net session >nul 2>&1
if not errorlevel 1 goto run

pwsh -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0' -ArgumentList '%CONFIG%','--elevated'"
exit /b

:run
title Firmly.Service (%CONFIG%)
rem Only one service can own the pipe: pause an installed Firmly's service while this one runs.
set "RESTART_INSTALLED="
sc query Firmly 2>nul | find "RUNNING" >nul
if not errorlevel 1 (
    echo Stopping the installed Firmly service while this one runs. When you stop this one with Ctrl+C,
    echo answer N to "Terminate batch job" so the installed service starts again.
    net stop Firmly >nul || (pause & exit /b 1)
    set "RESTART_INSTALLED=1"
)
set "DOTNET_ENVIRONMENT=Development"
"%EXE%"
if defined RESTART_INSTALLED (
    echo Starting the installed Firmly service again.
    net start Firmly >nul
)
rem Keep the elevated window open to read the output after the service stops.
if /i "%~2"=="--elevated" pause
