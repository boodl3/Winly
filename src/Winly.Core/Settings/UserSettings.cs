namespace Winly.Core.Settings;

/// <summary>Flat settings record, rewritten in full on each change and persisted across restarts (FR-030).</summary>
public sealed record UserSettings
{
    public string ActivationKeyCombination { get; init; } = "LWin+LAlt";

    public CompanionVisibilityMode CompanionVisibilityMode { get; init; } = CompanionVisibilityMode.InteractionOnly;

    public bool ScreenCaptureEnabled { get; init; } = true;

    public bool MicrophoneCaptureEnabled { get; init; } = true;

    /// <summary>Lets Winly open apps and change volume when the answer calls for it.</summary>
    public bool DesktopActionsEnabled { get; init; } = true;

    /// <summary>
    /// Opaque per-install id. Not a secret and not identifying: it is the key the backend files
    /// this install's linked music account under, so the account's tokens never come here.
    /// </summary>
    public string InstallId { get; init; } = string.Empty;

    public bool RunAtLogin { get; init; }
}
