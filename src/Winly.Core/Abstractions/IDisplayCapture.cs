using Winly.Core.Pointing;

namespace Winly.Core.Abstractions;

public interface IDisplayCapture
{
    /// <summary>
    /// Snapshots every connected display and releases all capture resources before returning.
    /// A display that cannot be captured is omitted rather than aborting the call; exactly one
    /// returned capture has <see cref="DisplayCapture.IsPrimary"/> set (FR-007, FR-008).
    /// </summary>
    /// <exception cref="DisplayCaptureUnavailableException">No display at all could be captured.</exception>
    Task<IReadOnlyList<DisplayCapture>> CaptureAll(CancellationToken cancellationToken);
}
