using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Platform.Capture;
using Winly.Platform.Overlay;

namespace Winly.App.Overlay;

/// <summary>One transparent, topmost, click-through window covering exactly one monitor (FR-023, FR-024, research.md §4).</summary>
public partial class CompanionOverlayWindow : Window
{
    private const double RestMargin = 24;
    private const double TaskbarAllowance = 48;
    private readonly MonitorDescription _monitor;

    public CompanionOverlayWindow(MonitorDescription monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        // Approximate DIP placement so WPF creates the HWND on the right monitor; exact physical placement follows once it exists.
        Left = monitor.Geometry.OriginXPx / monitor.Geometry.DpiScale;
        Top = monitor.Geometry.OriginYPx / monitor.Geometry.DpiScale;
        Width = monitor.Geometry.WidthPx / monitor.Geometry.DpiScale;
        Height = monitor.Geometry.HeightPx / monitor.Geometry.DpiScale;
    }

    public string MonitorId => _monitor.MonitorId;

    public MonitorGeometry Geometry => _monitor.Geometry;

    public bool BuddyVisible
    {
        get => Buddy.Visibility == Visibility.Visible;
        set => Buddy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        var handle = new WindowInteropHelper(this).Handle;
        OverlayWindowStyleApplier.MakeClickThroughAndNonActivating(handle);
        OverlayWindowStyleApplier.PlaceOverMonitor(handle, _monitor.Geometry);
    }

    public void SetState(CompanionState state) => Buddy.SetState(state);

    public void MoveBuddyToRest(bool animate)
    {
        Buddy.SetPointing(false);
        MoveBuddy(RestPosition, animate);
    }

    /// <summary>Hovers the buddy just above the location so the pointer tip touches it, kept inside this monitor.</summary>
    public void PointBuddyAt(double dipX, double dipY)
    {
        Buddy.SetPointing(true);
        var destination = new Point(
            Math.Clamp(dipX - Buddy.Width / 2, 0, Math.Max(0, SurfaceWidth - Buddy.Width)),
            Math.Clamp(dipY - Buddy.Height - 2, 0, Math.Max(0, SurfaceHeight - Buddy.Height)));
        MoveBuddy(destination, animate: true);
    }

    private double SurfaceWidth => ActualWidth > 0 ? ActualWidth : Width;

    private double SurfaceHeight => ActualHeight > 0 ? ActualHeight : Height;

    private Point RestPosition => new(SurfaceWidth - Buddy.Width - RestMargin, SurfaceHeight - Buddy.Height - RestMargin - TaskbarAllowance);

    private void MoveBuddy(Point destination, bool animate)
    {
        var currentLeft = Canvas.GetLeft(Buddy);
        var currentTop = Canvas.GetTop(Buddy);
        var current = double.IsNaN(currentLeft) || double.IsNaN(currentTop) ? RestPosition : new Point(currentLeft, currentTop);
        if (animate && BuddyVisible)
        {
            PointingAnimator.Travel(Buddy, current, destination);
        }
        else
        {
            PointingAnimator.Place(Buddy, destination);
        }
    }
}
