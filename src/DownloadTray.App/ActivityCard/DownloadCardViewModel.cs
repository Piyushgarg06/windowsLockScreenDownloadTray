using DownloadTray.Core.Models;

namespace DownloadTray.App.ActivityCard;

/// <summary>
/// ViewModel for a single download card row in the activity card UI.
/// Formats all fields according to BUILD-SPEC display rules (Section 2.2, 7).
/// </summary>
public class DownloadCardViewModel
{
    public DownloadCardViewModel(DownloadActivity activity)
    {
        Id = activity.Id;
        Filename = activity.Filename;
        State = activity.State;
        DownloadedBytes = activity.DownloadedBytes;
        TotalBytes = activity.TotalBytes;
        PercentageComplete = activity.PercentageComplete;
        SpeedBytesPerSecond = activity.SpeedBytesPerSecond;
        EstimatedSecondsRemaining = activity.EstimatedSecondsRemaining;
        Source = activity.Source;
    }

    public string Id { get; }
    public string Filename { get; }
    public DownloadState State { get; }
    public long DownloadedBytes { get; }
    public long? TotalBytes { get; }
    public double? PercentageComplete { get; }
    public double? SpeedBytesPerSecond { get; }
    public int? EstimatedSecondsRemaining { get; }
    public string Source { get; }

    // ─────────────────────────────────────────────────────────────────────────
    // Formatted display properties (used by XAML bindings)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>"↓" or "✓" depending on state.</summary>
    public string Icon => State == DownloadState.Completed ? "✓" : "↓";

    /// <summary>
    /// Percentage as whole-number string e.g. "64%", or null when total is unknown.
    /// BUILD-SPEC §7.1: "round to nearest whole number only."
    /// </summary>
    public string? PercentageText =>
        PercentageComplete.HasValue
            ? $"{(int)Math.Round(PercentageComplete.Value)}%"
            : null;

    /// <summary>Progress bar value 0–100 for the ProgressBar control.</summary>
    public double ProgressValue => PercentageComplete ?? 0;

    /// <summary>Whether a progress bar should be shown (only when total is known).</summary>
    public bool ShowProgressBar => TotalBytes.HasValue && State == DownloadState.Downloading;

    /// <summary>"4.2 GB / 6.5 GB" or "2.4 GB downloaded" depending on whether total is known.</summary>
    public string BytesLine =>
        TotalBytes.HasValue
            ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes.Value)}"
            : $"{FormatBytes(DownloadedBytes)} downloaded";

    /// <summary>Formatted speed or empty string.</summary>
    public string SpeedText =>
        SpeedBytesPerSecond.HasValue
            ? $"{FormatBytes((long)SpeedBytesPerSecond.Value)}/s"
            : string.Empty;

    /// <summary>
    /// ETA text — "~2m remaining" or "Calculating..." (never a large fake number).
    /// BUILD-SPEC §7.3: null speed → "Calculating...", not a fabricated huge ETA.
    /// </summary>
    public string EtaText =>
        State == DownloadState.Starting ? "Starting..." :
        State == DownloadState.Completed ? "Download complete" :
        State == DownloadState.Failed ? "Failed" :
        State == DownloadState.Paused ? "Paused" :
        EstimatedSecondsRemaining.HasValue
            ? $"~{FormatEta(EstimatedSecondsRemaining.Value)} remaining"
            : "Calculating...";

    // ─────────────────────────────────────────────────────────────────────────
    // Static formatting helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string FormatEta(int seconds)
    {
        if (seconds >= 3600) return $"{seconds / 3600}h {seconds % 3600 / 60}m";
        if (seconds >= 60) return $"{seconds / 60}m {seconds % 60}s";
        return $"{seconds}s";
    }
}
