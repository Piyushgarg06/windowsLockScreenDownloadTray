/**
 * DownloadTray — background service worker (Manifest V3)
 *
 * Bridges the chrome.downloads API to the native messaging host
 * (DownloadTray.NativeHost.exe) running on the local Windows machine.
 *
 * Protocol: messages are plain JSON objects sent via chrome.runtime.connectNative().
 * The native host receives them framed with a 4-byte little-endian length header.
 *
 * Message shapes sent to the native host:
 *   { type: "downloads.onCreated",  download: <DownloadItem> }
 *   { type: "downloads.onChanged",  delta:    <DownloadDelta> }
 *   { type: "downloads.onErased",   downloadId: number }
 *   { type: "downloads.sync",       download: <DownloadItem> }   ← initial/reconnect sync
 *
 * BUILD-SPEC reference: Section 4.2, 5.
 */

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────
const NATIVE_HOST_NAME = "com.downloadtray.nativehost";
const RECONNECT_DELAY_MS = 3000;

// ─────────────────────────────────────────────────────────────────────────────
// Connection state
// ─────────────────────────────────────────────────────────────────────────────
let port = null;
let reconnectTimer = null;

// ─────────────────────────────────────────────────────────────────────────────
// Connect to the native host
// ─────────────────────────────────────────────────────────────────────────────
function connect() {
  if (port) return; // already connected

  try {
    port = chrome.runtime.connectNative(NATIVE_HOST_NAME);
  } catch (err) {
    console.error("[DownloadTray] connectNative failed:", err);
    scheduleReconnect();
    return;
  }

  port.onDisconnect.addListener(() => {
    const err = chrome.runtime.lastError?.message ?? "unknown";
    console.warn(`[DownloadTray] Native port disconnected: ${err}`);
    port = null;
    // BUILD-SPEC §13: detect fresh connection on browser restart → re-sync on reconnect.
    scheduleReconnect();
  });

  // Sync all currently in-progress downloads on connect/reconnect.
  syncExistingDownloads();
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, RECONNECT_DELAY_MS);
}

// ─────────────────────────────────────────────────────────────────────────────
// Send a message to the native host (best-effort; drops if not connected)
// ─────────────────────────────────────────────────────────────────────────────
function send(message) {
  if (!port) {
    console.warn("[DownloadTray] Not connected — dropping message:", message.type);
    return;
  }
  try {
    port.postMessage(message);
  } catch (err) {
    console.error("[DownloadTray] postMessage error:", err);
    port = null;
    scheduleReconnect();
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Initial sync: send all currently in-progress downloads on connect/reconnect.
// BUILD-SPEC §13: "detect a fresh native-messaging connection and re-sync
// current downloads from chrome.downloads.search() on reconnect."
// ─────────────────────────────────────────────────────────────────────────────
function syncExistingDownloads() {
  chrome.downloads.search({ state: "in_progress" }, (items) => {
    for (const item of items) {
      send({ type: "downloads.sync", download: item });
    }
    console.log(`[DownloadTray] Synced ${items.length} in-progress download(s).`);
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// chrome.downloads event listeners
// ─────────────────────────────────────────────────────────────────────────────

chrome.downloads.onCreated.addListener((downloadItem) => {
  send({ type: "downloads.onCreated", download: downloadItem });
});

chrome.downloads.onChanged.addListener((downloadDelta) => {
  send({ type: "downloads.onChanged", delta: downloadDelta });
});

chrome.downloads.onErased.addListener((downloadId) => {
  send({ type: "downloads.onErased", downloadId });
});

// ─────────────────────────────────────────────────────────────────────────────
// Service worker lifecycle: connect on install/startup
// ─────────────────────────────────────────────────────────────────────────────
chrome.runtime.onInstalled.addListener(() => {
  console.log("[DownloadTray] Extension installed/updated — connecting to native host.");
  connect();
});

chrome.runtime.onStartup.addListener(() => {
  console.log("[DownloadTray] Browser started — connecting to native host.");
  connect();
});

// Connect immediately when the service worker wakes up (MV3 service workers
// can be re-activated by events; reconnect defensively on each wake).
connect();
