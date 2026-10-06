using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using DownloadTray.Core;
using DownloadTray.Core.Models;
using DownloadTray.Core.Sources;
using DownloadTray.NativeHost;

// ============================================================================
// DownloadTray — Windows Native Application & Native Messaging Host
// ============================================================================
// Can be run in two ways:
// 1. As a standalone Windows desktop app (user double-clicks DownloadTray.exe)
//    - Auto-registers itself with all Chromium browsers in the registry
//    - Sits in the System Tray
//    - Shows the Live Activity Card whenever clicked or when downloads are active
// 2. As the browser native messaging host (Brave/Chrome/Edge launches it)
//    - Feeds live download events from the browser extension
//    - Updates Windows Toasts, System Tray, and the Live Activity Card
// ============================================================================

return await NativeHostProgram.RunAsync(args);

internal static class NativeHostProgram
{
    private const string ExtensionId = "jkbpkicdhndajhgopephlfhoehfegnmg";

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        Console.SetOut(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloadTray");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "nativehost.log");
        var logFile = new StreamWriter(logPath, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Console.Error.WriteLine(line);
            try { logFile.WriteLine(line); } catch { }
        }

        // Auto-register this executable with all Chromium browsers on startup
        try
        {
            var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
            BrowserRegistrar.RegisterAllBrowsers(currentExe, ExtensionId, logDir);
            Log("Auto-registered native messaging host with all Chromium browsers.");
        }
        catch (Exception ex)
        {
            Log($"Registration error: {ex.Message}");
        }

        var browserSlug = ProcessHelper.DetectBrowserSlug(args);
        Log($"DownloadTray Native App started — mode: {browserSlug}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var source = new ChromiumExtensionDownloadSource(browserSlug, stdin, stdout);
        var tracker = new DownloadTracker();

        // Initialize Windows Toast and System Tray / Activity Card
        ToastService? toastService = null;
        TrayService? trayService = null;
        try { toastService = new ToastService(); } catch (Exception ex) { Log($"Toast init: {ex.Message}"); }
        try { trayService = new TrayService(); } catch (Exception ex) { Log($"Tray init: {ex.Message}"); }

        source.DownloadChanged += (_, activity) => tracker.Apply(activity);

        tracker.ActivityUpdated += (_, _) =>
        {
            var sorted = tracker.GetSorted();
            ConsoleRenderer.RenderToLog(sorted, Log);

            try { toastService?.UpdateDownloads(sorted); } catch { }
            try { trayService?.UpdateDownloads(sorted); } catch { }
        };

        try
        {
            await source.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("DownloadTray shutting down.");
        }
        catch (Exception ex)
        {
            Log($"Host error: {ex.Message}");
            // In standalone mode if stdin closed, keep the tray icon alive!
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
            }
        }

        return 0;
    }
}

// ============================================================================
// BrowserRegistrar — Automatically registers DownloadTray.exe with all browsers
// ============================================================================
internal static class BrowserRegistrar
{
    private const string HostName = "com.downloadtray.nativehost";

    public static void RegisterAllBrowsers(string exePath, string extensionId, string logDir)
    {
        // 1. Write the Native Messaging Manifest JSON
        var manifestDir = Path.Combine(logDir, "NativeMessagingHosts");
        Directory.CreateDirectory(manifestDir);
        var manifestPath = Path.Combine(manifestDir, $"{HostName}.json");

        string escapedExe = exePath.Replace("\\", "\\\\");
        string allowedOrigin = $"chrome-extension://{extensionId}/";

        string manifestJson = "{\n" +
            $"  \"name\": \"{HostName}\",\n" +
            $"  \"description\": \"DownloadTray Native Messaging Host\",\n" +
            $"  \"path\": \"{escapedExe}\",\n" +
            $"  \"type\": \"stdio\",\n" +
            $"  \"allowed_origins\": [\"{allowedOrigin}\"]\n" +
            "}";

        File.WriteAllText(manifestPath, manifestJson, System.Text.Encoding.UTF8);

        // 2. Register with all Chromium browser registry keys in HKCU
        string[] browserKeys = new[]
        {
            @"Software\Google\Chrome\NativeMessagingHosts\" + HostName,
            @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName,
            @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\" + HostName,
            @"Software\Opera Software\Opera Stable\NativeMessagingHosts\" + HostName,
            @"Software\Opera Software\Opera GX Stable\NativeMessagingHosts\" + HostName,
            @"Software\Vivaldi\NativeMessagingHosts\" + HostName,
            @"Software\Chromium\NativeMessagingHosts\" + HostName,
        };

        foreach (var keyPath in browserKeys)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
                key?.SetValue(null, manifestPath);
            }
            catch { }
        }

        // 3. Register AUMID and Lock Screen notification policy
        try
        {
            using var notifKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\DownloadTray", true);
            notifKey?.SetValue("Enabled", 1, RegistryValueKind.DWord);
            notifKey?.SetValue("ShowOnLockScreen", 1, RegistryValueKind.DWord);
            notifKey?.SetValue("ShowBanners", 1, RegistryValueKind.DWord);
            notifKey?.SetValue("ShowInActionCenter", 1, RegistryValueKind.DWord);
            notifKey?.SetValue("Rank", 1, RegistryValueKind.DWord);
        }
        catch { }
    }
}

// ============================================================================
// ProcessHelper & Win32 Snapshot Detection
// ============================================================================
internal static class ProcessHelper
{
    public static string DetectBrowserSlug(string[] args)
    {
        var browserArg = args.FirstOrDefault(a => a.StartsWith("--browser=", StringComparison.OrdinalIgnoreCase));
        if (browserArg != null)
            return browserArg.Split('=', 2)[1].ToLowerInvariant();

        try
        {
            var parent = GetParentProcessName(Environment.ProcessId);
            if (parent != null)
            {
                if (parent.Contains("brave", StringComparison.OrdinalIgnoreCase)) return "brave";
                if (parent.Contains("msedge", StringComparison.OrdinalIgnoreCase) || parent.Contains("edge", StringComparison.OrdinalIgnoreCase)) return "edge";
                if (parent.Contains("opera", StringComparison.OrdinalIgnoreCase)) return "opera";
                if (parent.Contains("vivaldi", StringComparison.OrdinalIgnoreCase)) return "vivaldi";
                if (parent.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "chrome";
            }
        }
        catch { }

        return "standalone";
    }

    private static string? GetParentProcessName(int pid)
    {
        const uint TH32CS_SNAPPROCESS = 0x00000002;
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero) return null;
        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
            };

            if (!NativeMethods.Process32First(snapshot, ref entry)) return null;

            uint parentPid = 0;
            do
            {
                if (entry.th32ProcessID == (uint)pid)
                {
                    parentPid = entry.th32ParentProcessID;
                    break;
                }
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));

            if (parentPid == 0) return null;

            entry.dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>();
            if (!NativeMethods.Process32First(snapshot, ref entry)) return null;
            do
            {
                if (entry.th32ProcessID == parentPid)
                    return entry.szExeFile;
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));

            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll")] internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll")] internal static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")] internal static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}

internal static class ConsoleRenderer
{
    public static void RenderToLog(IReadOnlyList<DownloadActivity> downloads, Action<string> log)
    {
        foreach (var d in downloads)
        {
            string line;
            switch (d.State)
            {
                case DownloadState.Starting:
                    line = $"[{d.Source}] {d.Filename} | Starting...";
                    break;

                case DownloadState.Downloading:
                    var pct = d.PercentageComplete.HasValue ? $"{(int)Math.Round(d.PercentageComplete.Value)}%" : "??%";
                    var bytes = d.TotalBytes.HasValue ? $"{FormatBytes(d.DownloadedBytes)}/{FormatBytes(d.TotalBytes.Value)}" : $"{FormatBytes(d.DownloadedBytes)} dl";
                    var speed = d.SpeedBytesPerSecond.HasValue ? $" {FormatBytes((long)d.SpeedBytesPerSecond.Value)}/s" : "";
                    var eta = d.EstimatedSecondsRemaining.HasValue ? $" ~{FormatEta(d.EstimatedSecondsRemaining.Value)}" : " Calculating...";
                    line = $"[{d.Source}] {d.Filename} | {pct} {bytes}{speed}{eta}";
                    break;

                case DownloadState.Completed:
                    line = $"[{d.Source}] {d.Filename} | COMPLETE";
                    break;

                case DownloadState.Failed:
                    line = $"[{d.Source}] {d.Filename} | FAILED";
                    break;

                default:
                    line = $"[{d.Source}] {d.Filename} | {d.State}";
                    break;
            }
            log(line);
        }
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
