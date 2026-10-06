using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DownloadTray.Core.Models;

namespace DownloadTray.NativeHost;

/// <summary>
/// System Tray & macOS/iOS Live Activity Quality Desktop Card
/// High-DPI PerMonitorV2 razor-sharp rendering with authentic Live Activity proportions.
/// </summary>
public sealed class TrayService : IDisposable
{
    private NotifyIcon? _trayIcon;
    private Thread? _uiThread;
    private LiveActivityCardForm? _cardForm;
    private readonly AutoResetEvent _readyEvent = new(false);
    private IReadOnlyList<DownloadActivity> _latestDownloads = Array.Empty<DownloadActivity>();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    public TrayService()
    {
        // MUST be called before any UI elements or windows are created
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

        _uiThread = new Thread(() =>
        {
            _cardForm = new LiveActivityCardForm();

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "DownloadTray: Live Activity"
            };

            _trayIcon.Click += (_, e) =>
            {
                if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                {
                    _cardForm?.ToggleVisibility(_latestDownloads);
                }
            };
            _trayIcon.DoubleClick += (_, _) => _cardForm?.ToggleVisibility(_latestDownloads);

            var menu = new ContextMenuStrip();
            var titleItem = menu.Items.Add("DownloadTray");
            titleItem.Font = new Font("Segoe UI Variable Display", 9.5f, FontStyle.Bold);
            titleItem.Enabled = false;
            menu.Items.Add("-");
            menu.Items.Add("Show Activity Card", null, (_, _) => _cardForm?.ShowCard(_latestDownloads));
            menu.Items.Add("Open Downloads Folder", null, (_, _) => OpenDownloadsFolder());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _trayIcon.Visible = false;
                Environment.Exit(0);
            });
            _trayIcon.ContextMenuStrip = menu;

            _readyEvent.Set();
            Application.Run();
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();

        _readyEvent.WaitOne(2000);
    }

    public static void OpenDownloadsFolder()
    {
        try
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = downloadsPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void UpdateDownloads(IReadOnlyList<DownloadActivity> downloads)
    {
        _latestDownloads = downloads;
        if (_trayIcon == null) return;

        var active = downloads.Where(d => d.State is DownloadState.Downloading or DownloadState.Starting).ToList();
        string text;

        if (active.Count == 0)
        {
            text = "DownloadTray: No active downloads";
        }
        else if (active.Count == 1)
        {
            var d = active[0];
            var pct = d.PercentageComplete.HasValue ? $"{(int)Math.Round(d.PercentageComplete.Value)}%" : "Downloading";
            text = $"DownloadTray: {d.Filename} ({pct})";
        }
        else
        {
            text = $"DownloadTray: {active.Count} downloads active";
        }

        if (text.Length > 63) text = text.Substring(0, 60) + "...";

        try { _trayIcon.Text = text; } catch { }

        if (_cardForm != null && _cardForm.Visible)
        {
            _cardForm.UpdateContent(downloads);
        }
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}

/// <summary>
/// Authentic Live Activity Card Form
/// - 20px Corner Radius (Apple Live Activity family)
/// - Dark neutral background (#0B0E14) with hairline 1px border rgba(255,255,255,0.10)
/// - Single unified accent color: iOS System Blue (#0A84FF)
/// - Middle-ellipsis filename truncation (e.g. "hr_dataset...zip")
/// - 3px fully rounded flat progress bar
/// </summary>
internal sealed class LiveActivityCardForm : Form
{
    private IReadOnlyList<DownloadActivity> _downloads = Array.Empty<DownloadActivity>();
    private readonly Dictionary<string, float> _animatedProgress = new();
    private readonly System.Windows.Forms.Timer _animTimer;
    private bool _hoverFolderBtn = false;
    private bool _hoverCloseBtn = false;

    // Single unified accent color across the entire interface
    private static readonly Color AccentColor = Color.FromArgb(10, 132, 255); // #0A84FF (iOS System Blue)
    private static readonly Color SuccessColor = Color.FromArgb(48, 209, 88); // #30D158 (iOS System Green)

    public LiveActivityCardForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BackColor = Color.FromArgb(11, 14, 20); // #0B0E14 Dark neutral
        Width = 400;
        Height = 220;

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps smooth bar animation
        _animTimer.Tick += OnAnimationTick;
        _animTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x20000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        bool needsRepaint = false;
        foreach (var d in _downloads)
        {
            float target = d.PercentageComplete.HasValue ? Math.Clamp((float)(d.PercentageComplete.Value / 100.0), 0f, 1f) : 0f;
            if (!_animatedProgress.TryGetValue(d.Id, out float current))
            {
                current = 0f;
            }

            if (Math.Abs(current - target) > 0.002f)
            {
                // Ease-out transition towards target
                _animatedProgress[d.Id] = current + (target - current) * 0.15f;
                needsRepaint = true;
            }
            else
            {
                _animatedProgress[d.Id] = target;
            }
        }

        if (needsRepaint && Visible)
        {
            Invalidate();
        }
    }

    public void ToggleVisibility(IReadOnlyList<DownloadActivity> downloads)
    {
        if (InvokeRequired) { Invoke(new Action(() => ToggleVisibility(downloads))); return; }
        if (Visible) Hide(); else ShowCard(downloads);
    }

    public void ShowCard(IReadOnlyList<DownloadActivity> downloads)
    {
        if (InvokeRequired) { Invoke(new Action(() => ShowCard(downloads))); return; }
        _downloads = downloads;
        ResizeToFit();
        PositionBottomRight();
        Show();
        BringToFront();
        Invalidate();
    }

    public void UpdateContent(IReadOnlyList<DownloadActivity> downloads)
    {
        if (InvokeRequired) { Invoke(new Action(() => UpdateContent(downloads))); return; }
        _downloads = downloads;
        ResizeToFit();
        PositionBottomRight();
        Invalidate();
    }

    private void ResizeToFit()
    {
        int count = Math.Max(1, _downloads.Count);
        // Header (38px) + rows (76px + 12px gap each) + quiet footer (44px) + padding (16px)
        Height = 44 + (count * 78) + 48;
        if (Height > 540) Height = 540;
    }

    private void PositionBottomRight()
    {
        var screen = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(screen.Right - Width - 16, screen.Bottom - Height - 16);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        bool oldClose = _hoverCloseBtn;
        bool oldFolder = _hoverFolderBtn;

        _hoverCloseBtn = (e.X >= Width - 38 && e.Y <= 38);
        int footerY = Height - 40;
        _hoverFolderBtn = (e.Y >= footerY && e.Y <= footerY + 28 && e.X >= 16 && e.X <= Width - 16);

        if (oldClose != _hoverCloseBtn || oldFolder != _hoverFolderBtn)
        {
            Cursor = (_hoverCloseBtn || _hoverFolderBtn) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverCloseBtn = false;
        _hoverFolderBtn = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (_hoverCloseBtn)
        {
            Hide();
            return;
        }

        if (_hoverFolderBtn)
        {
            TrayService.OpenDownloadsFolder();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var cardRect = new Rectangle(0, 0, Width - 1, Height - 1);

        // 20px Corner Radius Path (Apple Live Activity family)
        using (var cardPath = CreateRoundedRectangle(cardRect, 20))
        {
            // Dark Neutral Background (#0B0E14)
            using (var bgBrush = new SolidBrush(Color.FromArgb(11, 14, 20)))
                g.FillPath(bgBrush, cardPath);

            // Hairline 1px Border rgba(255, 255, 255, 0.10)
            using (var borderPen = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
                g.DrawPath(borderPen, cardPath);
        }

        // Header: Glyph + Title
        using (var titleFont = new Font("Segoe UI Variable Display", 10.5f, FontStyle.Bold))
        using (var titleBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) // 92% opacity
            g.DrawString("Live Download Activity", titleFont, titleBrush, 18, 14);

        // Plain Close (×) Button (16px hit target minimum, no button chrome)
        using (var closeFont = new Font("Segoe UI", 11f, FontStyle.Regular))
        using (var closeBrush = new SolidBrush(_hoverCloseBtn ? Color.White : Color.FromArgb(140, 255, 255, 255)))
            g.DrawString("×", closeFont, closeBrush, Width - 28, 12);

        int y = 46;

        if (_downloads.Count == 0)
        {
            using (var emptyFont = new Font("Segoe UI", 9f))
            using (var emptyBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                g.DrawString("No active downloads", emptyFont, emptyBrush, 18, y + 12);
        }
        else
        {
            foreach (var d in _downloads)
            {
                DrawDownloadRow(g, d, y);
                y += 78;
            }
        }

        // Quiet Footer Button: rgba(255,255,255,0.06), 8px radius, 13px medium text
        int footerY = Height - 40;
        var btnRect = new Rectangle(16, footerY, Width - 32, 26);
        using (var btnPath = CreateRoundedRectangle(btnRect, 8))
        using (var btnBg = new SolidBrush(_hoverFolderBtn ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(15, 255, 255, 255)))
        {
            g.FillPath(btnBg, btnPath);
        }

        using (var btnFont = new Font("Segoe UI Variable Text", 9f, FontStyle.Regular))
        using (var btnBrush = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
        {
            string btnText = "Open Downloads folder";
            var sz = g.MeasureString(btnText, btnFont);
            g.DrawString(btnText, btnFont, btnBrush, (Width - sz.Width) / 2, footerY + 5);
        }
    }

    private void DrawDownloadRow(Graphics g, DownloadActivity d, int y)
    {
        // 1. Monochrome 20px circle glyph with down-arrow tinted #0A84FF
        int iconX = 18;
        int iconY = y + 2;
        int iconSize = 20;

        Color glyphColor = d.State == DownloadState.Completed ? SuccessColor : AccentColor;

        using (var glyphBg = new SolidBrush(Color.FromArgb(30, glyphColor.R, glyphColor.G, glyphColor.B)))
            g.FillEllipse(glyphBg, iconX, iconY, iconSize, iconSize);

        using (var glyphPen = new Pen(glyphColor, 1.5f))
        {
            if (d.State == DownloadState.Completed)
            {
                // Checkmark
                g.DrawLine(glyphPen, iconX + 5, iconY + 10, iconX + 8, iconY + 14);
                g.DrawLine(glyphPen, iconX + 8, iconY + 14, iconX + 15, iconY + 6);
            }
            else
            {
                // Down arrow
                g.DrawLine(glyphPen, iconX + 10, iconY + 5, iconX + 10, iconY + 14);
                g.DrawLine(glyphPen, iconX + 6, iconY + 10, iconX + 10, iconY + 14);
                g.DrawLine(glyphPen, iconX + 14, iconY + 10, iconX + 10, iconY + 14);
            }
        }

        // 2. Filename (14px Semibold, white at 92% opacity, middle-ellipsis truncation)
        string middleTruncatedName = MiddleTruncate(d.Filename, 28);
        using (var nameFont = new Font("Segoe UI Variable Display", 9.75f, FontStyle.Bold))
        using (var nameBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) // 92%
        {
            g.DrawString(middleTruncatedName, nameFont, nameBrush, 46, y + 2);
        }

        // 3. Percentage (14px Semibold, tabular figures, white at 92%, right-aligned on same line)
        if (d.PercentageComplete.HasValue)
        {
            int pct = (int)Math.Round(d.PercentageComplete.Value);
            string pctText = $"{pct}%";
            using (var pctFont = new Font("Segoe UI Variable Display", 9.75f, FontStyle.Bold))
            using (var pctBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) // 92%
            {
                var sz = g.MeasureString(pctText, pctFont);
                g.DrawString(pctText, pctFont, pctBrush, Width - 18 - sz.Width, y + 2);
            }
        }

        // 4. Progress Bar Track: 3px tall, fully rounded, background rgba(255, 255, 255, 0.12)
        int pbX = 46;
        int pbY = y + 24;
        int pbW = Width - 64;
        const int pbH = 3;

        using (var trackPath = CreateRoundedRectangle(new Rectangle(pbX, pbY, pbW, pbH), 2))
        using (var trackBrush = new SolidBrush(Color.FromArgb(31, 255, 255, 255))) // 12% opacity
        {
            g.FillPath(trackBrush, trackPath);
        }

        // 5. Progress Bar Fill: Single flat accent color (#0A84FF), eased transition width
        if (!_animatedProgress.TryGetValue(d.Id, out float ratio))
        {
            ratio = d.PercentageComplete.HasValue ? (float)(d.PercentageComplete.Value / 100.0) : 0f;
        }

        int fillW = Math.Clamp((int)(pbW * ratio), 0, pbW);
        if (fillW > 0)
        {
            using (var fillPath = CreateRoundedRectangle(new Rectangle(pbX, pbY, fillW, pbH), 2))
            using (var fillBrush = new SolidBrush(d.State == DownloadState.Completed ? SuccessColor : AccentColor))
            {
                g.FillPath(fillBrush, fillPath);
            }
        }

        // 6. Subtext Line: 12px Regular, white at 55% opacity, natural words
        string subText;
        if (d.State == DownloadState.Completed)
        {
            subText = $"Completed ({FormatBytes(d.DownloadedBytes)})";
        }
        else
        {
            var parts = new List<string>();
            if (d.TotalBytes.HasValue)
                parts.Add($"{FormatBytes(d.DownloadedBytes)} of {FormatBytes(d.TotalBytes.Value)}");
            else
                parts.Add($"{FormatBytes(d.DownloadedBytes)} downloaded");

            if (d.SpeedBytesPerSecond.HasValue)
                parts.Add($"at {FormatBytes((long)d.SpeedBytesPerSecond.Value)}/s");

            if (d.EstimatedSecondsRemaining.HasValue)
                parts.Add($"about {FormatEta(d.EstimatedSecondsRemaining.Value)} remaining");

            subText = string.Join(", ", parts);
        }

        using (var subFont = new Font("Segoe UI Variable Text", 8.25f, FontStyle.Regular))
        using (var subBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255))) // 55% opacity
        {
            g.DrawString(subText, subFont, subBrush, 46, y + 33);
        }
    }

    /// <summary>
    /// Middle-ellipsis filename truncation (e.g. "hr_dataset_very_long...zip")
    /// Preserves the extension which is vital for file downloads.
    /// </summary>
    private static string MiddleTruncate(string filename, int maxLength)
    {
        if (string.IsNullOrEmpty(filename) || filename.Length <= maxLength)
            return filename;

        string ext = Path.GetExtension(filename);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

        int keepStart = (maxLength - ext.Length - 3) / 2;
        int keepEnd = (maxLength - ext.Length - 3) - keepStart;

        if (keepStart <= 0) return filename.Substring(0, maxLength - 3) + "...";

        string start = nameWithoutExt.Substring(0, Math.Min(keepStart, nameWithoutExt.Length));
        string end = nameWithoutExt.Length > keepEnd ? nameWithoutExt.Substring(nameWithoutExt.Length - keepEnd) : "";

        return $"{start}...{end}{ext}";
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
