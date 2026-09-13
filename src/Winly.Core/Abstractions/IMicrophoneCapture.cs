namespace Winly.Core.Abstractions;

public interface IMicrophoneCapture
{
    /// <summary>Returns a stream of 16 kHz mono PCM16 that ends when capture stops.</summary>
    /// <exception cref="MicrophoneUnavailableException">No device, or the device is held exclusively elsewhere.</exception>
    Task<Stream> StartCapture(CancellationToken cancellationToken);

    /// <summary>Idempotent; must not throw. Releases the device so nothing stays open while idle (FR-032).</summary>
    Task StopCapture();
}
