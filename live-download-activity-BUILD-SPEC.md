# Windows Live Download Activity — Master Build Specification (v1.0)

**Status:** Ready for implementation, starting at Phase 1 (research questions in Phase 0 are already answered below).
**Audience:** The engineer/agent writing the code. Treat this document as the single source of truth. Do not substitute your own assumptions for anything stated here. If something genuinely isn't covered, stop and ask rather than guessing.

---

## 0. How to use this document

This replaces the original brainstorm document. It contains the same product intent, but:

- Section 3 answers the Phase 0 "is the Lock Screen possible?" research question with a concrete, evidence-based conclusion, so you do **not** need to re-research it from scratch.
- Section 4 answers the "how do we detect downloads?" question with a concrete recommended approach.
- Everything else is written to be unambiguous enough to implement without guessing.

Build in the phase order given in Section 14. Do not jump ahead to polish or to the Lock Screen work before Phases 1–3 work end-to-end in a console app.

---

## 1. Product definition (do not scope-creep beyond this)

**One sentence:** A lightweight Windows background utility that turns active browser downloads into a glanceable, live-updating status card, so the user can check download progress without opening the browser and, where technically possible, without unlocking the PC.

**Inspiration model:** iOS/iPadOS "Live Activities." **Not** a download manager.

**Explicitly out of scope for the MVP — do not build these, even if they seem easy or "nice to add":**
- Pause / resume / cancel of downloads
- Download history or a list of past downloads
- File management (open folder, rename, delete, move)
- Browser navigation or browser control beyond reading status
- Any account system, login, or user profile
- Any cloud backend, remote database, or telemetry/analytics service
- A settings dashboard beyond the minimum needed to configure startup and lock-screen permission prompts
- Support for more than one browser in v1 (see Section 4)

If a task isn't explicitly requested in this document, do not add it "for completeness."

---

## 2. Exact user experience (verbatim reference — build to match these, not your own visual instincts)

### 2.1 Core loop
Look at the screen → understand status → go back to whatever else you were doing. No clicking required to get the information.

### 2.2 Reference states

**Active, size known:**
```
↓ Ubuntu.iso
64%
4.2 GB / 6.5 GB
18.2 MB/s
~2m remaining
```

**Just started:**
```
↓ Ubuntu.iso
Starting...
0%
```

**Active, size unknown (never fabricate a percentage in this case):**
```
↓ file.zip
2.4 GB downloaded
14.1 MB/s
Downloading...
```

**Completed:**
```
✓ Ubuntu.iso
Download complete
```
This state auto-dismisses after a short period (default: 8 seconds) or transitions to a "completed" visual style, depending on which UI surface is displaying it (see Section 9).

**Multiple simultaneous downloads**, most relevant one first:
```
↓ Satellite Dataset       37%
↓ PyTorch                  81%
↓ Ubuntu ISO               12%
```

Sort order for "most relevant": (1) anything actively downloading beats anything paused/stalled, (2) among active downloads, most recently started first, (3) tie-break by largest total size. Keep this logic simple — do not build a scoring system.

### 2.3 Rules that must never be violated
- Never show a fabricated percentage when `totalBytes` is unknown.
- Never show an ETA when speed is `0` or unknown — show `Calculating...` instead, not `~9999 hours`.
- Round percentages to whole numbers only (`37%`, never `37.18%`).
- Speed must be smoothed (see Section 7.2) — never show raw instantaneous jitter.

---

## 3. RESEARCHED ANSWER — Can a third-party app put a live, updating widget on the Windows Lock Screen?

This is the single most important open question from the original brief. Here is the evidence-based answer, current as of research done for this document (Windows 11, 2026).

### 3.1 Short answer
**PARTIALLY POSSIBLE, but not in the form the product idea originally envisioned.** There is no public, current, supported Windows API that lets an arbitrary third-party app draw a persistent, richly-formatted, live-updating card (with a progress bar, multiple lines, live percentage ticking every second) directly on the Lock Screen the way iOS Live Activities work on the iPhone lock screen. Build the closest supported alternative instead, described in 3.4.

### 3.2 What does exist, and why it doesn't solve this
There is an old capability, dating back to Windows 8/10, called **"Lock screen apps"**: a packaged (UWP/MSIX) app can declare a `LockScreen` element in its app manifest and ask the user (via a system-provided consent dialog) to be added as either:
- a **"quick status" app**: a small badge icon plus a number or glyph, shown in a row on the lock screen, or
- the single **"detailed status" app**: one line of plain text, shown larger, with no images.

Two structural facts make this unsuitable as the foundation of this product:

1. **It only ever mirrors "Live Tile" content.** The detailed-status text on the lock screen is literally just the text portion of the app's Start-menu Live Tile notification. Windows 11 removed Live Tiles from the Start menu entirely; the tile/badge notification plumbing behind this feature is legacy infrastructure that Microsoft has been quietly retiring, not a currently invested-in surface. Building new product functionality on top of it is building on a foundation Microsoft has already moved away from.
2. **Only one app total can hold the "detailed status" slot, and the user must manually pick it in Settings.** There's no way for an app to reserve this automatically, and it displays a single line of plain text — no progress bar, no multi-line layout, nothing resembling the mockups in Section 2.
3. **Update cadence is not "live."** Content only refreshes when the app pushes a new tile/badge notification, and while the machine is locked and the app process is suspended, the only ways to push an update are (a) a background task on a system-controlled schedule (Windows explicitly reserves the right to delay these by up to ~15 minutes) or (b) a cloud push notification (WNS), which requires running a server — explicitly disallowed by this spec's "no cloud" requirement (Section 23 of the original brief, preserved here).

Separately, Windows 11 also has a **lock-screen "Weather and more" widget strip** (small cards for weather/traffic/sports at the bottom of the lock screen). As of current documentation, this surface is populated by Microsoft's own first-party web content feed; it is not an API third-party desktop apps can publish arbitrary content into. Do not attempt to target it.

There is also a **Windows 11 Widgets Board** (the panel you open from the taskbar). As of Windows App SDK 1.2+, third-party apps genuinely can register widgets for this board. However, this board is a **desktop-session surface you open with a swipe/click while unlocked** — it is not shown on the Lock Screen, and it isn't "glanceable without interaction" the way this product needs. It's a reasonable secondary surface (see 3.4), just not a lock-screen answer.

### 3.3 Conclusion to record in the codebase
Do not attempt to reproduce iOS Live Activities' exact lock-screen visual behavior. It is not achievable with public, supported, non-hacky Windows APIs as of this writing. Do not use undocumented registry hacks, shell injection, or unofficial APIs to force something onto the lock screen — this is explicitly disallowed by the original brief (Section 25) and remains disallowed here.

### 3.4 Recommended closest supported alternative — build this instead
Use **Windows App Notifications with a progress bar** (`Microsoft.Windows.AppNotifications` in the Windows App SDK, the modern replacement for the older `ToastNotification` API). This is the same mechanism Windows itself uses for things like showing an in-place, continuously updating "Downloading update… 45%" notification. Key facts that make this the right choice:

- It supports an **adaptive progress bar binding** in the notification XML, so you can show a title, a percent-complete bar, and a status line, and then **update that same notification in place** (by `tag`/`group`) as often as you like — this is designed for exactly this kind of frequent, in-place progress update.
- It works from a normal **unpackaged Win32/.NET desktop app** (not just MSIX-packaged UWP apps) — Windows App SDK added unpackaged app support for this some time ago, so you are not forced into full MSIX packaging just to get this feature.
- Whether it is visible while the PC is locked depends on the **user's own Windows notification settings** ("Show notifications on the lock screen," in Settings → System → Notifications, plus the per-app toggle and Windows' own "hide sensitive content" option). This is standard, user-controlled behavior, not something the app can override — document this to the user rather than trying to bypass it.
- This is not a permanently pinned card; it behaves like a notification (it can be dismissed, and Windows may collapse/group repeated notifications from the same app under some settings). That is an accepted limitation of this alternative — do not attempt to fight the OS's own notification lifecycle rules.

**Ranked plan for UI surfaces (build in this order — see Section 9 for full detail):**
1. System tray icon + flyout/tooltip (baseline, always works, no special permission needed) — Phase 4.
2. In-app compact "activity card" window (small always-on-top style window, shown while unlocked) — Phase 4/5.
3. Progress-bar App Notification (best available lock-screen-adjacent behavior) — Phase 6.
4. Windows 11 Widgets Board entry (secondary, desktop-only, "nice to have," not required for MVP completion) — only after everything above works; treat as stretch goal.

Do not treat item 3 as equivalent to the original Section 2 mockup's exact lock-screen appearance — it is the closest supported approximation, not an identical reproduction. State this limitation plainly in the app's own "About" or first-run screen so the end user isn't surprised.

---

## 4. RESEARCHED ANSWER — How do we reliably detect an active browser download?

### 4.1 Why folder-watching is not sufficient (confirms the original brief's caution)
Watching `C:\Users\<user>\Downloads` with a `FileSystemWatcher` cannot reliably distinguish "actively downloading" from "just appeared." Chromium browsers write to a temporary file (commonly named with a `.crdownload` extension in Chrome, `.download`/similar patterns in other builds) during the download and rename it to the final filename only on completion — but exact behavior, extensions, and edge cases (resumed downloads, browser-managed multi-part downloads, downloads redirected to non-default folders) are not something to infer generically. Do not build detection logic around guessing temp-file naming conventions.

### 4.2 Recommended approach for v1: a small browser extension + native messaging
Build a minimal **Chrome/Edge extension** (both are Chromium-based and support the same extension API, satisfying the "pick one browser first" instruction while trivially covering two) that uses the built-in `chrome.downloads` API. This API is the official, stable, documented mechanism browsers expose specifically for this purpose, and directly provides, per download, in real time:
- `id`, `filename`, `url`
- `bytesReceived`, `totalBytes` (may be `-1`/unknown for some downloads — handle that as "unknown total size," matching Section 2.2)
- `state` (`in_progress`, `interrupted`, `complete`)
- events: `onCreated`, `onChanged` (fires as bytes accumulate), `onErased`

The extension relays this data to the local Windows app over **Native Messaging** (a documented Chromium mechanism for a browser extension to exchange JSON messages with a local native executable over stdin/stdout, registered via a small manifest file pointing at your app's `.exe`). This is far more reliable than parsing browser SQLite databases (which are locked by the browser process while it's running, forcing you into workarounds like read-only/WAL-mode connections) and vastly more reliable than filesystem heuristics.

**Do not build the SQLite-file-reading approach as the primary mechanism.** It may be considered later as a purely optional fallback for the case where no extension is installed, but it is not the v1 detection strategy, and if built at all it must be clearly marked experimental/best-effort in code comments and UI (e.g., "limited detection mode").

### 4.3 Abstraction to keep the door open for other sources later
Implement an interface, exactly as sketched in the original brief:

```csharp
public interface IDownloadSource
{
    string SourceName { get; }
    Task<IReadOnlyList<DownloadActivity>> GetActiveDownloadsAsync();
    event EventHandler<DownloadActivity> DownloadChanged;
}
```

For v1, implement exactly one concrete class: `ChromiumExtensionDownloadSource` (used for both Chrome and Edge, since the extension and native-messaging protocol are identical for both — register the native messaging host manifest for both browsers' expected registry/manifest locations). Do not build `FirefoxDownloadSource`, `SteamDownloadSource`, or any other source class in v1. Their absence is intentional, not an oversight.

---

## 5. System architecture

```
Chrome/Edge Extension (chrome.downloads API)
              │  Native Messaging (JSON over stdio)
              ▼
   Native Messaging Host Process  ──(same app or a thin companion exe)
              │
              ▼
   Download Tracker  (turns raw events into DownloadActivity objects,
                       computes percentage / smoothed speed / ETA,
                       tracks state transitions)
              │
              ▼
   Activity Store  (in-memory collection of current DownloadActivity
                    objects, keyed by id; purely local, no persistence
                    required beyond process lifetime for MVP)
              │
      ┌───────┼────────────┬─────────────────┐
      ▼       ▼             ▼                 ▼
  Tray UI  In-app card   App Notification   (stretch) Widgets Board
```

Constraints:
- Everything runs on the local machine. No network calls except to the browser via native messaging (which is also local, over stdio — not a network socket).
- No user accounts, no authentication, no remote server, no analytics/telemetry calls, at any layer.
- The presentation layer (tray/card/notification) must only ever read from the `Activity Store` / consume the `DownloadActivity` model. It must never talk to the browser extension directly. This separation is required so additional UI surfaces can be added later without touching detection code, and so detection code can be tested (Phase 1/2) with zero UI.

---

## 6. Data model (exact — implement these fields with these exact names and semantics)

```csharp
public enum DownloadState
{
    Starting,
    Downloading,
    Completed,
    Failed,
    Paused,
    Unknown
}

public class DownloadActivity
{
    public string Id { get; set; }                     // stable per-download identifier from the browser
    public string Filename { get; set; }
    public string Source { get; set; }                  // e.g. "chrome", "edge"
    public long DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }                // null/absent = unknown total size
    public double? PercentageComplete { get; set; }       // null when TotalBytes is null; else 0–100, rounded for display only (store full precision internally, round only at render time)
    public double? SpeedBytesPerSecond { get; set; }       // smoothed value, see Section 7.2; null until enough samples exist
    public int? EstimatedSecondsRemaining { get; set; }    // null when speed is 0/unknown, see Section 7.3
    public DownloadState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}
```

Rules:
- `PercentageComplete` must be clamped to the `[0, 100]` range even if the browser reports something briefly out of range due to resumed/re-validated downloads.
- Do not add extra fields "for future use." If a future phase needs something, add it then.

---

## 7. Calculation rules (implement exactly as specified — do not invent alternate formulas)

### 7.1 Percentage
```
if TotalBytes is known and > 0:
    percentage = clamp(DownloadedBytes / TotalBytes * 100, 0, 100)
else:
    percentage = null   // never fabricate one
```
Display rounded to the nearest whole number. Never display more than zero decimal places in the UI.

### 7.2 Speed (must be smoothed, not instantaneous)
Maintain a short rolling window (recommended: the last 5 samples, taken at the polling interval defined in Section 8) of `(timestamp, bytesReceived)` pairs per download. Compute:
```
speed = (bytes_at_newest_sample - bytes_at_oldest_sample) / (time_at_newest_sample - time_at_oldest_sample)
```
This is a simple moving average over the window — sufficient for the MVP. Do not implement a more complex smoothing algorithm (e.g., exponential decay, Kalman filtering) unless a later phase explicitly asks for it; keep this simple per the original brief's instruction not to over-engineer.

### 7.3 ETA
```
if remainingBytes > 0 and speed > 0:
    eta_seconds = remainingBytes / speed
else:
    eta_seconds = null   // display "Calculating..." in the UI, never a fabricated large number
```
Do not display an ETA at all during the first few polling cycles of a download (before the rolling speed window has enough samples) — show `Starting...` per the Section 2.2 mockup instead.

---

## 8. Update / polling cadence

- Target roughly **1 update per second** for the in-memory `DownloadActivity` state, driven by `chrome.downloads.onChanged` events (which fire as new byte counts arrive) rather than by polling the browser on a fixed timer — this is both more accurate and lighter-weight than polling.
- Separate the **data update rate** from the **UI render rate**. The tray/card UI may redraw at whatever rate feels smooth (e.g., throttle to 2–4 times per second even if the model updates faster) to avoid unnecessary redraw overhead.
- App Notification updates (Section 3.4) should be throttled more conservatively — e.g., once every 2–3 seconds, or on meaningful percentage change (e.g., every 1% or more) — to avoid the OS treating rapid-fire notification updates as spam. Verify actual OS behavior during the Phase 6 proof-of-concept and adjust the throttle based on what you observe; do not assume a number and ship it unverified.

---

## 9. UI surfaces — build order and exact scope per surface

| Order | Surface | Phase | Requirement |
|---|---|---|---|
| 1 | System tray icon, with a right-click/hover flyout showing the compact card layout from Section 2.2 | 4 | Always available; no special permission needed; baseline "does it work at all" surface |
| 2 | Small, borderless, always-on-top "activity card" window (optional pop-out from tray) | 4/5 | Shows the same layout; supports multiple stacked cards per Section 2.3 sort rule |
| 3 | Windows App Notification with progress bar, per Section 3.4 | 6 | Verify lock-screen visibility depends on user's own Windows settings; document this limitation in-app |
| 4 (stretch, optional) | Windows 11 Widgets Board entry | Post-MVP | Only attempt after 1–3 are solid; not required for MVP sign-off |

Do not attempt to build a custom Lock Screen replacement, custom logon UI, or any interception of `LogonUI.exe`/Winlogon. That is exactly the kind of undocumented-hack territory the original brief (and this document) rules out.

---

## 10. Technology stack (use exactly this; do not substitute for "familiarity" reasons)

- **Language/runtime:** C#, .NET (current LTS at time of building)
- **UI framework:** WinUI 3 via the Windows App SDK, for the tray/card UI and for App Notifications
- **Browser integration:** a small Manifest V3 Chrome/Edge extension (JavaScript), communicating via Native Messaging to the .NET app
- **Packaging:** MSIX (see Section 11) — this also gives the cleanest path to Windows App Notifications and, later, the Widgets Board, without requiring a rewrite
- **No Python, Node backend, Electron, or web server** anywhere in the production app. Python may only be used for your own throwaway experiments while investigating an API — never ship it.

---

## 11. Packaging & distribution

- Package as **MSIX** so installation is "download → double-click → install → done," with no separate .NET runtime install step required from the user (use self-contained/trimmed publishing or a framework-dependent MSIX with automatic runtime acquisition — verify at build time which produces the smaller, more reliable installer, and record the decision).
- Register the app to **start automatically with Windows** using the standard, supported per-user "Startup Task" mechanism for packaged apps (`Windows.ApplicationModel.StartupTask`), which also correctly shows up in Windows' own Task Manager → Startup Apps list (so the user can disable it through normal Windows UI — do not hide this from the user or write directly to Run registry keys as a workaround).
- Long-term distribution target: Microsoft Store, per the original brief. For early development, a locally-built and side-loaded MSIX package (or a simple installer) is sufficient — do not attempt Store submission at this stage.
- The **browser extension** cannot be side-loaded persistently without either (a) publishing it to the Chrome Web Store / Edge Add-ons store (even as an unlisted/private listing), or (b) using the "Load unpacked" developer mode during early development. Plan for an unlisted store listing before this leaves your own dev machine — document this clearly as a required step in Phase 8, since it is easy to overlook and it blocks the "normal user installs it with no developer tools" requirement.

---

## 12. Security & privacy constraints (non-negotiable)

- Run with normal user permissions. Never request administrator/elevated rights.
- Do not inject code into the browser process, modify browser binaries, or use any technique other than the officially supported extension + native messaging channel described in Section 4.
- Do not access, log, or transmit browser history, URLs beyond what's needed to show a filename, or any file contents.
- Nothing leaves the local machine. No analytics SDK, no crash-reporting SDK that phones home, no update-checker that calls a remote server, unless a future phase explicitly asks for one.

---

## 13. Error handling — required behaviors

The app must not crash on any of the following; each should degrade gracefully (e.g., mark the affected download `Unknown`/`Failed`, remove it from the active list, or simply stop showing it — never let one bad event take down the whole process):

- Browser is closed while a download is in progress (native messaging channel drops) → treat downloads from that source as stale after a short timeout; do not spin forever waiting for it to come back.
- Browser is restarted → detect a fresh native-messaging connection and re-sync current downloads from `chrome.downloads.search()` on reconnect.
- Download is cancelled or paused (browser reports `interrupted`/paused state) → reflect this in `DownloadState`; do not keep showing it as `Downloading`.
- Network disconnects mid-download → speed drops toward 0, ETA becomes `Calculating...`/null, per Section 7.
- `TotalBytes` unavailable → follow the Section 2.2 "unknown size" display, never guess a total.
- Native messaging host manifest missing/misconfigured → the app should detect that no extension is connected and show a clear "browser extension not connected" state rather than silently doing nothing.

---

## 14. Build phases (do not build out of order; do not skip a phase's acceptance check)

**Phase 0 — Research.** Already complete; see Sections 3 and 4. If you discover during implementation that something stated here is factually wrong (APIs change), stop, note the discrepancy explicitly, and confirm the correction before proceeding — do not silently work around it.

**Phase 1 — Detection, console only.**
Deliverable: a console app that prints active downloads from the Chrome/Edge extension via native messaging, in a format like:
```
Active downloads:

Satellite_Dataset.zip
42.8 GB / 115 GB
37%
18.4 MB/s
```
No UI beyond console text. Acceptance: start a real large download in Chrome, see it appear and update in the console.

**Phase 2 — Live tracking.**
Verify percentage, bytes, smoothed speed, and ETA all update correctly over the lifetime of a real download, per the exact formulas in Section 7.

**Phase 3 — Activity model separation.**
Refactor so detection code produces `DownloadActivity` objects (Section 6) consumed by a presentation-agnostic layer. No UI work yet — just prove the separation with, e.g., a second console renderer.

**Phase 4 — Basic UI.**
Build the tray icon + compact card (Section 9, rows 1–2). No animations, no theming polish yet.

**Phase 5 — Multiple downloads.**
Support several concurrent `DownloadActivity` items with the sort rule from Section 2.2.

**Phase 6 — Windows integration.**
Implement the App Notification progress-bar surface (Section 3.4/9). Build the smallest possible proof of concept first — a single test notification titled "Test Download" whose percentage updates from 37% → 38% → 39% while the app runs, and confirm what actually happens when the screen is locked, before wiring it into the main app.

**Phase 7 — Startup/background behavior.**
Wire up the `StartupTask` registration (Section 11). Confirm the app runs quietly with no visible main window on boot.

**Phase 8 — Packaging.**
Build the MSIX package and the browser extension's store listing (unlisted is fine). Test a clean install on a machine/VM that has never had the dev environment on it.

**Phase 9 — UX polish.**
Only now: typography, spacing, animations, dark/light mode, accessibility (screen reader labels, sufficient contrast), notification tone/behavior tuning.

---

## 15. Definition of done / acceptance tests

Match the original brief's Section 27, unchanged, plus the Phase 0 answer is now considered "done" via Section 3/4 of this document:

1. A large download is detected automatically.
2. Filename, percentage, downloaded/total, and speed are all displayed correctly.
3. Progress visibly updates live during the download (percentage climbs, e.g., 10% → 11% → 12%).
4. Multiple simultaneous downloads display correctly, sorted per Section 2.2.
5. Completion is detected and the UI reflects it per Section 2.2.
6. The app is lightweight while idle — measure and record actual CPU/RAM with zero, one, and several downloads active; there is no fixed target number in the original brief, so just report the measured numbers rather than inventing a pass/fail threshold.
7. The app survives a browser restart without crashing or requiring the user to relaunch it.
8. The app starts automatically after a Windows restart.
9. A non-technical user can install it via double-click, with no terminal, Python, Git, or SDK required.
10. The Lock Screen question has a documented, evidence-based answer (this document, Section 3) and the closest supported alternative (progress App Notification) is implemented and demonstrated.

---

## 16. Explicit non-assumptions — things you must not invent

- Do not assume any specific numeric CPU/RAM budget beyond "lightweight" — measure and report instead of guessing a target.
- Do not assume Firefox, Safari, or any non-Chromium browser is supported. It is explicitly out of scope for v1.
- Do not assume the app can bypass the user's own Windows notification/lock-screen privacy settings. It must respect them.
- Do not assume SQLite database reading is an acceptable primary detection mechanism. It is not; see Section 4.2.
- Do not assume a "real" lock-screen Live Activity card is achievable. It is not, per Section 3; build the documented alternative instead.
- Do not add cloud sync, accounts, or a backend "just to make updates faster" for the notification cadence problem. The no-cloud constraint is absolute.
- If Windows APIs appear to have changed since this document was written such that something above is now inaccurate, stop and flag it rather than silently building around stale information.
