param(
    [string]$ExtensionId = "",
    [string]$ExePath = ""
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Determine exe path
# ---------------------------------------------------------------------------
if (-not $ExePath) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $candidate1 = Join-Path $scriptDir "bin_publish\DownloadTray.NativeHost.exe"
    $candidate2 = Join-Path $scriptDir "publish\DownloadTray.NativeHost.exe"
    if (Test-Path $candidate1) {
        $ExePath = $candidate1
    } elseif (Test-Path $candidate2) {
        $ExePath = $candidate2
    } else {
        Write-Error "Could not find DownloadTray.NativeHost.exe. Build it first."
        exit 1
    }
}

$ExePath = (Resolve-Path $ExePath).Path
Write-Host "Using exe: $ExePath" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Determine extension ID
# ---------------------------------------------------------------------------
if (-not $ExtensionId) {
    Write-Host ""
    Write-Host "Extension ID is required. To find it:" -ForegroundColor Yellow
    Write-Host "  1. Open Brave and go to brave://extensions"
    Write-Host "  2. Enable Developer mode (top-right toggle)"
    Write-Host "  3. Click Load unpacked and select the extension folder"
    Write-Host "  4. Copy the ID shown under DownloadTray"
    Write-Host ""
    $ExtensionId = Read-Host "Enter extension ID"
}

if ($ExtensionId -notmatch '^[a-p]{32}$') {
    Write-Warning "Extension ID '$ExtensionId' looks unusual (expected 32 lowercase letters a-p)."
    Write-Warning "Continuing anyway."
}

$AllowedOrigin = "chrome-extension://$ExtensionId/"
Write-Host "Allowed origin: $AllowedOrigin" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Write the native messaging host manifest JSON
# ---------------------------------------------------------------------------
$manifestDir = Join-Path $env:LOCALAPPDATA "DownloadTray\NativeMessagingHosts"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifestPath = Join-Path $manifestDir "com.downloadtray.nativehost.json"

$exeEscaped = $ExePath.Replace("\", "\\")
$manifestJson = "{`"name`":`"com.downloadtray.nativehost`",`"description`":`"DownloadTray Native Messaging Host`",`"path`":`"$exeEscaped`",`"type`":`"stdio`",`"allowed_origins`":[`"$AllowedOrigin`"]}"

Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
Write-Host "Manifest written to: $manifestPath" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Register in registry for every Chromium browser
# ---------------------------------------------------------------------------
$registryKey = "com.downloadtray.nativehost"

$browsers = @(
    @{ Name = "Chrome";    Key = "Software\Google\Chrome\NativeMessagingHosts\$registryKey" },
    @{ Name = "Edge";      Key = "Software\Microsoft\Edge\NativeMessagingHosts\$registryKey" },
    @{ Name = "Brave";     Key = "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\$registryKey" },
    @{ Name = "Opera";     Key = "Software\Opera Software\Opera Stable\NativeMessagingHosts\$registryKey" },
    @{ Name = "Opera GX";  Key = "Software\Opera Software\Opera GX Stable\NativeMessagingHosts\$registryKey" },
    @{ Name = "Vivaldi";   Key = "Software\Vivaldi\NativeMessagingHosts\$registryKey" },
    @{ Name = "Chromium";  Key = "Software\Chromium\NativeMessagingHosts\$registryKey" }
)

Write-Host ""
Write-Host "Registering for all Chromium browsers..." -ForegroundColor Cyan

foreach ($b in $browsers) {
    try {
        $fullKey = "HKCU:\$($b.Key)"
        New-Item -Path $fullKey -Force | Out-Null
        Set-ItemProperty -Path $fullKey -Name "(Default)" -Value $manifestPath -Type String -Force
        Write-Host "  OK $($b.Name)" -ForegroundColor Green
    } catch {
        Write-Warning "  SKIP $($b.Name) - $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Brave (close all windows, reopen)"
Write-Host "  2. Start a large file download in Brave"
Write-Host "  3. Open brave://extensions -> DownloadTray -> Service Worker console"
Write-Host "  4. You should see: Synced 1 in-progress download"
Write-Host ""
