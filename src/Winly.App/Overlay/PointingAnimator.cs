using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Winly.App.Overlay;

/// <summary>Moves the buddy along an upward Bézier arc rather than jumping (FR-019).</summary>
public static class PointingAnimator
{
    public static readonly Duration TravelDuration = new(TimeSpan.FromMilliseconds(650));
    private const double ArcLift = 120;

    public static void Travel(UIElement element, Point from, Point to)
    {
        var apex = Math.Min(from.Y, to.Y) - ArcLift;
        var firstControl = new Point(from.X + (to.X - from.X) * 0.25, apex);
        var secondControl = new Point(from.X + (to.X - from.X) * 0.75, apex);
        var path = new PathGeometry([new PathFigure(from, [new BezierSegment(firstControl, secondControl, to, isStroked: false)], closed: false)]);
        path.Freeze();

        element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimationUsingPath { PathGeometry = path, Source = PathAnimationSource.X, Duration = TravelDuration });
        element.BeginAnimation(Canvas.TopProperty, new DoubleAnimationUsingPath { PathGeometry = path, Source = PathAnimationSource.Y, Duration = TravelDuration });
    }

    public static void Place(UIElement element, Point at)
    {
        element.BeginAnimation(Canvas.LeftProperty, null);
        element.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(element, at.X);
        Canvas.SetTop(element, at.Y);
    }
}
