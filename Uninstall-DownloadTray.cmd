@echo off
title DownloadTray Uninstaller
color 0c

echo.
echo  ================================================================
echo             DownloadTray -- Complete Uninstaller
echo  ================================================================
echo.
echo  Removing DownloadTray from your system...
echo.

echo  [1/5] Stopping running processes...
taskkill /F /IM DownloadTray.exe /T >nul 2>&1
taskkill /F /IM DownloadTray.NativeHost.exe /T >nul 2>&1

echo  [2/5] Removing Windows Startup auto-boot entry...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "DownloadTray" /f >nul 2>&1

echo  [3/5] Unregistering from Chromium browsers...
set "HOST_KEY=com.downloadtray.nativehost"
reg delete "HKCU\Software\Google\Chrome\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Edge\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Opera Software\Opera Stable\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Opera Software\Opera GX Stable\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Vivaldi\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Chromium\NativeMessagingHosts\%HOST_KEY%" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\DownloadTray" /f >nul 2>&1
reg delete "HKCU\Software\Classes\AppUserModelId\DownloadTray" /f >nul 2>&1

echo  [4/5] Removing shortcuts...
del /F /Q "%USERPROFILE%\Desktop\DownloadTray.lnk" >nul 2>&1
del /F /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\DownloadTray.lnk" >nul 2>&1

echo  [5/5] Deleting application files...
rmdir /S /Q "%LOCALAPPDATA%\DownloadTray" >nul 2>&1

echo.
echo  ================================================================
echo   Uninstallation Complete!
echo.
echo   To remove the browser extension:
echo   Open brave://extensions and click 'Remove' on DownloadTray.
echo  ================================================================
echo.
pause
