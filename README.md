# DownloadTray

A lightweight Windows background utility that turns active browser downloads into a glanceable, live-updating status card. Inspired by iOS Live Activities.

## What it does

- **Detects downloads in real time** via a small Manifest V3 browser extension + Native Messaging
- **Shows live progress** — filename, percentage, downloaded/total bytes, smoothed speed, ETA
- **System tray icon** with tooltip and flyout card
- **Always-on-top activity card** that appears when a download starts
- **Windows progress notification** (best available lock-screen-adjacent experience)
- **All Chromium browsers supported**: Brave, Chrome, Edge, Opera, Opera GX, Vivaldi, Chromium

## Quick Start

### Step 1 — Build

```powershell
.\build.ps1
```

Or manually:
```powershell
$env:PATH = "C:\Users\gargp\.dotnet;" + $env:PATH
dotnet build
```

### Step 2 — Load the browser extension

1. Open `brave://extensions` (or `chrome://extensions`, `edge://extensions`)
2. Enable **Developer mode** (top-right toggle)
3. Click **Load unpacked** → select the `extension/` folder
4. Copy the **Extension ID** shown (e.g. `abcdefghijklmnopabcdefghijklmnop`)

### Step 3 — Register the native messaging host

```powershell
# Publish the native host first:
$env:PATH = "C:\Users\gargp\.dotnet;" + $env:PATH
dotnet publish src\DownloadTray.NativeHost -c Release -o publish -r win-x64 --self-contained

# Then install (registers with ALL Chromium browsers):
.\install-native-host.ps1 -ExtensionId <your-extension-id>
```

### Step 4 — Test

Start a large download in Brave (or any supported browser). The console demo will show live progress:

```
↓ Ubuntu.iso
██████████████░░░░░░░░░░░░░░░░ 47%
4.8 GB / 10.2 GB
18.4 MB/s
~4m 52s remaining
```

## Architecture

```
Browser Extension (chrome.downloads API)
    │  Native Messaging (JSON over stdio)
    ▼
DownloadTray.NativeHost.exe
    │
    ▼
DownloadTracker  (smoothed speed, ETA, percentage — no UI)
    │
    ├── System Tray (H.NotifyIcon.WinUI)
    ├── Activity Card Window (WinUI 3, always-on-top)
    └── Windows App Notification (progress bar, lock-screen adjacent)
```

## Supported browsers

| Browser | Tested |
|---------|--------|
| Brave | ✅ Primary |
| Google Chrome | ✅ |
| Microsoft Edge | ✅ |
| Opera | ✅ |
| Opera GX | ✅ |
| Vivaldi | ✅ |
| Chromium | ✅ |

Firefox is **not supported** in v1 (uses a different extension API).

## Lock screen

A true iOS-style Live Activity on the Windows lock screen is not achievable with public Windows APIs (see [BUILD-SPEC §3](live-download-activity-BUILD-SPEC.md#3-researched-answer--can-a-third-party-app-put-a-live-updating-widget-on-the-windows-lock-screen)). The closest supported alternative — a Windows App Notification with a progress bar — is implemented instead. Its visibility on the lock screen depends on the user's own notification settings in Windows (Settings → System → Notifications).

## Project structure

```
downloadTray/
├── DownloadTray.sln
├── build.ps1                     ← quick build + test
├── install-native-host.ps1       ← registers NMH for all browsers
├── extension/                    ← Manifest V3 browser extension
│   ├── manifest.json
│   └── background.js
└── src/
    ├── DownloadTray.Core/        ← Phase 1–3: models, tracking, IDownloadSource
    │   ├── Models/               ← DownloadActivity, DownloadState
    │   ├── Sources/              ← IDownloadSource, ChromiumExtensionDownloadSource
    │   └── DownloadTracker.cs    ← smoothed speed, ETA, percentage
    ├── DownloadTray.NativeHost/  ← registered exe that talks to the extension
    ├── DownloadTray.ConsoleDemo/ ← Phase 1/2/3 live terminal dashboard
    └── DownloadTray.App/         ← Phase 4+: WinUI 3 tray + card + notifications
        ├── ActivityCard/
        ├── Services/
        └── Styles/
```

## Build phases

Per the spec, build in order:

- [x] **Phase 0** — Research (see spec §3/4)
- [x] **Phase 1** — Detection + console output (NativeHost + ConsoleDemo)
- [x] **Phase 2** — Live tracking (DownloadTracker: %, speed, ETA)
- [x] **Phase 3** — Model separation (IDownloadSource abstraction)
- [x] **Phase 4** — Basic UI (ActivityCardWindow, TrayIconService)
- [x] **Phase 5** — Multiple downloads (sort by spec §2.3)
- [x] **Phase 6** — Windows App Notification (NotificationService)
- [ ] **Phase 7** — StartupTask registration
- [ ] **Phase 8** — MSIX packaging + extension store listing
- [ ] **Phase 9** — UX polish (animations, dark/light mode, accessibility)
