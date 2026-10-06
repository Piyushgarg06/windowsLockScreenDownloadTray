@echo off
setlocal enabledelayedexpansion
title DownloadTray Installer
color 0b

echo.
echo  ================================================================
echo             DownloadTray -- Live Download Activity
echo  ================================================================
echo.
echo  Installing DownloadTray for Windows (Brave, Chrome, Edge, Opera)...
echo.

set "INSTALL_DIR=%LOCALAPPDATA%\DownloadTray"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo  [1/4] Installing application files...
copy /Y "%~dp0DownloadTray.exe" "%INSTALL_DIR%\DownloadTray.exe" >nul

echo  [2/4] Registering auto-start on Windows boot...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "DownloadTray" /t REG_SZ /d "\"%INSTALL_DIR%\DownloadTray.exe\"" /f >nul

echo  [3/4] Registering with Chromium browsers...
start "" "%INSTALL_DIR%\DownloadTray.exe"

echo  [4/4] Creating Desktop Shortcut...
powershell -NoProfile -Command "$wsh = New-Object -ComObject WScript.Shell; $s = $wsh.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\DownloadTray.lnk'); $s.TargetPath = '%INSTALL_DIR%\DownloadTray.exe'; $s.WorkingDirectory = '%INSTALL_DIR%'; $s.Save()"

echo.
echo  ================================================================
echo   Installation Successful!
echo.
echo   DownloadTray is now running in your Windows System Tray.
echo   It will automatically start whenever you turn on your PC.
echo.
echo   FINAL STEP: Connect to your browser (one-time setup)
echo   1. Turn ON 'Developer mode' in your browser extensions page
echo   2. Click 'Load unpacked'
echo   3. Select the 'extension' folder inside this downloaded folder
echo  ================================================================
echo.
pause
