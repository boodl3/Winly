using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Core.Settings;

namespace Winly.Core.Abstractions;

/// <summary>
/// The visible companion. Implementations must never take keyboard focus or intercept mouse
/// input (FR-024) and must marshal to their own UI thread — every member may be called from
/// any thread and none may throw.
/// </summary>
public interface ICompanionOverlay
{
    /// <summary>Must apply within 150 ms (SC-002).</summary>
    void SetState(CompanionState state);

    /// <summary>Travels to the location; a location on a display that no longer exists is a no-op.</summary>
    Task PointTo(DesktopLocation location, CancellationToken cancellationToken);

    void ReturnToRest();

    CompanionVisibilityMode VisibilityMode { get; set; }
}
