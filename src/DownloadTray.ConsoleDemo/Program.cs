using DownloadTray.Core;
using DownloadTray.Core.Models;
using DownloadTray.Core.Sources;

// ============================================================================
// DownloadTray — Console Demo & Live Simulator
// ============================================================================
// Supports two modes:
// 1. Interactive Simulator: `dotnet run --project src/DownloadTray.ConsoleDemo -- --simulate`
//    Simulates realistic live downloads (Ubuntu ISO + Node.js setup) with dynamic
//    progress, speed smoothing, ETAs, and completion states.
// 2. Extension Pipe Mode (default):
//    Reads native messaging frames from stdin and displays live dashboard.
// ============================================================================

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "DownloadTray — Console Dashboard";

var isSimulate = args.Contains("--simulate") || args.Contains("-s");

if (isSimulate)
{
    await RunSimulatorAsync();
}
else
{
    await RunPipeModeAsync();
}

static async Task RunSimulatorAsync()
{
    Console.WriteLine("Starting Live Download Simulation (Press Ctrl+C to stop)...");
    await Task.Delay(600);

    var tracker = new DownloadTracker();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    tracker.ActivityUpdated += (_, _) =>
    {
        RenderDashboard(tracker.GetSorted());
    };

    const long totalBytesIso = 5_368_709_120L; // 5.0 GB
    const long totalBytesZip = 45_000_000L;     // 45 MB

    // Download 1: Ubuntu ISO
    tracker.Apply(new DownloadActivity
    {
        Id = "d-101",
        Source = "brave",
        Filename = "Ubuntu-24.04-LTS-Desktop.iso",
        State = DownloadState.Starting,
        DownloadedBytes = 0,
        TotalBytes = totalBytesIso,
        StartedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    });

    await Task.Delay(1000);

    // Download 2: Node.js Installer
    tracker.Apply(new DownloadActivity
    {
        Id = "d-102",
        Source = "brave",
        Filename = "node-v20.12.2-x64.msi",
        State = DownloadState.Downloading,
        DownloadedBytes = 5_000_000,
        TotalBytes = totalBytesZip,
        StartedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    });

    // Progression loop
    const int totalSteps = 40;
    for (int step = 1; step <= totalSteps && !cts.Token.IsCancellationRequested; step++)
    {
        long isoBytes = (long)(totalBytesIso * (step / (double)totalSteps));
        long zipBytes = Math.Min(totalBytesZip, (long)(totalBytesZip * (step * 2.5 / totalSteps)));

        tracker.Apply(new DownloadActivity
        {
            Id = "d-101",
            Source = "brave",
            Filename = "Ubuntu-24.04-LTS-Desktop.iso",
            State = step == totalSteps ? DownloadState.Completed : DownloadState.Downloading,
            DownloadedBytes = isoBytes,
            TotalBytes = totalBytesIso,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-step),
            LastUpdatedAt = DateTimeOffset.UtcNow
        });

        if (zipBytes >= totalBytesZip)
        {
            tracker.Apply(new DownloadActivity
            {
                Id = "d-102",
                Source = "brave",
                Filename = "node-v20.12.2-x64.msi",
                State = DownloadState.Completed,
                DownloadedBytes = totalBytesZip,
                TotalBytes = totalBytesZip,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-step),
                LastUpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            tracker.Apply(new DownloadActivity
            {
                Id = "d-102",
                Source = "brave",
                Filename = "node-v20.12.2-x64.msi",
                State = DownloadState.Downloading,
                DownloadedBytes = zipBytes,
                TotalBytes = totalBytesZip,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-step),
                LastUpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await Task.Delay(350, cts.Token);
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("All simulated downloads finished!");
    Console.ResetColor();
    await Task.Delay(2000);
}

static async Task RunPipeModeAsync()
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var source = new ChromiumExtensionDownloadSource(
        "demo",
        Console.OpenStandardInput(),
        Console.OpenStandardOutput());

    var tracker = new DownloadTracker();

    source.DownloadChanged += (_, activity) =>
    {
        tracker.Apply(activity);
        RenderDashboard(tracker.GetSorted());
    };

    Console.WriteLine("DownloadTray Console Demo — waiting for browser extension events...");
    Console.WriteLine("(Start a download in Brave/Chrome/Edge to see it here)");
    Console.WriteLine();

    await source.StartAsync(cts.Token);
}

static void RenderDashboard(IReadOnlyList<DownloadActivity> downloads)
{
    try
    {
        Console.Clear();
    }
    catch { /* ignore if not attached to standard console buffer */ }

    Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║            DownloadTray — Live Download Dashboard          ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    if (downloads.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  No active downloads.");
        Console.ResetColor();
        return;
    }

    foreach (var d in downloads)
    {
        var stateColor = d.State switch
        {
            DownloadState.Downloading => ConsoleColor.Cyan,
            DownloadState.Completed => ConsoleColor.Green,
            DownloadState.Failed => ConsoleColor.Red,
            DownloadState.Paused => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };

        var icon = d.State == DownloadState.Completed ? "✓" : "↓";
        Console.ForegroundColor = stateColor;
        Console.Write($"  {icon} ");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{d.Filename}  [{d.Source}]");
        Console.ResetColor();

        switch (d.State)
        {
            case DownloadState.Starting:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("    Starting... (0%)");
                Console.ResetColor();
                break;

            case DownloadState.Downloading:
                if (d.TotalBytes.HasValue && d.PercentageComplete.HasValue)
                {
                    var pct = (int)d.PercentageComplete.Value;
                    var bar = BuildProgressBar(pct, 28);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"    [{bar}] {pct}%");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"    {FormatBytes(d.DownloadedBytes)} / {FormatBytes(d.TotalBytes.Value)}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    {FormatBytes(d.DownloadedBytes)} downloaded (size unknown)");
                    Console.ResetColor();
                }

                if (d.SpeedBytesPerSecond.HasValue)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"    ⚡ {FormatBytes((long)d.SpeedBytesPerSecond.Value)}/s");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                if (d.EstimatedSecondsRemaining.HasValue)
                    Console.WriteLine($"   ⏳ ~{FormatEta(d.EstimatedSecondsRemaining.Value)} remaining");
                else
                    Console.WriteLine("   ⏳ Calculating ETA...");
                Console.ResetColor();
                break;

            case DownloadState.Completed:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"    Completed ({FormatBytes(d.DownloadedBytes)}) ✓");
                Console.ResetColor();
                break;

            case DownloadState.Failed:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("    Failed / interrupted ✗");
                Console.ResetColor();
                break;

            case DownloadState.Paused:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("    Paused ⏸");
                Console.ResetColor();
                break;
        }

        Console.WriteLine();
    }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Updated: {DateTime.Now:HH:mm:ss.fff}");
    Console.ResetColor();
}

static string BuildProgressBar(int pct, int width)
{
    var filled = Math.Clamp((int)(pct / 100.0 * width), 0, width);
    return new string('█', filled) + new string('░', width - filled);
}

static string FormatBytes(long bytes)
{
    if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
    if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
    if (bytes >= 1_024) return $"{bytes / 1_024.0:F1} KB";
    return $"{bytes} B";
}

static string FormatEta(int seconds)
{
    if (seconds >= 3600) return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    if (seconds >= 60) return $"{seconds / 60}m {seconds % 60}s";
    return $"{seconds}s";
}
