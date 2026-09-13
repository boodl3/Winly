namespace Winly.Core.Abstractions;

/// <summary>Observes a system-wide key combination without consuming it (FR-002, FR-003).</summary>
public interface IActivationKeyMonitor
{
    /// <summary>Raised once when every key in the combination is held. May fire on any thread.</summary>
    event Action? KeyDown;

    /// <summary>Raised once when any key in the combination is released. May fire on any thread.</summary>
    event Action? KeyUp;

    /// <exception cref="ActivationKeyUnavailableException">The combination cannot be observed.</exception>
    Task Start(KeyCombination combination, CancellationToken cancellationToken);

    /// <summary>Must not throw.</summary>
    Task Stop();
}
