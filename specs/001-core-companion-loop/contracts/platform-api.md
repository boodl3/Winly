# Contract: Platform-Layer Interfaces

These are the interfaces defined in `Winly.Core.Abstractions` and implemented in
`Winly.Platform` (Constitution Principle V). `Winly.Core` depends only on these
interfaces; it never references the concrete implementations or any
Windows-specific type.

## `IActivationKeyMonitor`

| Member | Threading / Lifetime | Permitted failure |
|---|---|---|
| `event Action KeyDown` | Raised on a dedicated hook thread; core marshals to its own execution context before acting | None — hook registration failure is surfaced via `Start()` |
| `event Action KeyUp` | Same as `KeyDown` | None |
| `Task Start(KeyCombination combo, CancellationToken ct)` | Called once at app startup | Throws `ActivationKeyUnavailableException` if the combination cannot be registered (e.g., already claimed) |
| `Task Stop()` | Called once at app shutdown | Must not throw |

Lifetime: one instance for the life of the process; `Start`/`Stop` are not
expected to be called more than once each, except when the user changes the
key combination in settings, which calls `Stop` then `Start` with the new combo.

## `IMicrophoneCapture`

| Member | Threading / Lifetime | Permitted failure |
|---|---|---|
| `Task<Stream> StartCapture(CancellationToken ct)` | Called on `Listening` entry; returns a stream of 16kHz mono PCM16 frames | Throws `MicrophoneUnavailableException` if no device is present or the device is held exclusively by another application (edge case in spec) |
| `Task StopCapture()` | Called on key release or the 30s bound (FR-004) | Must not throw |

Lifetime: capture is started and stopped once per activation; the interface
does not keep a device open between activations (Additional Constraints:
resource behavior at idle).

## `IDisplayCapture`

| Member | Threading / Lifetime | Permitted failure |
|---|---|---|
| `Task<IReadOnlyList<DisplayCapture>> CaptureAll(CancellationToken ct)` | Called once per activation, at key release | Throws `DisplayCaptureUnavailableException` for a display that cannot be captured (elevated foreground window, DRM-protected content); that display is omitted or returned with a blank/placeholder image rather than aborting the whole call |

Lifetime: no persistent capture session is held open; each call captures a
fresh snapshot and releases all capture resources before returning.

## `IAudioPlayback`

| Member | Threading / Lifetime | Permitted failure |
|---|---|---|
| `Task Play(Stream audio, CancellationToken ct)` | Called once per answer, on entering `Speaking` | Throws `PlaybackDeviceUnavailableException` if no output device is available |
| `Task Stop()` | Called when a new activation abandons in-flight speech (FR-015) | Must not throw |

## `ICompanionOverlay`

| Member | Threading / Lifetime | Permitted failure |
|---|---|---|
| `void SetState(CompanionState state)` | Called on every state transition; must apply within 150ms (SC-002) | Must not throw |
| `Task PointTo(DesktopLocation location, CancellationToken ct)` | Called when an answer designates a position. The core has already clamped the `PointingTarget` and run it through `CaptureToDesktopCoordinateMapper`, so the overlay receives a resolved `DesktopLocation` (monitor id, physical desktop pixels, and monitor-relative DIPs) — keeping the DPI math in `Winly.Core` where it is unit-tested | Must not throw; an unreachable target (e.g., its display disconnected mid-flight) is a no-op, not an exception |
| `void ReturnToRest()` | Called when no position is designated, or after pointing completes | Must not throw |
| `CompanionVisibilityMode VisibilityMode { get; set; }` | Reflects `UserSettings.CompanionVisibilityMode` | N/A |

Constraint on every implementation: the overlay window(s) MUST NOT accept
keyboard focus and MUST NOT intercept mouse input intended for the window
beneath (FR-024) — this is a requirement on the `Winly.Platform` implementation,
not something `Winly.Core` can verify at compile time, and is covered by the
manual verification matrix (Constitution: platform-layer changes MUST document
manual verification).
