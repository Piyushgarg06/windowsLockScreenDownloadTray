using DownloadTray.Core.Models;

namespace DownloadTray.Core.Sources;

/// <summary>
/// Abstraction over any download data source (browser extension, file watcher, etc.).
/// Defined verbatim by BUILD-SPEC Section 4.3. Only ChromiumExtensionDownloadSource is
/// implemented in v1; additional sources may be added in later versions without touching
/// detection or UI code.
/// </summary>
public interface IDownloadSource
{
    /// <summary>Human-readable name of this source, e.g. "Chromium Native Messaging".</summary>
    string SourceName { get; }

    /// <summary>
    /// Returns all currently active downloads known to this source at the moment of calling.
    /// Used for initial sync on startup or reconnect.
    /// </summary>
    Task<IReadOnlyList<DownloadActivity>> GetActiveDownloadsAsync();

    /// <summary>
    /// Raised whenever any property of a tracked download changes (bytes received,
    /// state transition, completion, removal). The event argument is a snapshot of the
    /// updated DownloadActivity at the time of the change.
    /// </summary>
    event EventHandler<DownloadActivity> DownloadChanged;

    /// <summary>
    /// Starts listening for native messaging messages from the browser extension.
    /// Returns when the source is connected and ready (or throws on fatal error).
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
