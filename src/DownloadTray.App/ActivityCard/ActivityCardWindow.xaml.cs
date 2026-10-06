using DownloadTray.Core;
using DownloadTray.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DownloadTray.App.ActivityCard;

/// <summary>
/// Always-on-top, borderless activity card window.
/// Anchors to the bottom-right corner of the primary display (near the system tray).
/// BUILD-SPEC reference: Section 9 (surface #2), Section 5.
/// </summary>
public sealed partial class ActivityCardWindow : Window
{
    private readonly DownloadTracker _tracker;
    private readonly DispatcherTimer _dismissTimer = new();

    public ActivityCardWindow(DownloadTracker tracker)
    {
        _tracker = tracker;
        InitializeComponent();
        ConfigureWindow();

        // Auto-dismiss timer (checks if all downloads completed).
        _dismissTimer.Interval = TimeSpan.FromSeconds(8);
        _dismissTimer.Tick += DismissTimerTick;

        // Wire tracker updates.
        tracker.ActivityUpdated += OnActivityUpdated;
        Closed += (_, _) => tracker.ActivityUpdated -= OnActivityUpdated;

        UpdateDownloadList(_tracker.GetSorted());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Window configuration
    // ─────────────────────────────────────────────────────────────────────────
    private void ConfigureWindow()
    {
        var appWindow = AppWindow;

        // Remove title bar and chrome.
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // Borderless, always on top.
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false; // Don't show in Alt+Tab

        // Position: bottom-right of primary display, above the taskbar.
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var displayArea = DisplayArea.Primary;
        var workArea = displayArea.WorkArea;

        const int margin = 12;
        const int cardWidth = 360;
        const int cardHeight = 200; // initial estimate

        var x = workArea.Width - cardWidth - margin;
        var y = workArea.Height - cardHeight - margin;

        AppWindow.Move(new PointInt32(x, y));
        AppWindow.Resize(new SizeInt32(cardWidth, cardHeight));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Download list rendering
    // ─────────────────────────────────────────────────────────────────────────
    private void OnActivityUpdated(object? sender, DownloadActivity activity)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateDownloadList(_tracker.GetSorted());
        });
    }

    private void UpdateDownloadList(IReadOnlyList<DownloadActivity> downloads)
    {
        var viewModels = downloads.Select(d => new DownloadCardViewModel(d)).ToList();
        DownloadsList.ItemsSource = viewModels;

        // Toggle empty state.
        EmptyState.Visibility = downloads.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Resize window to fit content.
        ResizeToContent(downloads.Count);

        // Start dismiss timer when all downloads are completed.
        bool allDone = downloads.All(d =>
            d.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Unknown);

        if (downloads.Count > 0 && allDone)
            _dismissTimer.Start();
        else
            _dismissTimer.Stop();
    }

    private void ResizeToContent(int count)
    {
        const int headerHeight = 56;
        const int cardHeight = 88;
        const int padding = 16;
        const int maxHeight = 400;

        int height = Math.Min(headerHeight + (count * cardHeight) + padding, maxHeight);
        if (count == 0) height = headerHeight + 60;

        var displayArea = DisplayArea.Primary;
        var workArea = displayArea.WorkArea;
        const int margin = 12;
        const int cardWidth = 360;

        AppWindow.Resize(new SizeInt32(cardWidth, height));
        AppWindow.Move(new PointInt32(
            workArea.Width - cardWidth - margin,
            workArea.Height - height - margin));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auto-dismiss
    // ─────────────────────────────────────────────────────────────────────────
    private void DismissTimerTick(object? sender, object e)
    {
        _dismissTimer.Stop();
        Close();
    }

    public bool Visible => AppWindow.IsVisible;

    public void SetExtensionDisconnected()
    {
        ExtensionStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.OrangeRed);
        ExtensionStatusText.ToolTipService.SetToolTip(ExtensionStatusText,
            "Browser extension not connected — install the DownloadTray extension in Brave/Chrome/Edge");
    }
}
