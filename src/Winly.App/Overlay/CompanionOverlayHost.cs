using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;
using Winly.Core.Abstractions;
using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Core.Settings;
using Winly.Platform.Capture;

namespace Winly.App.Overlay;

/// <summary>
/// <see cref="ICompanionOverlay"/> over one <see cref="CompanionOverlayWindow"/> per monitor. Every
/// member marshals to the UI thread and swallows failures, as the contract requires.
/// </summary>
public sealed class CompanionOverlayHost : ICompanionOverlay, IDisposable
{
    private static readonly TimeSpan WithdrawDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>How fast the buddy closes on the cursor, per second, as an exponential approach. Higher
    /// is tighter; ~28 leaves a touch of lag so the follow reads as motion rather than a stuck sprite.
    /// Tuning value — it is a feel, not a measurement.</summary>
    private const double FollowRate = 28;

    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Dictionary<string, CompanionOverlayWindow> _windows = new();
    private readonly DispatcherTimer _withdrawTimer;
    private string? _activeMonitorId;
    private CompanionState _state = CompanionState.Idle;
    private CompanionVisibilityMode _visibilityMode;
    private TimeSpan _lastFrame;
    private bool _snapNextFrame = true;
    private bool _pointing;
    private (int X, int Y) _cursorPx;

    public CompanionOverlayHost(CompanionVisibilityMode visibilityMode)
    {
        _visibilityMode = visibilityMode;
        _withdrawTimer = new DispatcherTimer(WithdrawDelay, DispatcherPriority.Normal, (_, _) => { _withdrawTimer!.Stop(); ApplyVisibility(); }, _dispatcher);
        _withdrawTimer.Stop();
        RebuildWindows();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        CompositionTarget.Rendering += OnRendering;
    }

    public CompanionVisibilityMode VisibilityMode
    {
        get => _visibilityMode;
        set
        {
            _visibilityMode = value;
            _dispatcher.InvokeAsync(ApplyVisibility);
        }
    }

    public void SetState(CompanionState state) => _dispatcher.InvokeAsync(() => Guarded(() =>
    {
        _state = state;
        foreach (var window in _windows.Values)
        {
            window.SetState(state);
        }

        ApplyVisibility();
    }));

    public Task PointTo(DesktopLocation location, CancellationToken cancellationToken) => _dispatcher.InvokeAsync(() => Guarded(() =>
    {
        if (!_windows.TryGetValue(location.MonitorId, out var target))
        {
            return; // That display is gone: a no-op by contract.
        }

        if (_activeMonitorId != location.MonitorId)
        {
            ActivateMonitor(location.MonitorId);
        }

        _pointing = true; // Hold off the cursor follow until ReturnToRest.
        target.PointBuddyAt(location.MonitorDipX, location.MonitorDipY);
    })).Task;

    /// <summary>Releases the pointing hold; the follow loop glides the buddy back to the cursor.</summary>
    public void ReturnToRest() => _dispatcher.InvokeAsync(() => Guarded(() => _pointing = false));

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private CompanionOverlayWindow? ActiveWindow => _activeMonitorId is null ? null : _windows.GetValueOrDefault(_activeMonitorId);

    /// <summary>Follows the cursor every frame (FR-025), including while idle. Exponential approach
    /// against the real frame delta, so the motion is identical at 60 Hz and 165 Hz.</summary>
    private void OnRendering(object? sender, EventArgs eventArgs)
    {
        if (eventArgs is not RenderingEventArgs rendering)
        {
            return;
        }

        var elapsed = rendering.RenderingTime - _lastFrame;
        _lastFrame = rendering.RenderingTime; // Kept current even while pointing, so the glide back starts from one frame, not from however long the point lasted.
        if (_pointing || elapsed <= TimeSpan.Zero)
        {
            return; // WPF raises Rendering more than once for the same frame.
        }

        if (MonitorEnumerator.TryGetCursorPositionPx(out var x, out var y))
        {
            _cursorPx = (x, y); // Keep the last known position when the secure desktop hides it.
        }

        Guarded(() =>
        {
            var window = _windows.Values.FirstOrDefault(candidate => Contains(candidate.Geometry, _cursorPx));
            if (window is null)
            {
                return;
            }

            if (window.MonitorId != _activeMonitorId)
            {
                ActivateMonitor(window.MonitorId); // Crossing monitors hands the buddy to the next window.
            }

            var smoothing = _snapNextFrame ? 1 : 1 - Math.Exp(-FollowRate * Math.Min(elapsed.TotalSeconds, 0.25));
            _snapNextFrame = false;
            window.FollowCursor(
                (_cursorPx.X - window.Geometry.OriginXPx) / window.Geometry.DpiScale,
                (_cursorPx.Y - window.Geometry.OriginYPx) / window.Geometry.DpiScale,
                smoothing);
        });
    }

    private static bool Contains(MonitorGeometry geometry, (int X, int Y) point) =>
        point.X >= geometry.OriginXPx && point.X < geometry.OriginXPx + geometry.WidthPx &&
        point.Y >= geometry.OriginYPx && point.Y < geometry.OriginYPx + geometry.HeightPx;

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) => _dispatcher.InvokeAsync(() => Guarded(RebuildWindows));

    private void RebuildWindows()
    {
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
        var monitors = MonitorEnumerator.Enumerate();
        foreach (var monitor in monitors)
        {
            var window = new CompanionOverlayWindow(monitor);
            _windows[monitor.MonitorId] = window;
            window.Show();
            window.SetState(_state);
        }

        _activeMonitorId = null;
        ActivateMonitor((monitors.FirstOrDefault(monitor => monitor.ContainsCursor) ?? monitors.FirstOrDefault())?.MonitorId);
        ApplyVisibility();
    }

    private void ActivateMonitor(string? monitorId)
    {
        if (monitorId is null || !_windows.ContainsKey(monitorId))
        {
            return;
        }

        if (ActiveWindow is { } previous)
        {
            previous.BuddyVisible = false;
        }

        _activeMonitorId = monitorId;
        _snapNextFrame = true; // Place it under the cursor rather than flying it in from the old monitor.
        ApplyVisibility();
    }

    /// <summary>FR-025: always visible, or shown only for the interaction and withdrawn shortly after it ends.</summary>
    private void ApplyVisibility()
    {
        var interacting = _state != CompanionState.Idle;
        if (_visibilityMode == CompanionVisibilityMode.InteractionOnly && !interacting && _withdrawTimer.IsEnabled)
        {
            return; // Withdraw when the timer fires.
        }

        var shouldShow = _visibilityMode == CompanionVisibilityMode.AlwaysVisible || interacting;
        foreach (var (monitorId, window) in _windows)
        {
            window.BuddyVisible = shouldShow && monitorId == _activeMonitorId;
        }

        if (_visibilityMode == CompanionVisibilityMode.InteractionOnly && !interacting)
        {
            return;
        }

        if (_visibilityMode == CompanionVisibilityMode.InteractionOnly && interacting)
        {
            _withdrawTimer.Stop();
        }
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Overlay update failed");
        }
    }
}
