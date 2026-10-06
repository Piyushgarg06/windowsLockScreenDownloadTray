using DownloadTray.Core;
using DownloadTray.Core.Models;
using H.NotifyIcon;

namespace DownloadTray.App.Services;

/// <summary>
/// Manages the system tray icon and its right-click/hover flyout.
/// Uses H.NotifyIcon.WinUI for WinUI 3 tray support.
/// BUILD-SPEC reference: Section 9 (surface #1).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly DownloadTracker _tracker;

    public TrayIconService(DownloadTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task InitializeAsync()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri("ms-appx:///Assets/AppIcon.ico")),
            ToolTipText = "DownloadTray — no active downloads",
            ContextMenuMode = ContextMenuMode.PopupMenu,
        };

        // Double-click shows the activity card.
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowActivityCard();

        await Task.CompletedTask;
    }

    public void UpdateDownloads(IReadOnlyList<DownloadActivity> downloads)
    {
        if (_trayIcon == null) return;

        var active = downloads
            .Where(d => d.State is DownloadState.Downloading or DownloadState.Starting)
            .ToList();

        if (active.Count == 0)
        {
            _trayIcon.ToolTipText = "DownloadTray — no active downloads";
        }
        else if (active.Count == 1)
        {
            var d = active[0];
            var pct = d.PercentageComplete.HasValue
                ? $" {(int)Math.Round(d.PercentageComplete.Value)}%"
                : string.Empty;
            _trayIcon.ToolTipText = $"↓ {d.Filename}{pct}";
        }
        else
        {
            _trayIcon.ToolTipText = $"↓ {active.Count} downloads active";
        }
    }

    public void SetExtensionDisconnected(string reason)
    {
        if (_trayIcon != null)
            _trayIcon.ToolTipText = "DownloadTray — extension not connected";
    }

    private void ShowActivityCard()
    {
        // The activity card is managed by App.xaml.cs; this just signals intent.
        // In a real implementation we'd raise an event or use a shared service.
    }

    public void Dispose() => _trayIcon?.Dispose();
}
