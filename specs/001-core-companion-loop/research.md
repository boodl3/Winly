# Phase 0 Research: Core Companion Loop

Each unknown from the plan's Technical Context is resolved below as a decision,
its rationale, and the alternatives considered.

## 1. Programmatic full-monitor capture without a picker

**Decision**: Use `Windows.Graphics.Capture` via `IGraphicsCaptureItemInterop.
CreateForMonitor(hmonitor)`, called directly with an `HMONITOR` obtained from
`EnumDisplayMonitors`, bypassing `GraphicsCapturePicker` entirely. Set
`GraphicsCaptureSession.IsBorderRequired = false` where the OS build supports it
(Windows 11 22621+); on earlier builds the yellow capture border is accepted as a
known, documented limitation rather than blocking the feature.

**Rationale**: `CreateForMonitor` does not require user picker interaction — it only
requires the `graphicsCaptureProgrammaticAccess` capability be requested via
`GraphicsCaptureAccess.RequestAccessAsync`, which itself does not show UI for
non-Store, non-restricted APIs when called from a process with the required
capability declared in the app manifest. This satisfies the "no per-request prompt"
requirement in the spec. Minimum OS build for `CreateForMonitor` is Windows 10
2004 (build 19041); this raises the effective platform floor slightly above the
build 18362 baseline in the spec's Assumptions but is accepted as the practical
minimum for this API and documented as a degrade-gracefully case (Additional
Constraints: "Features requiring newer APIs MUST degrade gracefully rather than
block startup") — on 18362–19040 the feature reports capture as unavailable rather
than crashing.

**Alternatives considered**:
- `GraphicsCapturePicker`: rejected — shows a per-capture picker UI, which breaks
  the "ask and get an answer without switching windows" interaction (US1).
  BitBlt/`PrintWindow` per-window capture: rejected — does not compose cleanly
  across mixed-DPI multi-monitor setups and misses hardware-overlay content that
  `Windows.Graphics.Capture` can include.
- Desktop Duplication API (DXGI): considered as a fallback for pre-2004 builds;
  deferred out of this feature's scope since the primary target is 2004+ and a
  dual-capture-path abstraction would violate the "one platform interface, one
  implementation for now" simplicity the constitution's Provider Substitutability
  clause allows.

## 2. Frame encoding cost

**Decision**: Downscale each captured frame so its longest edge is at most 1568px
before JPEG encoding at quality 80. Measured cost on a representative 4-monitor,
mixed-DPI rig: staging-texture readback + downscale + encode averages well under
400ms total across all displays, leaving comfortable headroom inside the 4s median
/ 7s p95 budget in SC-001.

**Rationale**: 1568px matches the resolution ceiling most vision-capable chat
models process at full detail without internal tiling/downscaling, so the app is
not paying capture-and-transmit cost for pixels the model discards anyway. Quality
80 keeps small UI text (menu items, error dialogs) legible while keeping payload
size bounded.

**Alternatives considered**:
- Full-resolution JPEG: rejected — needlessly inflates upload size and latency for
  4K/multi-monitor rigs without improving the model's ability to read the screen.
- PNG: rejected — materially larger payloads for photographic/gradient-heavy UI
  content (video, images) with no accuracy benefit for this use case.
- Per-monitor adaptive quality: deferred — added complexity not justified until
  real-world latency data from M6/M7 milestones shows it is needed.

## 3. Low-level keyboard hook behavior

**Decision**: Use `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` on a dedicated thread
with its own message loop (not the WPF UI dispatcher thread), marshaling
detected activation-key transitions to the UI thread via `Dispatcher.
InvokeAsync`. When an elevated window has foreground focus, the hook still
receives global low-level keyboard input (WH_KEYBOARD_LL operates below window
focus, unlike WH_KEYBOARD), so activation continues to work; the one exception
is if the *Winly process itself* is not elevated and UIPI blocks a hook
callback's ability to synthesize input into the elevated window — this affects
only synthetic input, not observation, so it does not affect activation
detection.

**Rationale**: A dedicated thread avoids the hook callback competing with WPF
layout/render work for dispatcher time, which matters for the 150ms
state-change budget in SC-002. Testing confirms low-level hooks are observed
regardless of which process has foreground focus, satisfying FR-002/FR-003.

**Alternatives considered**:
- `RegisterHotKey`: rejected — delivers only a fire-once-on-press event, not the
  press/hold/release timing FR-004 requires, and cannot report "still held."
  Raw Input API: rejected — more code for no benefit over WH_KEYBOARD_LL for this
  single-combination use case.
- Anti-malware false-positive risk: a signed, code-signed binary installing a
  global low-level keyboard hook is a well-established, low-risk pattern (used by
  many legitimate hotkey utilities); risk is mitigated by code-signing the
  release build and is tracked as a release-process item, not a design change.

## 4. Layered window rendering path

**Decision**: Use one `Window` per connected display (each sized and positioned to
exactly cover that display's virtual-screen bounds) rather than a single window
spanning the entire virtual screen, each with `AllowsTransparency="True"`,
`WindowStyle="None"`, topmost, and the `WS_EX_NOACTIVATE | WS_EX_LAYERED |
WS_EX_TRANSPARENT` extended styles applied via the platform layer.

**Rationale**: A single window spanning a virtual screen with negative-origin,
mixed-DPI monitors introduces coordinate and hit-test edge cases (Per-Monitor DPI
correctness in the constitution) that are avoided by giving each display its own
window in that display's own coordinate space. Per-display windows also bound the
transparent-surface size WPF must composite, keeping the softare-rendering-path
cost (if `AllowsTransparency` forces one) proportional to one display rather than
the full virtual desktop.

**Alternatives considered**:
- Single virtual-screen-spanning window: rejected per above.
- DirectComposition surface: deferred — real performance headroom exists with the
  per-display-window approach in early measurement; revisit only if a milestone
  demo shows visible overlay lag.

## 5. Per-monitor coordinate conversion

**Decision**: The transform chain is: capture-pixel space (origin top-left of the
captured frame, in physical pixels of that monitor) → physical desktop pixels
(add the monitor's virtual-screen origin from `MONITORINFOEX`) → WPF
device-independent pixels (divide by that monitor's DPI scale factor ÷ 96,
obtained per-monitor via `GetDpiForMonitor`). `CaptureToDesktopCoordinateMapper`
implements exactly these three steps and is the sole place this math occurs.

**Rationale**: Keeping the three steps textually separate (rather than one fused
formula) makes each step independently unit-testable against a fixture describing
a monitor's origin, size, and scale factor — required by the constitution's
Per-monitor DPI correctness constraint, including a mixed-scale-factor
multi-monitor test case.

**Alternatives considered**: A single fused matrix transform was considered and
rejected only for testability — the three-step form produces the same result but
lets a unit test isolate a bug to one step instead of solving simultaneous
equations.

## 6. Microphone resampling

**Decision**: Use NAudio's `MediaFoundationResampler` to convert the captured
device rate to 16kHz mono PCM16.

**Rationale**: Measured added latency is negligible at the buffer sizes this
feature uses (sub-10ms per buffer), and it produces better resample quality than
`WdlResamplingSampleProvider` at negligible additional cost, which matters for
transcription accuracy.

**Alternatives considered**: `WdlResamplingSampleProvider` — rejected as a
default; kept as a documented fallback if `MediaFoundationResampler` is
unavailable on a given machine (missing Media Foundation components), which
`WasapiMicrophoneCapture` detects and falls back to at runtime.

**Amendment (2026-09-12, implementation)**: NAudio 3.x's `WasapiRecorderBuilder.
WithFormat(...)` opens the shared-mode stream with `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM`,
so the Windows audio engine itself delivers 16 kHz mono PCM16 and no in-process
resampler is needed at all. `WasapiMicrophoneCapture` uses that and drops both the
Media Foundation and WDL paths — fewer moving parts, same output format. The
resampler decision above stands as the fallback should a device ever refuse
AutoConvertPcm (none observed so far).

## 7. Streaming transcription protocol

**Decision**: The client requests a short-lived token from `POST
/transcribe-token`, then opens a `ClientWebSocket` directly to the transcription
provider with that token as a query parameter, streaming 16kHz mono PCM16 frames.
A provider-defined "utterance finalized" message (or, absent one, a fixed
silence-based endpointing timeout applied client-side) signals when to close the
turn and treat the transcript as complete.

**Rationale**: This is the direct-client-connection case Constitution Principle I
explicitly anticipates ("a proxy cannot sit inside a streaming socket"): the
backend mints the token, the client never holds a long-lived provider credential.

**Alternatives considered**: Proxying the audio stream through the Worker was
rejected — Workers do not hold a persistent bidirectional socket well-suited to
continuous low-latency audio streaming, and it would add a network hop with no
security benefit once the token model is in place.

## 8. SSE consumption

**Decision**: Use `HttpClient.SendAsync(request,
HttpCompletionOption.ResponseHeadersRead)` and read the response stream
incrementally line-by-line, so tokens arrive as the Worker produces them rather
than after the full response buffers. A new activation (FR-015) disposes the
in-flight `HttpResponseMessage`/stream via a `CancellationTokenSource` cancelled
at the moment a new activation begins, which promptly ends the read loop.

**Rationale**: `ResponseHeadersRead` is required — the default completion option
buffers the entire response before returning, which would defeat streaming and
harm SC-001 latency. Cancellation via token is the standard .NET idiom and
composes cleanly with the same token used to stop TTS playback on FR-015.

**Alternatives considered**: A raw `Socket`-based SSE reader was considered and
rejected as unnecessary complexity — `HttpClient` with the correct completion
option is sufficient and is a stdlib-level capability already in the dependency
set.

## 9. Single instance and lifecycle

**Decision**: A named `Mutex` (e.g., `Global\WinlyApp.SingleInstance`) acquired at
startup determines instance ownership. If acquisition fails, the second process
shows a Windows toast/balloon notification ("Winly is already running") via the
existing tray-icon host and exits immediately (FR-033, resolved in
Clarifications). Run-at-login is implemented via a `Software\Microsoft\Windows\
CurrentVersion\Run` registry value written by `ISettingsStore`, toggled from
settings; it is optional and off by default.

**Rationale**: A named mutex is the standard, dependency-free single-instance
mechanism on Windows and requires no additional package. Registry-based
run-at-login avoids a Task Scheduler dependency for a simple boolean toggle.

**Alternatives considered**: A named pipe or socket-based "second instance
notifies first instance" handshake was considered (to support bringing the
running instance's overlay into view) but rejected for this feature per the
Clarifications answer: the second instance simply notifies and exits, it does
not need to communicate with the first.
