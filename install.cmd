@echo off
setlocal enabledelayedexpansion
title OMI Installer

echo =======================================================
echo               OMI Installation Wizard
echo =======================================================
echo.

:: 1. Check for administrative privileges and elevate if needed
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [i] Requesting Administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs" >nul 2>&1
    if %errorLevel% neq 0 (
        echo [!] Could not automatically prompt for Administrator elevation.
        echo     Please right-click 'install.cmd' and select 'Run as administrator'.
        echo.
        pause
    )
    exit /b
)

cd /d "%~dp0"

:: 2. Locate OMI.exe
if not exist "OMI.exe" (
    echo [X] Error: 'OMI.exe' was not found in this folder!
    echo     Please make sure 'OMI.exe' and 'install.cmd' are in the same directory.
    echo.
    pause
    exit /b 1
)

:: 3. Stop running instances if any
echo [1/5] Stopping any running instances...
taskkill /F /IM OMI.exe >nul 2>&1
timeout /t 1 /nobreak >nul

:: 4. Set installation directory
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\OMI"
set "TARGET_EXE=%INSTALL_DIR%\OMI.exe"

echo [2/5] Installing to: %INSTALL_DIR%
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /Y "OMI.exe" "%TARGET_EXE%" >nul
if %errorLevel% neq 0 (
    echo [X] Failed to copy %SOURCE_EXE% to target directory.
    pause
    exit /b 1
)

:: 5. Create Shortcuts (Start Menu & Desktop)
echo [3/5] Creating Start Menu and Desktop shortcuts...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ws = New-Object -ComObject WScript.Shell; " ^
    "$sMenu = [System.IO.Path]::Combine($env:APPDATA, 'Microsoft\Windows\Start Menu\Programs\OMI.lnk'); " ^
    "$sc1 = $ws.CreateShortcut($sMenu); $sc1.TargetPath = '%TARGET_EXE%'; $sc1.WorkingDirectory = '%INSTALL_DIR%'; $sc1.Description = 'OMI Hardware Control'; $sc1.Save(); " ^
    "$desk = [System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'OMI.lnk'); " ^
    "$sc2 = $ws.CreateShortcut($desk); $sc2.TargetPath = '%TARGET_EXE%'; $sc2.WorkingDirectory = '%INSTALL_DIR%'; $sc2.Description = 'OMI Hardware Control'; $sc2.Save();"

:: 6. Register logon autostart task (HighestAvailable privileges, no UAC prompts on boot)
echo [4/5] Registering logon autostart task...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$exe = '%TARGET_EXE%'; " ^
    "$action = New-ScheduledTaskAction -Execute $exe -Argument '--hidden'; " ^
    "$trigger = New-ScheduledTaskTrigger -AtLogOn; " ^
    "$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest; " ^
    "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0; " ^
    "try { Register-ScheduledTask -TaskName 'OMI' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null } catch {}"

:: 7. Launch OMI
echo [5/5] Launching OMI...
start "" "%TARGET_EXE%"

echo.
echo =======================================================
echo          Installation Completed Successfully!
echo =======================================================
echo.
echo - Installed to: %INSTALL_DIR%
echo - Start Menu and Desktop shortcuts created.
echo - Autostart on boot configured (runs elevated without UAC prompts).
echo - OMI is now running in your system tray.
echo.
timeout /t 3 /nobreak >nul
exit /b 0
