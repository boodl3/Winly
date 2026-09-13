using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Winly.Platform.Capture;

/// <summary>Downscales and encodes JPEG at quality 80 (research.md §2), all in memory.</summary>
public static class CapturedFrameEncoder
{
    public const int MaxLongestEdgePx = 1568;

    /// <summary>
    /// The vision model bills by pixel area and stops gaining detail past roughly this much, so a
    /// 1920x1200 desktop scaled to the 1568 px edge alone would pay for ~25% more image than it can
    /// use. Both caps apply; whichever bites harder wins.
    /// </summary>
    public const double MaxPixels = 1_150_000;

    public const float JpegQuality = 0.8f;

    public static async Task<(byte[] Bytes, int Width, int Height)> EncodeJpeg(byte[] bgraPixels, int width, int height, CancellationToken cancellationToken)
    {
        var scale = Math.Min(
            Math.Min(1.0, (double)MaxLongestEdgePx / Math.Max(width, height)),
            Math.Sqrt(MaxPixels / ((double)width * height)));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(JpegQuality, PropertyType.Single),
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options).AsTask(cancellationToken);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, bgraPixels);
        encoder.BitmapTransform.ScaledWidth = (uint)targetWidth;
        encoder.BitmapTransform.ScaledHeight = (uint)targetHeight;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync().AsTask(cancellationToken);

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken);
        reader.ReadBytes(bytes);
        return (bytes, targetWidth, targetHeight);
    }
}
