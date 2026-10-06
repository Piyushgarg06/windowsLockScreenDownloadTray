using System.Collections.Concurrent;
using DownloadTray.Core.Models;

namespace DownloadTray.Core;

/// <summary>
/// Maintains the canonical in-memory collection of DownloadActivity objects,
/// computes percentage / smoothed speed / ETA from raw byte-count updates,
/// and enforces all display rules from BUILD-SPEC Sections 7 and 2.3.
///
/// This layer is purely a model transform — it never touches the browser
/// or the UI directly, only consumes IDownloadSource events and exposes
/// DownloadActivity snapshots to UI consumers.
/// </summary>
public sealed class DownloadTracker
{
    // -------------------------------------------------------------------------
    // Speed rolling window configuration (BUILD-SPEC §7.2)
    // -------------------------------------------------------------------------
    private const int SpeedWindowSize = 5;

    // -------------------------------------------------------------------------
    // Inner type: per-download speed window
    // -------------------------------------------------------------------------
    private sealed class SpeedWindow
    {
        private readonly record struct Sample(DateTimeOffset Timestamp, long Bytes);

        private readonly Queue<Sample> _samples = new();

        public void Add(long bytesReceived)
        {
            _samples.Enqueue(new Sample(DateTimeOffset.UtcNow, bytesReceived));
            while (_samples.Count > SpeedWindowSize)
                _samples.Dequeue();
        }

        /// <summary>
        /// Returns smoothed speed in bytes/second, or null if the window is not full yet
        /// (BUILD-SPEC §7.2 — don't show speed until we have enough samples).
        /// </summary>
        public double? ComputeSpeed()
        {
            if (_samples.Count < 2) return null;

            var oldest = _samples.Peek();
            var newest = _samples.Last(); // Queue in .NET doesn't expose Last directly; see below

            double elapsedSeconds = (newest.Timestamp - oldest.Timestamp).TotalSeconds;
            if (elapsedSeconds <= 0) return null;

            return (newest.Bytes - oldest.Bytes) / elapsedSeconds;
        }

        public bool IsWindowFull => _samples.Count >= SpeedWindowSize;

        // Queue<T> doesn't have Last(), so we track newest separately.
        private Sample _newest;
        public void AddTracked(long bytesReceived)
        {
            _newest = new Sample(DateTimeOffset.UtcNow, bytesReceived);
            _samples.Enqueue(_newest);
            while (_samples.Count > SpeedWindowSize)
                _samples.Dequeue();
        }

        public double? ComputeSpeedTracked()
        {
            if (_samples.Count < 2) return null;

            var oldest = _samples.Peek();
            double elapsedSeconds = (_newest.Timestamp - oldest.Timestamp).TotalSeconds;
            if (elapsedSeconds <= 0) return null;

            return (_newest.Bytes - oldest.Bytes) / elapsedSeconds;
        }
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------
    private readonly ConcurrentDictionary<string, DownloadActivity> _store = new();
    private readonly ConcurrentDictionary<string, SpeedWindow> _windows = new();

    // -------------------------------------------------------------------------
    // Public events
    // -------------------------------------------------------------------------
    /// <summary>Raised after a DownloadActivity has been updated with computed fields.</summary>
    public event EventHandler<DownloadActivity>? ActivityUpdated;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------
    /// <summary>
    /// Accept a raw DownloadActivity (as received from the source), compute derived fields,
    /// update the store, and raise ActivityUpdated.
    /// </summary>
    public void Apply(DownloadActivity raw)
    {
        var id = raw.Id;

        // Get or create the tracked record.
        var tracked = _store.GetOrAdd(id, _ => new DownloadActivity
        {
            Id = id,
            Source = raw.Source,
            StartedAt = raw.StartedAt
        });

        // Copy simple fields.
        tracked.Filename = raw.Filename;
        tracked.DownloadedBytes = raw.DownloadedBytes;
        tracked.TotalBytes = raw.TotalBytes;
        tracked.State = raw.State;
        tracked.LastUpdatedAt = raw.LastUpdatedAt;

        // Update speed window.
        var window = _windows.GetOrAdd(id, _ => new SpeedWindow());
        window.AddTracked(raw.DownloadedBytes);

        // Compute percentage (BUILD-SPEC §7.1).
        if (raw.TotalBytes is > 0)
        {
            var pct = (double)raw.DownloadedBytes / raw.TotalBytes.Value * 100.0;
            tracked.PercentageComplete = Math.Clamp(pct, 0.0, 100.0);
        }
        else
        {
            tracked.PercentageComplete = null; // Never fabricate — BUILD-SPEC §2.3
        }

        // Compute smoothed speed (BUILD-SPEC §7.2).
        tracked.SpeedBytesPerSecond = window.ComputeSpeedTracked();

        // Compute ETA (BUILD-SPEC §7.3).
        if (raw.State == DownloadState.Completed || raw.State == DownloadState.Failed)
        {
            tracked.EstimatedSecondsRemaining = null;
        }
        else if (!window.IsWindowFull)
        {
            // Too early — show "Starting..." in UI.
            tracked.EstimatedSecondsRemaining = null;
        }
        else if (tracked.SpeedBytesPerSecond is > 0 && raw.TotalBytes is > 0)
        {
            long remaining = raw.TotalBytes.Value - raw.DownloadedBytes;
            if (remaining > 0)
                tracked.EstimatedSecondsRemaining =
                    (int)Math.Ceiling(remaining / tracked.SpeedBytesPerSecond.Value);
            else
                tracked.EstimatedSecondsRemaining = 0;
        }
        else
        {
            tracked.EstimatedSecondsRemaining = null; // Show "Calculating..." in UI
        }

        // Raise event.
        ActivityUpdated?.Invoke(this, tracked);
    }

    /// <summary>Removes the entry from the store (download erased/gone).</summary>
    public void Remove(string id)
    {
        _store.TryRemove(id, out _);
        _windows.TryRemove(id, out _);
    }

    /// <summary>
    /// Returns all current downloads, sorted by BUILD-SPEC §2.3 sort rule:
    /// (1) active beats paused/stalled, (2) most recently started first,
    /// (3) tie-break: largest total size.
    /// </summary>
    public IReadOnlyList<DownloadActivity> GetSorted()
    {
        return _store.Values
            .OrderByDescending(IsActive)
            .ThenByDescending(d => d.StartedAt)
            .ThenByDescending(d => d.TotalBytes ?? 0)
            .ToList();
    }

    private static bool IsActive(DownloadActivity d) =>
        d.State is DownloadState.Downloading or DownloadState.Starting;
}
