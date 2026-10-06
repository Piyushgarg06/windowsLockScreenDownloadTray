using DownloadTray.Core;
using DownloadTray.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DownloadTray.App.Services;

/// <summary>
/// Sends and updates Windows App Notifications with progress bars for active downloads.
/// Uses the Microsoft.Windows.AppNotifications API (Windows App SDK).
///
/// Throttling: BUILD-SPEC §8 mandates throttling notification updates to avoid the OS
/// treating rapid-fire updates as spam. This implementation updates at most once every
/// 2 seconds per download, or on ≥1% percentage change.
///
/// Lock screen: visibility depends on the user's own Windows notification settings
/// (Settings → System → Notifications). This is by design — BUILD-SPEC §3.4 explicitly
/// states we must not attempt to bypass user privacy settings.
///
/// BUILD-SPEC reference: Sections 3.4, 6, 8, 9 (surface #3).
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly DownloadTracker _tracker;

    // Per-download throttle state.
    private readonly Dictionary<string, (DateTimeOffset LastSent, double? LastPct)> _throttleState = new();

    // Throttle constants (per BUILD-SPEC §8 — verify actual OS behavior and tune).
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);
    private const double MinPctChange = 1.0; // percentage points

    public NotificationService(DownloadTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task InitializeAsync()
    {
        // Register the app with the notification platform.
        // For unpackaged apps, the app must be registered with a unique AUMID.
        AppNotificationManager.Default.Register();
        await Task.CompletedTask;
    }

    public void UpdateDownloads(IReadOnlyList<DownloadActivity> downloads)
    {
        foreach (var d in downloads)
        {
            switch (d.State)
            {
                case DownloadState.Downloading:
                case DownloadState.Starting:
                    MaybeSendOrUpdateNotification(d);
                    break;

                case DownloadState.Completed:
                    SendCompletionNotification(d);
                    _throttleState.Remove(d.Id);
                    break;

                case DownloadState.Failed:
                    _throttleState.Remove(d.Id);
                    break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Throttled progress notification
    // ─────────────────────────────────────────────────────────────────────────
    private void MaybeSendOrUpdateNotification(DownloadActivity d)
    {
        var now = DateTimeOffset.UtcNow;

        if (_throttleState.TryGetValue(d.Id, out var prev))
        {
            bool tooSoon = (now - prev.LastSent) < MinInterval;
            bool pctUnchanged = d.PercentageComplete.HasValue && prev.LastPct.HasValue &&
                                Math.Abs(d.PercentageComplete.Value - prev.LastPct.Value) < MinPctChange;

            if (tooSoon && pctUnchanged) return;
        }

        _throttleState[d.Id] = (now, d.PercentageComplete);
        SendProgressNotification(d);
    }

    private void SendProgressNotification(DownloadActivity d)
    {
        try
        {
            // Progress bar notifications use a special binding format.
            // The tag/group lets us update the same notification in-place.
            var pct = d.PercentageComplete.HasValue
                ? d.PercentageComplete.Value / 100.0
                : 0.0;

            var status = d.State == DownloadState.Starting
                ? "Starting..."
                : BuildStatusLine(d);

            // Build the notification XML with progress bar.
            var builder = new AppNotificationBuilder()
                .AddText($"↓ {d.Filename}")
                .AddProgressBar(new AppNotificationProgressBar()
                    .BindTitle()
                    .BindValue()
                    .BindStatus()
                    .BindValueStringOverride());

            var notification = builder.BuildNotification();
            notification.Tag = $"download-{d.Id}";
            notification.Group = "downloads";

            // Set the data binding for the progress bar.
            var data = new AppNotificationProgressData(1)
            {
                Title = d.Filename,
                Value = pct,
                ValueStringOverride = d.PercentageComplete.HasValue
                    ? $"{(int)Math.Round(d.PercentageComplete.Value)}%"
                    : string.Empty,
                Status = status
            };

            AppNotificationManager.Default.Show(notification);
            _ = AppNotificationManager.Default.UpdateAsync(data, notification.Tag, notification.Group);
        }
        catch (Exception ex)
        {
            // Non-fatal — notification failure must not crash the app (BUILD-SPEC §13).
            System.Diagnostics.Debug.WriteLine($"[Notifications] Update failed: {ex.Message}");
        }
    }

    private void SendCompletionNotification(DownloadActivity d)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText($"✓ {d.Filename}")
                .AddText("Download complete");

            var notification = builder.BuildNotification();
            notification.Tag = $"download-complete-{d.Id}";
            notification.Group = "downloads";

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notifications] Completion notification failed: {ex.Message}");
        }
    }

    private static string BuildStatusLine(DownloadActivity d)
    {
        var parts = new List<string>();

        if (d.SpeedBytesPerSecond.HasValue)
            parts.Add(FormatBytes((long)d.SpeedBytesPerSecond.Value) + "/s");

        if (d.EstimatedSecondsRemaining.HasValue)
            parts.Add($"~{FormatEta(d.EstimatedSecondsRemaining.Value)} remaining");
        else
            parts.Add("Calculating...");

        return string.Join(" · ", parts);
    }

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
        if (seconds >= 60) return $"{seconds / 60}m";
        return $"{seconds}s";
    }

    public void Dispose()
    {
        AppNotificationManager.Default.Unregister();
    }
}
