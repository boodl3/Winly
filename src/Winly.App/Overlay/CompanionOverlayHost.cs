using System.Windows;
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

    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Dictionary<string, CompanionOverlayWindow> _windows = new();
    private readonly DispatcherTimer _withdrawTimer;
    private string? _activeMonitorId;
    private CompanionState _state = CompanionState.Idle;
    private CompanionVisibilityMode _visibilityMode;

    public CompanionOverlayHost(CompanionVisibilityMode visibilityMode)
    {
        _visibilityMode = visibilityMode;
        _withdrawTimer = new DispatcherTimer(WithdrawDelay, DispatcherPriority.Normal, (_, _) => { _withdrawTimer!.Stop(); ApplyVisibility(); }, _dispatcher);
        _withdrawTimer.Stop();
        RebuildWindows();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
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
        if (state == CompanionState.Listening)
        {
            MoveToMonitorContainingCursor();
        }

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

        target.PointBuddyAt(location.MonitorDipX, location.MonitorDipY);
    })).Task;

    public void ReturnToRest() => _dispatcher.InvokeAsync(() => Guarded(() => ActiveWindow?.MoveBuddyToRest(animate: true)));

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private CompanionOverlayWindow? ActiveWindow => _activeMonitorId is null ? null : _windows.GetValueOrDefault(_activeMonitorId);

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

    private void MoveToMonitorContainingCursor()
    {
        var cursorMonitor = MonitorEnumerator.Enumerate().FirstOrDefault(monitor => monitor.ContainsCursor);
        if (cursorMonitor is not null && _windows.ContainsKey(cursorMonitor.MonitorId) && cursorMonitor.MonitorId != _activeMonitorId)
        {
            ActivateMonitor(cursorMonitor.MonitorId);
        }
    }

    private void ActivateMonitor(string? monitorId)
    {
        if (monitorId is null || !_windows.TryGetValue(monitorId, out var window))
        {
            return;
        }

        if (ActiveWindow is { } previous)
        {
            previous.BuddyVisible = false;
        }

        _activeMonitorId = monitorId;
        window.MoveBuddyToRest(animate: false);
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
