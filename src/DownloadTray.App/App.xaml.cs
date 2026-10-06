using DownloadTray.App.Services;
using DownloadTray.Core;
using DownloadTray.Core.Sources;
using Microsoft.UI.Xaml;

namespace DownloadTray.App;

/// <summary>
/// Application entry point.
/// 
/// On launch: start the native messaging host listener, wire it to the tracker,
/// show a tray icon only (no main window). The UI surfaces (activity card, tray
/// flyout, notifications) are managed by their respective services.
/// 
/// BUILD-SPEC reference: Sections 5, 9, 11.
/// </summary>
public partial class App : Application
{
    private Window? _activityCardWindow;
    private TrayIconService? _trayService;
    private DownloadTracker? _tracker;
    private ChromiumExtensionDownloadSource? _source;
    private NotificationService? _notificationService;
    private CancellationTokenSource _cts = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Run with no visible main window — tray icon only (BUILD-SPEC §7, §9).
        _ = StartServicesAsync();
    }

    private async Task StartServicesAsync()
    {
        // Create the core tracker.
        _tracker = new DownloadTracker();

        // Create the download source (native messaging from the browser extension).
        _source = new ChromiumExtensionDownloadSource(
            "chromium",
            Console.OpenStandardInput(),
            Console.OpenStandardOutput());

        // Wire source → tracker.
        _source.DownloadChanged += (_, activity) =>
        {
            _tracker.Apply(activity);
            OnDownloadsUpdated();
        };

        // Initialize UI services.
        _trayService = new TrayIconService(_tracker);
        _notificationService = new NotificationService(_tracker);

        await _trayService.InitializeAsync();
        await _notificationService.InitializeAsync();

        // Start listening (blocks until the channel is closed).
        try
        {
            await _source.StartAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Extension not connected — show a friendly state in tray.
            _trayService.SetExtensionDisconnected(ex.Message);
        }
    }

    private void OnDownloadsUpdated()
    {
        // Dispatch UI updates on the main thread.
        DispatcherQueue.TryEnqueue(() =>
        {
            var downloads = _tracker!.GetSorted();
            _trayService?.UpdateDownloads(downloads);
            _notificationService?.UpdateDownloads(downloads);

            // Show/hide the activity card window based on active downloads.
            if (downloads.Any(d => d.State is
                    DownloadTray.Core.Models.DownloadState.Downloading or
                    DownloadTray.Core.Models.DownloadState.Starting))
            {
                EnsureActivityCardVisible();
            }
        });
    }

    private void EnsureActivityCardVisible()
    {
        if (_activityCardWindow == null || !_activityCardWindow.Visible)
        {
            _activityCardWindow = new ActivityCard.ActivityCardWindow(_tracker!);
            _activityCardWindow.Activate();
        }
    }

    private Window? _window;
    // Expose a hidden main window so the app stays alive as a background process.
    public Window? MainWindow => _window;
}
