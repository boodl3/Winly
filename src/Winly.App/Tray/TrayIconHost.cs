using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Controls;
using H.NotifyIcon;
using Serilog;

namespace Winly.App.Tray;

/// <summary>The tray entry — the app's only persistent chrome (FR-001). Icons are drawn here, not shipped as assets.</summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly Icon _idleIcon = DrawIcon(captureActive: false);
    private readonly Icon _captureActiveIcon = DrawIcon(captureActive: true);

    public TrayIconHost(Action openPanel, Action exit)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open Winly" };
        open.Click += (_, _) => openPanel();
        var quit = new MenuItem { Header = "Exit" };
        quit.Click += (_, _) => exit();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);

        _icon = new TaskbarIcon { ToolTipText = "Winly", Icon = _idleIcon, ContextMenu = menu };
        _icon.TrayLeftMouseUp += (_, _) => openPanel();
        _icon.ForceCreate();
    }

    /// <summary>Glanceable capture indicator (FR-027): a red dot on the icon while the mic or screen is being captured.</summary>
    public void SetCaptureActive(bool active)
    {
        _icon.Icon = active ? _captureActiveIcon : _idleIcon;
        try
        {
            _icon.ToolTipText = active ? "Winly — capturing" : "Winly";
        }
        catch (InvalidOperationException exception)
        {
            // Shell_NotifyIcon refuses the update while the tray is busy. The icon itself already
            // carries the indicator, so a missed tooltip is not worth a log entry per activation.
            Log.Debug(exception, "Tray tooltip could not be updated");
        }
    }

    public void Notify(string title, string message) => _icon.ShowNotification(title, message);

    public static async Task ShowAlreadyRunningNotification()
    {
        using var host = new TrayIconHost(() => { }, () => { });
        host.Notify("Winly", "Winly is already running.");
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    public void Dispose()
    {
        _icon.Dispose();
        _idleIcon.Dispose();
        _captureActiveIcon.Dispose();
    }

    private static Icon DrawIcon(bool captureActive)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var bodyBrush = new SolidBrush(Color.FromArgb(242, 180, 65));
        using var body = new GraphicsPath();
        body.AddArc(2, 2, 12, 12, 180, 90);
        body.AddArc(18, 2, 12, 12, 270, 90);
        body.AddArc(18, 18, 12, 12, 0, 90);
        body.AddArc(2, 18, 12, 12, 90, 90);
        body.CloseFigure();
        graphics.FillPath(bodyBrush, body);
        graphics.FillEllipse(Brushes.Black, 8, 10, 5, 8);
        graphics.FillEllipse(Brushes.Black, 19, 10, 5, 8);
        if (captureActive)
        {
            graphics.FillEllipse(Brushes.Red, 20, 20, 11, 11);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
