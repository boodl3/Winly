# Phase 1 Data Model: Core Companion Loop

All entities here are in-memory only for this feature. None are written to disk
except `UserSettings` (FR-030), consistent with FR-014 and FR-029.

## CompanionState (enumeration)

| State | Entered when | Exited when |
|---|---|---|
| `Idle` | App start; after `Speaking` completes; after a request is abandoned | Activation key pressed → `Listening` |
| `Listening` | Activation key held | Activation key released → `Working`; 30s max-hold elapses → `Working` (FR-004) |
| `Working` | Key released with a non-empty transcript; request in flight | Answer begins playback → `Speaking`; provider failure → `Idle` (FR-031); new activation → `Listening` (FR-015, abandons this request) |
| `Speaking` | Spoken answer playback begins | Playback completes → `Idle`; new activation → `Listening` (FR-015, abandons playback) |

Legal transitions only: `Idle → Listening → Working → Speaking → Idle`, with a
new activation able to interrupt from `Working` or `Speaking` back to
`Listening` at any time (FR-015). No other transitions are valid.

If the transcript is empty after key release (FR-006), the transition is
`Listening → Idle` directly — `Working` is never entered.

## Session

The period between application start and exit.

| Field | Type | Notes |
|---|---|---|
| `Exchanges` | ordered list of `Exchange` | Empty at start; appended to as the user interacts |
| `StartedAtUtc` | timestamp | For diagnostic logging only |

Discarded in full on exit (FR-014) — never serialized.

## Exchange

One user utterance and the answer produced for it.

| Field | Type | Notes |
|---|---|---|
| `Transcript` | string | Non-empty (FR-006 guarantees this before an `Exchange` is created) |
| `AnswerText` | string | Spoken text, with any pointing designation already stripped (FR-017) |
| `PointingTarget` | `PointingTarget?` | Null when the answer designates no position (FR-016) |
| `CreatedAtUtc` | timestamp | Used for ordering within `Session.Exchanges` |

## DisplayCapture

One display's captured image at a moment in time.

| Field | Type | Notes |
|---|---|---|
| `MonitorId` | string | Stable handle-derived identifier for the source monitor, used to resolve `PointingTarget.MonitorId` back to a physical display |
| `ImageBytes` | byte[] | JPEG-encoded, downscaled per `research.md` §2; held only for the duration of the request (FR-029) |
| `WidthPx` / `HeightPx` | int | Captured image dimensions, needed to resolve any position referenced against this capture |
| `IsPrimary` | bool | True for exactly one `DisplayCapture` per request — the display containing the cursor at key release (FR-008) |
| `Monitor` | `MonitorGeometry` | The source monitor's virtual-screen origin, physical size, and DPI scale — what `CaptureToDesktopCoordinateMapper` needs to resolve a position in this (downscaled) image back to the desktop (research.md §5). Added at implementation time. |

## PointingTarget

A position and a short label designating an on-screen element, resolved to a
specific display.

| Field | Type | Notes |
|---|---|---|
| `MonitorId` | string | Matches a `DisplayCapture.MonitorId` from the same request |
| `XInCapturePx` / `YInCapturePx` | int | Position in that display's capture-pixel space, before conversion (research.md §5) |
| `Label` | string | Short human-readable name of what is at that position, e.g. "Font Size dropdown" |

Constraint: `XInCapturePx`/`YInCapturePx` MUST be clamped to
`[0, WidthPx) x [0, HeightPx)` of the matching `DisplayCapture` before
conversion to desktop coordinates (FR-020) — out-of-bounds values are
constrained, not discarded.

## UserSettings

Persisted at `%LOCALAPPDATA%\Winly\settings.json` (FR-030).

| Field | Type | Default | Notes |
|---|---|---|---|
| `ActivationKeyCombination` | string (key-chord descriptor) | `"LWin+LAlt"` | Clarifications 2026-09-12: Windows key + Left Alt |
| `CompanionVisibilityMode` | enum `{ AlwaysVisible, InteractionOnly }` | `InteractionOnly` | FR-025 |
| `ScreenCaptureEnabled` | bool | `true` | FR-028; disclosed before first capture (FR-026) |
| `MicrophoneCaptureEnabled` | bool | `true` | FR-028 |
| `RunAtLogin` | bool | `false` | research.md §9 |

State transitions: none — this is a flat settings record, rewritten in full on
each change and reloaded at startup.
