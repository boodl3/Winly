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
    /// <summary>Where the buddy's top-left sits relative to the cursor hotspot, in DIPs: down and to the
    /// right, far enough back that it trails the pointer rather than riding on it. Tuning value — this is
    /// the follow distance, and it is separate from how tightly the buddy closes on it (FollowRate).</summary>
    private static readonly Vector CursorOffset = new(46, 40);

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

    /// <summary>Eases the buddy toward the cursor and returns where it landed. <paramref name="smoothing"/>
    /// is the fraction of the remaining distance closed this frame; 1 snaps.</summary>
    public void FollowCursor(double dipX, double dipY, double smoothing)
    {
        Buddy.SetPointing(false);
        var target = Clamp(dipX + CursorOffset.X, dipY + CursorOffset.Y);
        var current = CurrentPosition ?? target;
        // Set directly rather than animating: one animation per frame would fight the next frame's.
        PointingAnimator.Place(Buddy, new Point(
            current.X + ((target.X - current.X) * smoothing),
            current.Y + ((target.Y - current.Y) * smoothing)));
    }

    /// <summary>Hovers the buddy just above the location so the pointer tip touches it, kept inside this monitor.</summary>
    public void PointBuddyAt(double dipX, double dipY)
    {
        Buddy.SetPointing(true);
        var destination = Clamp(dipX - (Buddy.Width / 2), dipY - Buddy.Height - 2);
        PointingAnimator.Travel(Buddy, CurrentPosition ?? destination, destination);
    }

    private double SurfaceWidth => ActualWidth > 0 ? ActualWidth : Width;

    private double SurfaceHeight => ActualHeight > 0 ? ActualHeight : Height;

    private Point? CurrentPosition
    {
        get
        {
            var (left, top) = (Canvas.GetLeft(Buddy), Canvas.GetTop(Buddy));
            return double.IsNaN(left) || double.IsNaN(top) ? null : new Point(left, top);
        }
    }

    /// <summary>Keeps the whole buddy on this monitor, so it is never half off an edge.</summary>
    private Point Clamp(double dipX, double dipY) => new(
        Math.Clamp(dipX, 0, Math.Max(0, SurfaceWidth - Buddy.Width)),
        Math.Clamp(dipY, 0, Math.Max(0, SurfaceHeight - Buddy.Height)));
}
