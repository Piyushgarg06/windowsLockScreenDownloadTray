#!/usr/bin/env pwsh
##############################################################################
# DownloadTray — Quick Build Script
# Builds Core, NativeHost, ConsoleDemo, and Tests.
# Does NOT build DownloadTray.App (WinUI 3) — that requires Windows App SDK.
##############################################################################

$ErrorActionPreference = "Stop"
$dotnet = "C:\Users\gargp\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$root = $PSScriptRoot

Write-Host "Building DownloadTray.Core..." -ForegroundColor Cyan
& $dotnet build "$root\src\DownloadTray.Core\DownloadTray.Core.csproj" -c Release --nologo

Write-Host "Building DownloadTray.NativeHost..." -ForegroundColor Cyan
& $dotnet build "$root\src\DownloadTray.NativeHost\DownloadTray.NativeHost.csproj" -c Release --nologo

Write-Host "Building DownloadTray.ConsoleDemo..." -ForegroundColor Cyan
& $dotnet build "$root\src\DownloadTray.ConsoleDemo\DownloadTray.ConsoleDemo.csproj" -c Release --nologo

Write-Host "Running tests..." -ForegroundColor Cyan
& $dotnet test "$root\tests\DownloadTray.Tests\DownloadTray.Tests.csproj" --nologo --logger "console;verbosity=normal"

Write-Host ""
Write-Host "All builds succeeded." -ForegroundColor Green
Write-Host ""
Write-Host "To publish the native host (self-contained exe):" -ForegroundColor Yellow
Write-Host "  dotnet publish src\DownloadTray.NativeHost -c Release -o publish -r win-x64 --self-contained"
Write-Host ""
Write-Host "Then run:" -ForegroundColor Yellow
Write-Host "  .\install-native-host.ps1"
