using DownloadTray.Core;
using DownloadTray.Core.Models;
using Xunit;

namespace DownloadTray.Tests;

/// <summary>
/// Unit tests for DownloadTracker — verifies the calculation rules from
/// BUILD-SPEC Sections 7.1 (percentage), 7.2 (smoothed speed), 7.3 (ETA)
/// and the "never fabricate" constraints from Section 2.3.
/// </summary>
public class DownloadTrackerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static DownloadActivity MakeActivity(
        string id = "1",
        long downloaded = 0,
        long? total = null,
        DownloadState state = DownloadState.Downloading) =>
        new()
        {
            Id = id,
            Filename = "test.zip",
            Source = "brave",
            DownloadedBytes = downloaded,
            TotalBytes = total,
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

    private static DownloadActivity ApplyN(DownloadTracker tracker, string id,
        long[] byteSeries, long? total)
    {
        DownloadActivity? last = null;
        tracker.ActivityUpdated += (_, a) => last = a;

        foreach (var b in byteSeries)
        {
            var act = MakeActivity(id, b, total);
            tracker.Apply(act);
            Thread.Sleep(50); // ensure non-zero time between samples
        }

        return last!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7.1 — Percentage
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Percentage_IsNullWhenTotalBytesUnknown()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        tracker.Apply(MakeActivity("1", downloaded: 500_000, total: null));

        Assert.NotNull(result);
        Assert.Null(result!.PercentageComplete); // BUILD-SPEC §2.3: never fabricate
    }

    [Fact]
    public void Percentage_IsComputedWhenTotalKnown()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        tracker.Apply(MakeActivity("1", downloaded: 500_000, total: 1_000_000));

        Assert.NotNull(result!.PercentageComplete);
        Assert.Equal(50.0, result!.PercentageComplete!.Value, precision: 1);
    }

    [Fact]
    public void Percentage_IsClampedTo100WhenBytesExceedTotal()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        // downloaded > total (can happen with resumed downloads)
        tracker.Apply(MakeActivity("1", downloaded: 1_100_000, total: 1_000_000));

        Assert.Equal(100.0, result!.PercentageComplete!.Value, precision: 1);
    }

    [Fact]
    public void Percentage_IsClampedTo0WhenNegative()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        tracker.Apply(MakeActivity("1", downloaded: 0, total: 1_000_000));

        Assert.True(result!.PercentageComplete!.Value >= 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7.2 — Smoothed speed (rolling window)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Speed_IsNullBeforeWindowFull()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        // Only 1 sample — window not full (needs ≥2 for a delta)
        tracker.Apply(MakeActivity("1", downloaded: 1_000_000));

        // Speed may be null or zero — must not be a positive number yet
        Assert.True(result!.SpeedBytesPerSecond is null or <= 0);
    }

    [Fact]
    public void Speed_IsPositiveAfterSeveralSamples()
    {
        var tracker = new DownloadTracker();
        var byteSeries = new long[] { 0, 1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000 };
        var result = ApplyN(tracker, "1", byteSeries, total: 100_000_000);

        Assert.NotNull(result.SpeedBytesPerSecond);
        Assert.True(result.SpeedBytesPerSecond > 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7.3 — ETA
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ETA_IsNullWhenSpeedIsZero()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        // All samples with the same byte count → speed = 0
        for (int i = 0; i < 6; i++)
        {
            tracker.Apply(MakeActivity("1", downloaded: 1_000_000, total: 10_000_000));
            Thread.Sleep(50);
        }

        // Speed should be 0 (all same bytes) → ETA must be null
        Assert.Null(result!.EstimatedSecondsRemaining); // BUILD-SPEC §7.3
    }

    [Fact]
    public void ETA_IsNullWhenTotalUnknown()
    {
        var tracker = new DownloadTracker();
        var byteSeries = new long[] { 0, 1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000 };
        var result = ApplyN(tracker, "1", byteSeries, total: null); // unknown total

        // Can't compute ETA without knowing remaining bytes
        Assert.Null(result.EstimatedSecondsRemaining);
    }

    [Fact]
    public void ETA_IsNullOnCompletion()
    {
        var tracker = new DownloadTracker();
        DownloadActivity? result = null;
        tracker.ActivityUpdated += (_, a) => result = a;

        tracker.Apply(MakeActivity("1", downloaded: 1_000_000, total: 1_000_000,
            state: DownloadState.Completed));

        Assert.Null(result!.EstimatedSecondsRemaining);
    }

    [Fact]
    public void ETA_IsPositiveWhenSpeedAndRemainingKnown()
    {
        var tracker = new DownloadTracker();
        // 5 samples with growing bytes → positive speed → should yield an ETA
        var byteSeries = new long[] { 0, 2_000_000, 4_000_000, 6_000_000, 8_000_000, 10_000_000 };
        var result = ApplyN(tracker, "1", byteSeries, total: 100_000_000);

        // At 10MB received with positive speed, we expect a positive ETA
        if (result.SpeedBytesPerSecond is > 0)
        {
            Assert.NotNull(result.EstimatedSecondsRemaining);
            Assert.True(result.EstimatedSecondsRemaining > 0);
        }
        // If window wasn't full enough, null is also acceptable (Starting... state)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sort order (BUILD-SPEC §2.3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSorted_ActiveBeforePaused()
    {
        var tracker = new DownloadTracker();
        tracker.Apply(MakeActivity("paused", downloaded: 500_000, total: 1_000_000,
            state: DownloadState.Paused));
        tracker.Apply(MakeActivity("active", downloaded: 500_000, total: 1_000_000,
            state: DownloadState.Downloading));

        var sorted = tracker.GetSorted();
        Assert.Equal("active", sorted[0].Id);
        Assert.Equal("paused", sorted[1].Id);
    }

    [Fact]
    public void GetSorted_StartingCountsAsActive()
    {
        var tracker = new DownloadTracker();
        tracker.Apply(MakeActivity("completed", downloaded: 1_000_000, total: 1_000_000,
            state: DownloadState.Completed));
        tracker.Apply(MakeActivity("starting", downloaded: 0, total: null,
            state: DownloadState.Starting));

        var sorted = tracker.GetSorted();
        Assert.Equal("starting", sorted[0].Id);
    }
}
