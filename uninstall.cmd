@echo off
setlocal enabledelayedexpansion
title Ohman Uninstaller

echo =======================================================
echo              Ohman Uninstallation
echo =======================================================
echo.

:: 1. Check for administrative privileges and elevate if needed
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [i] Requesting Administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs" >nul 2>&1
    if %errorLevel% neq 0 (
        echo [!] Could not automatically prompt for Administrator elevation.
        echo     Please right-click 'uninstall.cmd' and select 'Run as administrator'.
        echo.
        pause
    )
    exit /b
)

:: 2. Stop running instances
echo [1/4] Stopping Ohman...
taskkill /F /IM Ohman.exe >nul 2>&1
timeout /t 1 /nobreak >nul

:: 3. Remove scheduled task
echo [2/4] Removing autostart task...
schtasks /Delete /TN "Ohman" /F >nul 2>&1

:: 4. Remove shortcuts
echo [3/4] Removing shortcuts...
del /F /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Ohman.lnk" >nul 2>&1
del /F /Q "%USERPROFILE%\Desktop\Ohman.lnk" >nul 2>&1

:: 5. Remove program files
echo [4/4] Removing installed files...
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\Ohman"
if exist "%INSTALL_DIR%" (
    rmdir /S /Q "%INSTALL_DIR%" >nul 2>&1
)

echo.
echo =======================================================
echo        Ohman was successfully uninstalled!
echo =======================================================
echo.
timeout /t 3 /nobreak >nul
exit /b 0
