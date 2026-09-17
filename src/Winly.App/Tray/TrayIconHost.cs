using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Controls;
using H.NotifyIcon;
using Serilog;

namespace Winly.App.Tray;

/// <summary>The tray entry — the app's only persistent chrome (FR-001). Icons are drawn here, not shipped as assets.</summary>
public sealed class TrayIconHost : IDisposable
{
    // H.NotifyIcon's TaskbarIcon.Id defaults to an all-zero Guid, and Windows keys an icon's shell
    // registration off (exe path, Id) - so two TaskbarIcons from this same exe with unset Id are the
    // *same* registration. ShowAlreadyRunningNotification used to construct one of those on every
    // failed second launch, and its Dispose() (NIM_DELETE) then deleted the live instance's icon out
    // from under it a few seconds later. Two distinct fixed GUIDs keep the two icons apart.
    private static readonly Guid MainIconId = new("6f2b6a2b-3c0a-4b7b-9a5b-6a7a6a2b6a01");
    private static readonly Guid NotificationIconId = new("6f2b6a2b-3c0a-4b7b-9a5b-6a7a6a2b6a02");

    private readonly TaskbarIcon _icon;
    private readonly Icon _idleIcon = DrawIcon(captureActive: false);
    private readonly Icon _captureActiveIcon = DrawIcon(captureActive: true);

    public TrayIconHost(Action openPanel, Action exit) : this(openPanel, exit, MainIconId)
    {
    }

    private TrayIconHost(Action openPanel, Action exit, Guid id)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open Winly" };
        open.Click += (_, _) => openPanel();
        var quit = new MenuItem { Header = "Exit" };
        quit.Click += (_, _) => exit();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);

        _icon = new TaskbarIcon { Id = id, ToolTipText = "Winly", Icon = _idleIcon, ContextMenu = menu };
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
        using var host = new TrayIconHost(() => { }, () => { }, NotificationIconId);
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
        using var bodyBrush = new SolidBrush(Color.FromArgb(27, 26, 22));
        using var eyeBrush = new SolidBrush(Color.FromArgb(242, 239, 232));
        using var body = new GraphicsPath();
        body.AddArc(2, 2, 12, 12, 180, 90);
        body.AddArc(18, 2, 12, 12, 270, 90);
        body.AddArc(18, 18, 12, 12, 0, 90);
        body.AddArc(2, 18, 12, 12, 90, 90);
        body.CloseFigure();
        graphics.FillPath(bodyBrush, body);
        graphics.FillEllipse(eyeBrush, 8, 10, 5, 8);
        graphics.FillEllipse(eyeBrush, 19, 10, 5, 8);
        if (captureActive)
        {
            using var captureBrush = new SolidBrush(Color.FromArgb(229, 72, 77));
            graphics.FillEllipse(captureBrush, 20, 20, 11, 11);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
