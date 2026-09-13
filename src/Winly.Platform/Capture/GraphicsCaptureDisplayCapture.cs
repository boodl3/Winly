using Serilog;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Winly.Core.Abstractions;
using Winly.Core.Pointing;

namespace Winly.Platform.Capture;

/// <summary>
/// Snapshots every monitor through Windows.Graphics.Capture with no picker and no per-capture
/// prompt (research.md §1). Nothing is held open between calls and nothing touches disk (FR-029, FR-032).
/// </summary>
public sealed class GraphicsCaptureDisplayCapture : IDisplayCapture
{
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<DisplayCapture>> CaptureAll(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || !GraphicsCaptureSession.IsSupported())
        {
            throw new DisplayCaptureUnavailableException("Screen capture needs Windows 10 build 19041 or later.");
        }

        var monitors = MonitorEnumerator.Enumerate();
        using var device = CaptureDevice.Create();
        var captures = new List<DisplayCapture>(monitors.Count);
        foreach (var monitor in monitors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (pixels, width, height) = await CaptureFrame(device, monitor.Handle, cancellationToken);
                var (jpeg, jpegWidth, jpegHeight) = await CapturedFrameEncoder.EncodeJpeg(pixels, width, height, cancellationToken);
                captures.Add(new DisplayCapture(monitor.MonitorId, jpeg, jpegWidth, jpegHeight, monitor.ContainsCursor, monitor.Geometry));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Per contract: a display that cannot be captured is omitted rather than aborting the call.
                Log.Warning(exception, "Display {MonitorId} could not be captured and was omitted", monitor.MonitorId);
            }
        }

        if (captures.Count == 0)
        {
            throw new DisplayCaptureUnavailableException("No display could be captured.");
        }

        if (!captures.Any(capture => capture.IsPrimary))
        {
            captures[0] = captures[0] with { IsPrimary = true };
        }

        return captures;
    }

    private static async Task<(byte[] Bgra, int Width, int Height)> CaptureFrame(CaptureDevice device, nint monitorHandle, CancellationToken cancellationToken)
    {
        var item = GraphicsCaptureItemFactory.CreateForMonitor(monitorHandle);
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        var firstFrame = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        framePool.FrameArrived += (pool, _) =>
        {
            var frame = pool.TryGetNextFrame();
            if (frame is not null && !firstFrame.TrySetResult(frame))
            {
                frame.Dispose();
            }
        };

        using var session = framePool.CreateCaptureSession(item);
        DisableCaptureBorderWhereSupported(session);
        session.StartCapture();
        using var capturedFrame = await firstFrame.Task.WaitAsync(FirstFrameTimeout, cancellationToken);
        return device.ReadPixels(capturedFrame.Surface);
    }

    private static void DisableCaptureBorderWhereSupported(GraphicsCaptureSession session)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            return; // The yellow capture border is a documented limitation on older builds (research.md §1).
        }

        try
        {
            session.IsBorderRequired = false;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Capture border could not be disabled");
        }
    }
}
