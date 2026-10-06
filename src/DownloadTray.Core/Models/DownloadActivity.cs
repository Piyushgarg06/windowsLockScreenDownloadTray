namespace DownloadTray.Core.Models;

/// <summary>
/// Represents one active (or recently completed) download, as exposed to all UI surfaces.
/// Field names, types, and nullability defined verbatim by BUILD-SPEC Section 6.
/// </summary>
public class DownloadActivity
{
    /// <summary>Stable per-download identifier from the browser (chrome.downloads id, cast to string).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Final filename (leaf name only, not full path). May be empty briefly at download start.</summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>Originating browser slug, e.g. "chrome", "edge", "brave".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Bytes received so far.</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>
    /// Total file size in bytes, or null when the server did not send Content-Length.
    /// NEVER fabricate a value when this is null — see BUILD-SPEC Section 2.3.
    /// </summary>
    public long? TotalBytes { get; set; }

    /// <summary>
    /// 0–100, or null when TotalBytes is null.
    /// Stored at full precision internally; round only at render time (BUILD-SPEC §7.1).
    /// Clamped to [0, 100] even if the browser briefly reports an out-of-range value.
    /// </summary>
    public double? PercentageComplete { get; set; }

    /// <summary>
    /// Smoothed speed in bytes/second (rolling window, see BUILD-SPEC §7.2).
    /// Null until the rolling window has enough samples to produce a meaningful value.
    /// </summary>
    public double? SpeedBytesPerSecond { get; set; }

    /// <summary>
    /// Estimated seconds remaining, or null when speed is 0 or unknown (BUILD-SPEC §7.3).
    /// Never show a fabricated large ETA — show "Calculating..." in UI when this is null.
    /// </summary>
    public int? EstimatedSecondsRemaining { get; set; }

    /// <summary>Current lifecycle state of this download.</summary>
    public DownloadState State { get; set; } = DownloadState.Starting;

    /// <summary>UTC timestamp when the download was first detected.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC timestamp of the most recent update to this record.</summary>
    public DateTimeOffset LastUpdatedAt { get; set; }
}
