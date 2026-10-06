using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;

namespace LockScreenTest;

/// <summary>
/// Minimal Standalone Proof of Concept for Windows Lock Screen Notification Test
/// Tests different Toast scenarios (Default, Reminder, Alarm) to determine
/// exact Lock Screen visibility behavior on Windows 10/11.
/// </summary>
internal static class Program
{
    private const string AumId = "DownloadTray.POC";

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("================================================================");
        Console.WriteLine("   Windows Lock Screen Notification — Isolated Proof of Concept  ");
        Console.WriteLine("================================================================");
        Console.WriteLine();
        Console.WriteLine("This tool will fire a test download notification with a progress bar.");
        Console.WriteLine();
        Console.WriteLine("Instructions:");
        Console.WriteLine("  1. When prompted, press ENTER.");
        Console.WriteLine("  2. You will have a 5-second countdown.");
        Console.WriteLine("  3. Press WIN + L immediately to lock your computer.");
        Console.WriteLine("  4. While locked, observe if any notification banner appears on screen.");
        Console.WriteLine("  5. Unlock your PC to report what happened.");
        Console.WriteLine();

        Console.Write("Press ENTER to start the 5-second countdown... ");
        Console.ReadLine();

        for (int i = 5; i >= 1; i--)
        {
            Console.WriteLine($"Locking in {i} seconds... (Press Win + L now!)");
            await Task.Delay(1000);
        }

        Console.WriteLine("Firing Lock Screen test toast notification now...");

        // Fire Test Notification 1: Alarm Scenario (Highest Lock Screen Priority in Windows 11)
        new ToastContentBuilder()
            .SetToastScenario(ToastScenario.Alarm)
            .AddText("🔒 DownloadTray — Lock Screen Test")
            .AddText("Satellite_Dataset.zip")
            .AddProgressBar(
                title: "Satellite_Dataset.zip",
                value: 0.37,
                valueStringOverride: "37%",
                status: "37% downloaded • 18.4 MB/s • ~4m remaining")
            .Show(toast =>
            {
                toast.Tag = "poc-lockscreen-test";
                toast.Group = "downloadtray-poc";
            });

        Console.WriteLine("Notification dispatched!");
        Console.WriteLine("Waiting 15 seconds to allow inspection while locked...");
        await Task.Delay(15000);

        Console.WriteLine("Test completed. You can unlock your computer now.");
    }
}
