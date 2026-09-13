# Implementation Plan: Core Companion Loop

**Branch**: `001-core-companion-loop` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification at `specs/001-core-companion-loop/spec.md`

## Summary

Build the complete voice-in / screen-context / voice-out / point-at-element loop as a
WPF desktop application with no main window, backed by a credential-holding proxy the
project controls.

Technical approach: a WPF tray-resident application whose orchestration core is free of
OS-specific types, with all Win32 and WinRT interop confined to a platform layer behind
interfaces. A low-level keyboard hook drives push-to-talk; WASAPI captures microphone
audio and streams it to a transcription service over a websocket authorized by a
short-lived token; Windows.Graphics.Capture grabs every display at key release; the
transcript and captured displays are posted to the proxy, which forwards to the chat
provider and streams the response back; the response carries an in-band position
designation which the application strips, converts from capture-pixel space to desktop
coordinates, and uses to animate the companion; the remaining text is sent to the
speech provider and played back.

The companion itself is, for this feature, a square body with two black eyes drawn in
XAML — no external artwork, no third-party asset licensing.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (LTS)
**Primary Dependencies**:
- WPF (`net10.0-windows10.0.22621.0` target framework moniker so the projection exposes
  `GraphicsCaptureSession.IsBorderRequired`; `SupportedOSPlatformVersion` stays 10.0.19041
  and newer APIs are runtime-guarded)
- `H.NotifyIcon.Wpf` — system tray entry (MIT)
- `NAudio` — WASAPI microphone capture, resampling, MP3 playback (MIT)
- WinRT projections for `Windows.Graphics.Capture` and `Windows.Graphics.Imaging` come
  from the Windows-targeted TFM itself (`Microsoft.Windows.SDK.NET.Ref`); no separate
  `Microsoft.Windows.CsWinRT` package is needed
- `Vortice.Direct3D11` — GPU texture readback for captured frames (MIT)
- `Serilog` + `Serilog.Sinks.File` — structured logging (Apache-2.0)
- `System.Text.Json` — settings, request/response payloads (built-in)
- Source-generated P/Invoke (`[LibraryImport]`) for `user32.dll` — no third-party
  interop package

All dependency licenses are permissive (MIT / Apache-2.0), satisfying Constitution
Principle III. No copyleft dependency is introduced.

**Backend**: Cloudflare Worker, TypeScript, deployed with Wrangler. Three routes:
`POST /chat` (chat + vision, streams Server-Sent Events back), `POST /tts` (returns
audio), `POST /transcribe-token` (mints a short-lived streaming token). Provider
credentials are Worker secrets.

**Storage**:
- User settings — JSON at `%LOCALAPPDATA%\Winly\settings.json`
- Logs — rolling files at `%LOCALAPPDATA%\Winly\logs\`
- Conversation history — in memory only, discarded on exit (FR-014)
- Captured frames and audio — in memory only, never written to disk (FR-029)

**Testing**: xUnit for unit tests; NSubstitute for platform-interface fakes;
`Verify` for snapshot-testing response parsing. Core, provider-client, and coordinate
math are covered in CI. Platform-layer behavior is covered by a documented manual
verification matrix (Windows version × monitor configuration × scaling factor).

**Target Platform**: Windows 10 1903 (build 18362) or later, x64, for the
application generally. Screen capture (`IGraphicsCaptureItemInterop.
CreateForMonitor`) requires build 19041 (2004) or later; on 18362–19040 the
application starts normally but reports screen capture as unavailable rather than
blocking startup, per the constitution's degrade-gracefully allowance. Frame-border
suppression, if adopted, requires Windows 11 22621+ and degrades gracefully where
unavailable.

**Project Type**: Desktop client + minimal serverless backend.

**Performance Goals**: Key release to first audible word ≤ 4 s median, ≤ 7 s p95
(SC-001). Visual state change within 150 ms of key press (SC-002). Idle CPU < 1%
averaged over five minutes with no open capture handles (SC-005).

**Constraints**:
- No third-party credential in the shipped binary (Principle I, FR-034)
- No copyleft dependencies or assets (Principle III)
- Per-monitor DPI v2 awareness declared in the application manifest
- Overlay must not take focus and must not intercept clicks (FR-024)
- Capture bounded in duration; nothing persisted (FR-004, FR-029)

**Scale/Scope**: Single local user, one session at a time. Roughly 4 client projects,
1 worker, ~34 functional requirements, 4 user stories.

## Constitution Check

*Gate evaluated before Phase 0 and re-evaluated after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| I. Secrets never ship in client | No credential literal, no long-lived key in any client project; all provider calls go through the Worker; streaming auth uses a short-lived minted token | PASS — architecture routes 100% of provider traffic through the Worker |
| II. Consent before capture | Capture only on key-hold; visible active-capture indicator; kill switch persisted; nothing written to disk or retained | PASS — FR-026 through FR-029 are in scope for this feature, not deferred |
| III. Clean-room and license hygiene | No code ported from any existing companion app; every dependency permissive; companion art drawn in XAML by the project | PASS — dependency list above is MIT/Apache-2.0 only |
| IV. Vertical slices | Milestones M0–M7 below are each independently runnable and demonstrable | PASS |
| V. Interop quarantined | All P/Invoke, WinRT, D3D confined to `Winly.Platform`; `Winly.Core` references only interfaces and has no Windows-specific types | PASS — enforced by project reference direction; `Winly.Core` targets `net10.0` (no `-windows`) so OS types cannot compile into it |
| VI. Clarity over cleverness | Naming and comment standards applied in review | PASS — review criterion |
| VII. Fail loud to log, soft to user | Every provider call, capture call, and device acquisition has explicit failure handling and a user-facing message | PASS — FR-031 in scope |

**Post-Phase-1 re-evaluation**: PASS — no new violations introduced by `data-model.md`
or the `contracts/` design; the gate table above already reflects this verdict for
all seven principles.

## Project Structure

### Documentation

```
specs/001-core-companion-loop/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output — resolved unknowns
├── data-model.md        # Phase 1 output — entities and state machine
├── quickstart.md        # Phase 1 output — clone-to-running instructions
├── contracts/
│   ├── worker-api.md    # Proxy route contracts
│   └── platform-api.md  # Platform-layer interface contracts
└── tasks.md             # Generated by /speckit.tasks — do not hand-author
```

### Source Code

```
src/
├── Winly.Core/                      # net10.0 — NO Windows types, fully testable
│   ├── Companion/
│   │   ├── CompanionStateMachine.cs        # idle → listening → working → speaking
│   │   ├── ConversationSession.cs          # in-memory exchange history
│   │   └── CompanionState.cs
│   ├── Pointing/
│   │   ├── PointingDesignationParser.cs    # strips + parses the in-band position tag
│   │   ├── PointingTarget.cs
│   │   └── CaptureToDesktopCoordinateMapper.cs   # the DPI-critical math
│   ├── Abstractions/                # interfaces the platform layer implements
│   │   ├── IActivationKeyMonitor.cs
│   │   ├── IMicrophoneCapture.cs
│   │   ├── IDisplayCapture.cs
│   │   ├── IAudioPlayback.cs
│   │   └── ICompanionOverlay.cs
│   ├── Providers/                   # interfaces the provider layer implements
│   │   ├── IChatProvider.cs
│   │   ├── ISpeechToTextProvider.cs
│   │   └── ITextToSpeechProvider.cs
│   └── Settings/
│       ├── UserSettings.cs
│       └── ISettingsStore.cs
│
├── Winly.Platform/                  # net10.0-windows10.0.22621.0 — all interop here
│   ├── Input/
│   │   ├── LowLevelKeyboardHookActivationMonitor.cs   # WH_KEYBOARD_LL
│   │   └── NativeInputMethods.cs                      # [LibraryImport] user32
│   ├── Capture/
│   │   ├── GraphicsCaptureDisplayCapture.cs           # Windows.Graphics.Capture
│   │   ├── MonitorEnumerator.cs                       # EnumDisplayMonitors, DPI per monitor
│   │   └── CapturedFrameEncoder.cs                    # D3D texture → CPU → JPEG
│   ├── Audio/
│   │   ├── WasapiMicrophoneCapture.cs                 # NAudio capture → PCM16 16 kHz mono
│   │   └── NAudioPlayback.cs
│   ├── Overlay/
│   │   ├── OverlayWindowStyleApplier.cs               # WS_EX_NOACTIVATE/LAYERED/TRANSPARENT
│   │   └── VirtualScreenGeometry.cs
│   └── Settings/
│       └── JsonFileSettingsStore.cs
│
├── Winly.Providers/                 # net10.0 — HTTP/WS clients, no Windows types
│   ├── Chat/
│   │   ├── ProxyChatProvider.cs                       # POST /chat, SSE stream reader
│   │   └── ServerSentEventReader.cs
│   ├── SpeechToText/
│   │   ├── StreamingSpeechToTextProvider.cs           # ClientWebSocket
│   │   └── TranscriptionTokenClient.cs                # POST /transcribe-token
│   ├── TextToSpeech/
│   │   └── ProxyTextToSpeechProvider.cs               # POST /tts
│   └── ProxyEndpointOptions.cs                        # base URL only — never a key
│
└── Winly.App/                       # net10.0-windows10.0.22621.0 — WPF composition root
    ├── App.xaml(.cs)                                  # DI wiring, single-instance guard
    ├── app.manifest                                   # PerMonitorV2 DPI awareness
    ├── Tray/
    │   └── TrayIconHost.cs
    ├── Panel/
    │   ├── CompanionPanelWindow.xaml(.cs)             # borderless settings/status panel
    │   └── FirstRunDisclosureWindow.xaml(.cs)         # FR-026
    └── Overlay/
        ├── CompanionOverlayWindow.xaml(.cs)           # transparent, topmost, all displays
        ├── SquareBuddyControl.xaml(.cs)               # demo companion: square + 2 eyes
        └── PointingAnimator.cs                        # travel animation

tests/
├── Winly.Core.Tests/                # state machine, tag parsing, coordinate math
├── Winly.Providers.Tests/           # SSE parsing, websocket framing, error mapping
└── manual-verification.md           # platform-layer matrix (documented per Principle V)

worker/
├── src/index.ts                     # /chat, /tts, /transcribe-token
├── wrangler.toml                    # non-secret vars only
└── package.json
```

**Structure decision**: Four client projects rather than one, specifically to make
Principle V mechanically enforceable rather than merely aspirational. `Winly.Core`
targets plain `net10.0` — not `net10.0-windows` — so a Windows-specific type physically
cannot compile into it. Reference direction is `App → Platform → Core` and
`App → Providers → Core`; `Core` references nothing. This means the state machine,
designation parsing, and coordinate math run in CI on any agent, and the untestable
surface is confined to `Winly.Platform`.

## Demo-Phase Companion Rendering

`SquareBuddyControl` is a `UserControl` containing a rounded `Border` for the body and
two `Ellipse` eyes filled black, sized relative to the body. State is expressed through
`VisualStateManager` states matching `CompanionState` (Idle, Listening, Working,
Speaking) with `Storyboard` animations: a periodic blink (eye scale on Y), an idle bob
(translate transform), an eye-squint while working, and a subtle scale pulse while
speaking. Travel to a pointing target is a `PointAnimation` along a `BezierSegment` so
motion arcs rather than moving in a straight line.

No image files, no vector assets, no font glyphs from third parties. This satisfies
FR-021 and keeps the demo phase entirely free of asset licensing questions.

## Phase 0 — Research

Unknowns resolved before design was finalized. Full detail → [research.md](./research.md).

1. Programmatic full-monitor capture without a picker.
2. Frame encoding cost (downscale factor and JPEG quality).
3. Low-level keyboard hook behavior under elevation and anti-malware scrutiny.
4. Layered window rendering path for a full-virtual-screen transparent overlay.
5. Per-monitor coordinate conversion transform chain.
6. Microphone resampling method.
7. Streaming transcription protocol shape.
8. SSE consumption and cancellation semantics.
9. Single instance and lifecycle mechanism.

## Phase 1 — Design Outputs

- **[data-model.md](./data-model.md)**: `CompanionState` enumeration and legal
  transitions; `Session`, `Exchange`, `DisplayCapture`, `PointingTarget`,
  `UserSettings`; explicit note that captures and conversation never leave memory.
- **[contracts/worker-api.md](./contracts/worker-api.md)**: request/response shape,
  status codes, and error body for each of the three routes, plus the rule that the
  client sends no credential.
- **[contracts/platform-api.md](./contracts/platform-api.md)**: the five platform
  interfaces, their threading and lifetime expectations, and the failure each is
  permitted to raise.
- **[quickstart.md](./quickstart.md)**: clone → deploy worker with secrets → set proxy
  base URL → `dotnet run`, plus the local-development variant and its explicit warning.

## Phase 2 — Task Generation Approach

`/speckit.tasks` should derive tasks from the milestone sequence below rather than from
the requirement list in order, because Principle IV demands each milestone be
independently demonstrable. Within a milestone, order is: platform interface definition
→ core logic with tests → platform implementation → UI wiring → manual verification
entry.

**M0 — Skeleton.** Tray icon, single instance, settings store, logging, no capabilities.
*Demo: it runs, it sits in the tray, it exits cleanly.*

**M1 — Activation.** Keyboard hook, companion overlay window with the square buddy
visible, idle ↔ listening state change on key hold.
*Demo: hold the key anywhere, the buddy reacts. (Covers SC-002.)*

**M2 — Backend.** Worker deployed with secrets, all three routes reachable, exercised
from a throwaway console project.
*Demo: curl the routes; no key anywhere in the client.*

**M3 — Screen to answer, text only.** Display capture on key release, `/chat` call
non-streaming, answer shown in the panel. Typed input stands in for voice.
*Demo: ask about the screen, read the answer. (First end-to-end proof of US1.)*

**M4 — Streaming and panel.** Swap to SSE; real panel UI; first-run disclosure and the
capture kill switch.
*Demo: US1 minus voice, plus US4.*

**M5 — Voice in.** WASAPI capture, streaming transcription, typed input removed.
*Demo: US1 by voice.*

**M6 — Voice out.** `/tts` and playback, speaking state.
*Demo: US1 complete. (SC-001 measurable here.)*

**M7 — Pointing.** Designation parsing, coordinate mapping, travel animation,
click-through and no-focus verification across a mixed-DPI multi-monitor setup.
*Demo: US2 complete. (SC-003, SC-004.)*

US3 (conversation continuity) is small and folds into M4; it is called out in tasks
rather than given its own milestone.

## Complexity Tracking

| Violation | Justification | Alternative rejected because |
|---|---|---|
| Local development stores provider keys in `worker/.dev.vars` on the developer machine, which is technically a credential at rest outside the Worker's secret store | Required to run the backend locally without deploying on every change; the file is git-ignored and never enters a client project or a build artifact | Deploying to Cloudflare for each change makes the inner loop unworkable; mocking all three providers locally defers real integration failures past the point Principle IV requires them surfaced. **Constraint: this exception applies only to `worker/.dev.vars`. No client project may read a key under any configuration, ever.** |
| Four client projects for a single desktop application | Mechanically enforces Principle V by making it a compile error for OS types to reach the core | A single project with folder conventions relies on discipline alone; this codebase's interop surface is exactly where discipline erodes under deadline pressure |

## Progress Tracking

- [x] Constitution ratified (v1.0.0)
- [x] Feature specification complete
- [x] Implementation plan drafted
- [x] Constitution Check — pre-Phase-0: PASS
- [x] Phase 0 research complete → `research.md`
- [x] Phase 1 design complete → `data-model.md`, `contracts/`, `quickstart.md`
- [x] Constitution Check — post-Phase-1: PASS
- [x] `/speckit.tasks` run → `tasks.md`
- [x] `/speckit.implement` run (2026-09-12) — all code tasks complete; manual-verification
  tasks pending a human run on real hardware (see tasks.md and tests/manual-verification.md)
