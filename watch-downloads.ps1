param()

$logPath = "$env:LOCALAPPDATA\DownloadTray\nativehost.log"

if (-not (Test-Path $logPath)) {
    Write-Host "Waiting for Brave to start a download..." -ForegroundColor Yellow
    while (-not (Test-Path $logPath)) {
        Start-Sleep -Milliseconds 500
    }
}

$Host.UI.RawUI.WindowTitle = "DownloadTray - Live Brave Downloads"

$activeDownloads = @{}

function Render-UI {
    Clear-Host
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "            DownloadTray - Live Brave Downloads                 " -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""

    if ($activeDownloads.Count -eq 0) {
        Write-Host "  No active downloads detected yet." -ForegroundColor DarkGray
        Write-Host ""
        return
    }

    foreach ($key in $activeDownloads.Keys) {
        $entry = $activeDownloads[$key]
        
        Write-Host "  $($entry.Icon) $($entry.Filename)" -ForegroundColor White
        
        if ($entry.RawLine -like "*%*") {
            Write-Host "    $($entry.Details)" -ForegroundColor Cyan
        } elseif ($entry.RawLine -like "*COMPLETE*") {
            Write-Host "    Completed" -ForegroundColor Green
        } else {
            Write-Host "    $($entry.Details)" -ForegroundColor DarkGray
        }
        Write-Host ""
    }

    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  Streaming live from Brave | Press Ctrl+C to exit" -ForegroundColor DarkGray
}

Get-Content -Path $logPath -Wait -Tail 50 | ForEach-Object {
    $line = $_
    
    if ($line -match '\[(.*?)\]\s+\[(.*?)\]\s+(.*?)\s+\|\s+(.*)') {
        $time     = $matches[1]
        $browser  = $matches[2]
        $filename = $matches[3].Trim()
        $details  = $matches[4].Trim()
        
        $icon = if ($details -like "*COMPLETE*") { "[DONE]" } else { "[DOWN]" }

        $activeDownloads[$filename] = @{
            Filename = $filename
            Details  = $details
            Icon     = $icon
            RawLine  = $line
            Time     = $time
        }

        Render-UI
    }
}
