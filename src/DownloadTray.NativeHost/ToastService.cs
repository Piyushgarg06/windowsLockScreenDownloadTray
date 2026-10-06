using CommunityToolkit.WinUI.Notifications;
using DownloadTray.Core.Models;

namespace DownloadTray.NativeHost;

/// <summary>
/// Consolidated Windows Live Toast Notification Service
/// Shows EXACTLY ONE clean notification card for all active downloads.
/// Tag: "downloadtray-live"
/// </summary>
public sealed class ToastService
{
    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(1800);
    private const string Tag = "downloadtray-live";
    private const string Group = "downloadtray";

    public ToastService()
    {
    }

    public void UpdateDownloads(IReadOnlyList<DownloadActivity> downloads)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastUpdate < ThrottleInterval) return;
        _lastUpdate = now;

        var active = downloads.Where(d => d.State is DownloadState.Downloading or DownloadState.Starting).ToList();

        if (active.Count == 0)
        {
            // If all completed, send one clean completion notification
            var completed = downloads.Where(d => d.State == DownloadState.Completed).ToList();
            if (completed.Count > 0)
            {
                SendCompletedToast(completed);
            }
            return;
        }

        try
        {
            string title;
            string line1;
            string line2 = "";
            double overallRatio = 0;

            if (active.Count == 1)
            {
                var d = active[0];
                int pct = d.PercentageComplete.HasValue ? (int)Math.Round(d.PercentageComplete.Value) : 0;
                title = $"↓ {d.Filename} ({pct}%)";
                
                var parts = new List<string>();
                if (d.TotalBytes.HasValue)
                    parts.Add($"{FormatBytes(d.DownloadedBytes)} / {FormatBytes(d.TotalBytes.Value)}");
                else
                    parts.Add($"{FormatBytes(d.DownloadedBytes)}");

                if (d.SpeedBytesPerSecond.HasValue)
                    parts.Add($"{FormatBytes((long)d.SpeedBytesPerSecond.Value)}/s");

                if (d.EstimatedSecondsRemaining.HasValue)
                    parts.Add($"~{FormatEta(d.EstimatedSecondsRemaining.Value)} remaining");

                line1 = string.Join(" • ", parts);
                overallRatio = d.PercentageComplete.HasValue ? Math.Clamp(d.PercentageComplete.Value / 100.0, 0.0, 1.0) : 0.0;
            }
            else
            {
                // Multi-download consolidated card
                long totalRecv = active.Sum(d => d.DownloadedBytes);
                long? totalBytes = active.All(d => d.TotalBytes.HasValue) ? active.Sum(d => d.TotalBytes!.Value) : null;
                
                int overallPct = totalBytes.HasValue && totalBytes > 0 
                    ? (int)Math.Round((double)totalRecv / totalBytes.Value * 100.0) 
                    : 0;

                title = $"↓ Downloading {active.Count} files ({overallPct}%)";
                
                var d1 = active[0];
                int p1 = d1.PercentageComplete.HasValue ? (int)Math.Round(d1.PercentageComplete.Value) : 0;
                line1 = $"1. {d1.Filename} ({p1}%) - {FormatBytes(d1.DownloadedBytes)}";

                var d2 = active[1];
                int p2 = d2.PercentageComplete.HasValue ? (int)Math.Round(d2.PercentageComplete.Value) : 0;
                line2 = $"2. {d2.Filename} ({p2}%) - {FormatBytes(d2.DownloadedBytes)}";

                overallRatio = totalBytes.HasValue && totalBytes > 0 ? Math.Clamp((double)totalRecv / totalBytes.Value, 0.0, 1.0) : 0.0;
            }

            var builder = new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Reminder)
                .AddText(title)
                .AddText(line1);

            if (!string.IsNullOrEmpty(line2))
            {
                builder.AddText(line2);
            }

            builder.AddProgressBar(
                title: "Progress",
                value: overallRatio,
                isIndeterminate: overallRatio <= 0,
                valueStringOverride: $"{Math.Round(overallRatio * 100)}%",
                status: active.Count > 1 ? $"{active.Count} files active" : "Downloading...")
                .Show(toast =>
                {
                    toast.Tag = Tag;
                    toast.Group = Group;
                });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Toast] Error: {ex.Message}");
        }
    }

    private void SendCompletedToast(IReadOnlyList<DownloadActivity> completed)
    {
        try
        {
            string title = completed.Count == 1 ? "✓ Download complete" : $"✓ {completed.Count} downloads complete";
            string summary = string.Join(", ", completed.Select(d => d.Filename));

            new ToastContentBuilder()
                .AddText(title)
                .AddText(summary)
                .Show(toast =>
                {
                    toast.Tag = "downloadtray-complete";
                    toast.Group = Group;
                });
        }
        catch { }
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
}
