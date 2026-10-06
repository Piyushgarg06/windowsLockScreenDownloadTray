namespace DownloadTray.Core.Models;

/// <summary>
/// Represents the lifecycle state of a tracked download.
/// States defined verbatim by BUILD-SPEC Section 6.
/// </summary>
public enum DownloadState
{
    /// <summary>Download has just been created; no bytes received yet.</summary>
    Starting,

    /// <summary>Download is actively receiving bytes.</summary>
    Downloading,

    /// <summary>Download finished successfully.</summary>
    Completed,

    /// <summary>Download failed or was interrupted unrecoverably.</summary>
    Failed,

    /// <summary>Download was paused by the user or browser.</summary>
    Paused,

    /// <summary>State cannot be determined (e.g., source disconnected).</summary>
    Unknown
}
