---

description: "Task list for the Core Companion Loop feature"
---

# Tasks: Core Companion Loop

**Input**: Design documents from `specs/001-core-companion-loop/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — plan.md specifies xUnit + NSubstitute for `Winly.Core`/`Winly.Providers`
and a documented manual-verification matrix for `Winly.Platform` (Constitution Principle V).

**Organization**: Per plan.md's "Phase 2 — Task Generation Approach", phases follow the
milestone sequence (M0–M7) rather than a flat requirement list, because Constitution
Principle IV requires each milestone to be independently demonstrable. Every task still
carries a `[USn]` label identifying which user story (spec.md) it serves; Setup,
Foundational, and Polish tasks carry no story label because they serve all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (P1), US2 (P2), US3 (P3), US4 (P3) — see spec.md
- File paths are exact, per plan.md's Project Structure

---

## Phase 1: Setup (M0 — project initialization)

**Purpose**: Solution scaffolding so any story's code has somewhere to live.

- [X] T001 Create the solution and four client project skeletons per plan.md Project
      Structure: `Winly.sln`, `src/Winly.Core/Winly.Core.csproj` (`net10.0`),
      `src/Winly.Platform/Winly.Platform.csproj` (`net10.0-windows10.0.22621.0`),
      `src/Winly.Providers/Winly.Providers.csproj` (`net10.0`),
      `src/Winly.App/Winly.App.csproj` (`net10.0-windows10.0.22621.0`, WPF)
- [X] T002 Add project references enforcing Constitution Principle V's reference
      direction — `Winly.App` → `Winly.Platform` and `Winly.App` → `Winly.Providers`;
      `Winly.Platform` → `Winly.Core`; `Winly.Providers` → `Winly.Core`; `Winly.Core`
      references nothing — in the four `.csproj` files from T001
- [X] T003 [P] Create test projects `tests/Winly.Core.Tests/Winly.Core.Tests.csproj` and
      `tests/Winly.Providers.Tests/Winly.Providers.Tests.csproj` referencing xUnit,
      NSubstitute, and Verify, each referencing its corresponding `src/` project
- [X] T004 [P] Add NuGet dependencies to the relevant projects per plan.md Technical
      Context: `H.NotifyIcon.Wpf` and `Microsoft.Windows.CsWinRT` and
      `Vortice.Direct3D11` → `Winly.Platform`; `NAudio` → `Winly.Platform`;
      `Serilog` + `Serilog.Sinks.File` → `Winly.App`
- [X] T005 [P] Scaffold the Cloudflare Worker project at `worker/` with
      `worker/package.json`, `worker/wrangler.toml`, and a stub `worker/src/index.ts`
- [X] T006 [P] Add `.editorconfig` at the repository root enforcing Constitution
      Principle VI (no abbreviations or single-character identifiers outside loop
      counters; consistent naming conventions across all four client projects)
- [X] T007 Create `tests/manual-verification.md` with an empty matrix (columns: Windows
      version, monitor configuration, scaling factor, verified by, date) per
      Constitution Principle V's manual-verification documentation requirement

---

## Phase 2: Foundational (M0 continued + M2 — blocking prerequisites)

**Purpose**: Backend routes and app skeleton every user story depends on.

**⚠️ CRITICAL**: No user story task in Phase 3+ may begin until this phase is complete.

- [X] T008 [P] Implement `POST /transcribe-token` in `worker/src/index.ts` per
      contracts/worker-api.md: request body `{}`, response
      `{ token, expiresInSeconds, endpoint }`, provider key read only from a Worker
      secret and never present in the response
- [X] T009 [P] Implement `POST /chat` in `worker/src/index.ts` per
      contracts/worker-api.md: accepts `{ transcript, displays[], history[] }`, streams
      `text/event-stream` `{ delta }` events and a final `{ done: true, pointingTarget }`
      event
- [X] T010 [P] Implement `POST /tts` in `worker/src/index.ts` per
      contracts/worker-api.md: accepts `{ text }`, returns `audio/mpeg` bytes
- [X] T011 Configure `worker/wrangler.toml` with non-secret vars only; document the
      three required `wrangler secret put` names (`CHAT_PROVIDER_API_KEY`,
      `SPEECH_TO_TEXT_PROVIDER_API_KEY`, `TEXT_TO_SPEECH_PROVIDER_API_KEY`) in
      `worker/README.md`
- [X] T012 Add `worker/.dev.vars.example` (placeholder values only) and confirm
      `worker/.dev.vars` is listed in `.gitignore` — the sole, explicitly scoped
      exception in plan.md's Complexity Tracking table
- [X] T013 [P] Define the `CompanionState` enum in
      `src/Winly.Core/Companion/CompanionState.cs` with values `Idle`, `Listening`,
      `Working`, `Speaking` per data-model.md
- [X] T014 [P] Define the `UserSettings` record in
      `src/Winly.Core/Settings/UserSettings.cs` with fields `ActivationKeyCombination`
      (string, default `"LWin+LAlt"`), `CompanionVisibilityMode` (enum
      `AlwaysVisible|InteractionOnly`, default `InteractionOnly`),
      `ScreenCaptureEnabled` (bool, default `true`), `MicrophoneCaptureEnabled` (bool,
      default `true`), `RunAtLogin` (bool, default `false`) — per data-model.md
- [X] T015 [P] Define `ISettingsStore` in `src/Winly.Core/Settings/ISettingsStore.cs`
      (`Task<UserSettings> Load()`, `Task Save(UserSettings settings)`)
- [X] T016 [P] Implement `JsonFileSettingsStore` in
      `src/Winly.Platform/Settings/JsonFileSettingsStore.cs` persisting to
      `%LOCALAPPDATA%\Winly\settings.json` (FR-030)
- [X] T017 [P] Configure Serilog rolling-file logging to `%LOCALAPPDATA%\Winly\logs\`
      in `src/Winly.App/App.xaml.cs` startup
- [X] T018 Implement a named-mutex single-instance guard
      (`Global\WinlyApp.SingleInstance`) in `src/Winly.App/App.xaml.cs`: on failure to
      acquire, show an "already running" tray notification and exit immediately without
      affecting the running instance (FR-033, Clarifications 2026-09-12, research.md §9)
- [X] T019 [P] Implement `TrayIconHost` in `src/Winly.App/Tray/TrayIconHost.cs` using
      `H.NotifyIcon.Wpf`, with no taskbar window (FR-001)
- [X] T020 Wire the DI composition root in `src/Winly.App/App.xaml.cs`, registering
      `ISettingsStore`, Serilog, and placeholder registrations for the platform/provider
      interfaces defined in later phases
- [X] T021 [P] Unit test: `JsonFileSettingsStore` round-trips defaults and persists
      changes across load/save, in
      `tests/Winly.Core.Tests/Settings/SettingsStoreTests.cs`

**Checkpoint**: Solution builds; Worker is deployable with all three routes reachable
and no key anywhere in client source; the app runs, sits in the tray, and exits
cleanly. No user story is independently demonstrable yet — that begins in Phase 3.

---

## Phase 3: User Story 1 - Ask about what is on screen and hear an answer (Priority: P1) 🎯 MVP

**Goal**: The complete voice-in → screen-context → spoken-answer loop (spec.md US1).

**Independent Test**: Hold the activation key, ask a question aloud about visible
screen content, release, and hear a spoken answer that references what's on screen.

This phase internally sequences plan.md's milestones M1 → M3 → M4 (streaming portion)
→ M5 → M6, each with its own checkpoint, per Constitution Principle IV.

### M1 — Activation

- [X] T022 [P] [US1] Define `IActivationKeyMonitor` in
      `src/Winly.Core/Abstractions/IActivationKeyMonitor.cs` per
      contracts/platform-api.md: `event Action KeyDown`, `event Action KeyUp`,
      `Task Start(KeyCombination combo, CancellationToken ct)` (throws
      `ActivationKeyUnavailableException` if the combination is already claimed),
      `Task Stop()`
- [X] T023 [P] [US1] Define `ICompanionOverlay` in
      `src/Winly.Core/Abstractions/ICompanionOverlay.cs` per contracts/platform-api.md:
      `SetState(CompanionState)`, `Task PointTo(PointingTarget, CancellationToken)`,
      `ReturnToRest()`, `bool VisibilityMode { get; set; }`
- [X] T024 [US1] Implement `CompanionStateMachine` in
      `src/Winly.Core/Companion/CompanionStateMachine.cs` enforcing only the legal
      transitions in data-model.md: `Idle → Listening → Working → Speaking → Idle`,
      plus a new activation interrupting from `Working` or `Speaking` back to
      `Listening` (FR-015)
- [X] T025 [P] [US1] Unit test: `CompanionStateMachine` rejects every transition not
      listed in data-model.md and allows the Working/Speaking → Listening interrupt, in
      `tests/Winly.Core.Tests/Companion/CompanionStateMachineTests.cs`
- [X] T026 [P] [US1] Implement `LowLevelKeyboardHookActivationMonitor` in
      `src/Winly.Platform/Input/LowLevelKeyboardHookActivationMonitor.cs`
      (`WH_KEYBOARD_LL` on its own thread with its own message loop, per research.md
      §3), default activation combination Windows key + Left Alt (Clarifications
      2026-09-12)
- [X] T027 [P] [US1] Implement `NativeInputMethods` in
      `src/Winly.Platform/Input/NativeInputMethods.cs` using source-generated
      `[LibraryImport]` for the `user32.dll` calls the hook needs — no third-party
      interop package (Constitution Principle III)
- [X] T028 [P] [US1] Implement `CompanionOverlayWindow` in
      `src/Winly.App/Overlay/CompanionOverlayWindow.xaml(.cs)` — one window per
      connected display, transparent, topmost, with `WS_EX_NOACTIVATE`,
      `WS_EX_LAYERED`, `WS_EX_TRANSPARENT` applied via
      `src/Winly.Platform/Overlay/OverlayWindowStyleApplier.cs` (FR-023, FR-024,
      research.md §4)
- [X] T029 [P] [US1] Implement `SquareBuddyControl` in
      `src/Winly.App/Overlay/SquareBuddyControl.xaml(.cs)`: a rounded `Border` body and
      two black-filled `Ellipse` eyes, with `VisualStateManager` states for
      `Idle`/`Listening`/`Working`/`Speaking` (FR-021, FR-022) — no external image,
      vector, or font assets (Constitution Principle III)
- [X] T030 [US1] Wire `IActivationKeyMonitor` → `CompanionStateMachine` →
      `ICompanionOverlay.SetState` in `src/Winly.App/App.xaml.cs`, verified to apply
      within 150ms of key press (SC-002)

**Checkpoint (M1)**: hold the key anywhere, the buddy visibly reacts.

### M3 — Screen to answer, text only

- [X] T031 [P] [US1] Define `IDisplayCapture` in
      `src/Winly.Core/Abstractions/IDisplayCapture.cs` per contracts/platform-api.md:
      `Task<IReadOnlyList<DisplayCapture>> CaptureAll(CancellationToken ct)` (throws
      `DisplayCaptureUnavailableException` for a display that cannot be captured)
- [X] T032 [P] [US1] Define the `DisplayCapture` record in
      `src/Winly.Core/Pointing/DisplayCapture.cs` with fields `MonitorId` (string),
      `ImageBytes` (byte[]), `WidthPx`/`HeightPx` (int), `IsPrimary` (bool) per
      data-model.md — exactly one `IsPrimary = true` per captured set (FR-008)
- [X] T033 [P] [US1] Define `IChatProvider` in
      `src/Winly.Core/Providers/IChatProvider.cs`: submits transcript + displays +
      history, returns streamed answer text plus an optional `PointingTarget`
- [X] T034 [US1] Implement `GraphicsCaptureDisplayCapture` in
      `src/Winly.Platform/Capture/GraphicsCaptureDisplayCapture.cs` using
      `IGraphicsCaptureItemInterop.CreateForMonitor` — no `GraphicsCapturePicker`, no
      per-capture prompt (research.md §1)
- [X] T035 [P] [US1] Implement `MonitorEnumerator` in
      `src/Winly.Platform/Capture/MonitorEnumerator.cs`: `EnumDisplayMonitors`,
      per-monitor DPI via `GetDpiForMonitor`, identifies the monitor containing the
      cursor as primary (FR-008)
- [X] T036 [US1] Implement `CapturedFrameEncoder` in
      `src/Winly.Platform/Capture/CapturedFrameEncoder.cs`: D3D texture → CPU readback
      → downscale to a longest edge of ≤1568px → JPEG at quality 80 (research.md §2)
- [X] T037 [P] [US1] Implement `ProxyChatProvider` (non-streaming first pass) in
      `src/Winly.Providers/Chat/ProxyChatProvider.cs`, posting to `/chat` per
      contracts/worker-api.md — sends no credential (FR-034)
- [X] T038 [US1] Wire key-release → `IDisplayCapture.CaptureAll` → `IChatProvider` →
      a minimal `CompanionPanelWindow` in
      `src/Winly.App/Panel/CompanionPanelWindow.xaml(.cs)` showing the answer as text,
      with typed input standing in for voice for this milestone and the empty-input
      guard (FR-006) applied

**Checkpoint (M3)**: ask (typed) about the screen, read the answer in the panel — first
end-to-end proof of US1.

### M4 (US1 portion) — Streaming

- [X] T039 [US1] Convert `ProxyChatProvider` to consume the `/chat` response as
      Server-Sent Events via `HttpClient` with `HttpCompletionOption.
      ResponseHeadersRead`, appending each `delta` as it arrives, in
      `src/Winly.Providers/Chat/ProxyChatProvider.cs` and
      `src/Winly.Providers/Chat/ServerSentEventReader.cs` (research.md §8)
- [X] T040 [P] [US1] Wire a new activation to cancel the in-flight SSE read via a
      shared `CancellationTokenSource` (FR-015), in `src/Winly.App/App.xaml.cs`

**Checkpoint (M4/US1 portion)**: answer text streams into the panel incrementally.

### M5 — Voice in

- [X] T041 [P] [US1] Define `IMicrophoneCapture` in
      `src/Winly.Core/Abstractions/IMicrophoneCapture.cs` per contracts/platform-api.md:
      `Task<Stream> StartCapture(CancellationToken ct)` (throws
      `MicrophoneUnavailableException`), `Task StopCapture()`
- [X] T042 [US1] Implement `WasapiMicrophoneCapture` in
      `src/Winly.Platform/Audio/WasapiMicrophoneCapture.cs` using NAudio's
      `MediaFoundationResampler` → 16kHz mono PCM16, falling back to
      `WdlResamplingSampleProvider` when Media Foundation is unavailable (research.md
      §6), capturing for at most 30 seconds before stopping automatically (FR-004)
- [X] T043 [P] [US1] Define `ISpeechToTextProvider` in
      `src/Winly.Core/Providers/ISpeechToTextProvider.cs`: streams PCM16 frames,
      returns the final transcript, signals utterance-complete
- [X] T044 [US1] Implement `TranscriptionTokenClient` in
      `src/Winly.Providers/SpeechToText/TranscriptionTokenClient.cs` calling
      `POST /transcribe-token` per contracts/worker-api.md
- [X] T045 [US1] Implement `StreamingSpeechToTextProvider` in
      `src/Winly.Providers/SpeechToText/StreamingSpeechToTextProvider.cs` using
      `ClientWebSocket` directly against the provider with the minted token
      (research.md §7, Constitution Principle I's direct-connection exception) —
      never holds a long-lived provider credential
- [X] T046 [US1] Wire key-hold → `IMicrophoneCapture.StartCapture` →
      `StreamingSpeechToTextProvider`, key-release or the 30s cap → `StopCapture` →
      final transcript; an empty transcript returns to `Idle` without issuing a request
      (FR-005, FR-006), replacing the typed-input stand-in from T038, in
      `src/Winly.App/App.xaml.cs`
- [X] T047 [P] [US1] Unit test: an empty or unintelligible transcript produces no
      request and the state machine returns to `Idle`, in
      `tests/Winly.Core.Tests/Companion/EmptyTranscriptGuardTests.cs` (FR-006)

**Checkpoint (M5)**: US1 works fully by voice for the "ask" half.

### M6 — Voice out

- [X] T048 [P] [US1] Define `IAudioPlayback` in
      `src/Winly.Core/Abstractions/IAudioPlayback.cs` per contracts/platform-api.md:
      `Task Play(Stream audio, CancellationToken ct)` (throws
      `PlaybackDeviceUnavailableException`), `Task Stop()`
- [X] T049 [US1] Implement `NAudioPlayback` in
      `src/Winly.Platform/Audio/NAudioPlayback.cs` playing the MP3 bytes returned from
      `/tts`
- [X] T050 [P] [US1] Implement `ProxyTextToSpeechProvider` in
      `src/Winly.Providers/TextToSpeech/ProxyTextToSpeechProvider.cs` posting the
      stripped answer text to `/tts` per contracts/worker-api.md
- [X] T051 [US1] Implement `AnswerTextSanitizer` in
      `src/Winly.Core/Companion/AnswerTextSanitizer.cs`, removing markup, list syntax,
      and symbols that read unnaturally aloud before text reaches
      `ProxyTextToSpeechProvider` (FR-012)
- [X] T052 [P] [US1] Unit test: `AnswerTextSanitizer` removes markdown/list/symbol
      artifacts and passes plain sentences through unchanged, in
      `tests/Winly.Core.Tests/Companion/AnswerTextSanitizerTests.cs`
- [X] T053 [US1] Wire answer-complete → `CompanionStateMachine.Speaking` →
      `IAudioPlayback.Play`, with a new activation stopping in-flight playback
      (FR-015), in `src/Winly.App/App.xaml.cs`

**Checkpoint (M6 / US1 complete)**: full ask-aloud → hear-answer loop works;
SC-001 (≤4s median / ≤7s p95, key release to first audible word) is measurable.

---

## Phase 4: User Story 2 - See the companion point at what it is describing (Priority: P2)

**Goal**: The companion travels to and indicates the specific on-screen element an
answer designates (spec.md US2).

**Independent Test**: With US1 working, ask "where do I change X" in an application
that has such a control; confirm the companion travels there and remains while
speaking.

This phase is plan.md's M7.

- [X] T054 [P] [US2] Define the `PointingTarget` record in
      `src/Winly.Core/Pointing/PointingTarget.cs` with fields `MonitorId` (string),
      `XInCapturePx`/`YInCapturePx` (int — MUST be clamped to
      `[0, WidthPx) x [0, HeightPx)` of the matching `DisplayCapture` before
      conversion, per data-model.md and FR-020), `Label` (string)
- [X] T055 [US2] Implement `PointingDesignationParser` in
      `src/Winly.Core/Pointing/PointingDesignationParser.cs`: extracts `pointingTarget`
      from the `/chat` SSE final event and strips any residual in-band designation
      markup from the spoken text (FR-016, FR-017)
- [X] T056 [P] [US2] Unit test: `PointingDesignationParser` strips designation markup
      while leaving narration untouched, and a `null` `pointingTarget` produces no
      designation, in
      `tests/Winly.Core.Tests/Pointing/PointingDesignationParserTests.cs`
- [X] T057 [US2] Implement `CaptureToDesktopCoordinateMapper` in
      `src/Winly.Core/Pointing/CaptureToDesktopCoordinateMapper.cs` as the three
      explicit steps from research.md §5: capture-pixel → physical desktop pixel (add
      monitor origin) → WPF device-independent pixel (divide by per-monitor scale
      factor ÷ 96)
- [X] T058 [P] [US2] Unit test: `CaptureToDesktopCoordinateMapper` against fixtures
      covering a mixed-scale-factor multi-monitor case and a negative-origin monitor
      layout, in
      `tests/Winly.Core.Tests/Pointing/CaptureToDesktopCoordinateMapperTests.cs`
      (Constitution: Per-monitor DPI correctness requires this exact test case)
- [X] T059 [P] [US2] Unit test: an out-of-bounds `PointingTarget` is clamped into its
      display's valid range rather than discarded, in
      `tests/Winly.Core.Tests/Pointing/PointingTargetClampingTests.cs` (FR-020)
- [X] T060 [US2] Implement `PointingAnimator` in
      `src/Winly.App/Overlay/PointingAnimator.cs`: a `PointAnimation` along a
      `BezierSegment` from the current position to the mapped desktop location
      (FR-019 — no instant jump)
- [X] T061 [US2] Wire `PointingDesignationParser` output →
      `CaptureToDesktopCoordinateMapper` → `ICompanionOverlay.PointTo` →
      `PointingAnimator`, calling `ReturnToRest()` when no target is designated
      (FR-016), in `src/Winly.App/App.xaml.cs`
- [ ] T062 [US2] Manual verification entry in `tests/manual-verification.md`: confirm
      click-through reaches the underlying application while pointing (FR-024) and the
      companion never takes keyboard focus, across a real mixed-DPI multi-monitor rig
      — record the Windows version and monitor configuration tested (Constitution
      Principle V)

**Checkpoint**: US2 complete — SC-003 and SC-004 are measurable.

---

## Phase 5: User Story 3 - Continue a conversation without repeating context (Priority: P3)

**Goal**: Follow-up questions are answered coherently using the current session's
prior exchanges (spec.md US3).

**Independent Test**: Ask a question, receive an answer, then ask "why?" and confirm
the second answer reflects the first.

- [X] T063 [P] [US3] Implement `ConversationSession` in
      `src/Winly.Core/Companion/ConversationSession.cs`: an ordered in-memory list of
      `Exchange` (`Transcript`, `AnswerText`, `PointingTarget?`, `CreatedAtUtc`),
      discarded on exit and never serialized (FR-013, FR-014)
- [X] T064 [US3] Wire `ConversationSession.Exchanges` into the `history` field of every
      `/chat` request via `IChatProvider`, appending each completed exchange after its
      answer is spoken, in `src/Winly.App/App.xaml.cs`
- [X] T065 [P] [US3] Unit test: `ConversationSession` is empty on construction
      (simulating a restart) and accumulates exchanges in order within one session, in
      `tests/Winly.Core.Tests/Companion/ConversationSessionTests.cs` (FR-014)

**Checkpoint**: US3 complete — follow-up questions carry session context, and a
restart starts with an empty session.

---

## Phase 6: User Story 4 - Control when the companion can see and hear (Priority: P3)

**Goal**: The user controls capture consent, companion visibility, and settings
persistence (spec.md US4).

**Independent Test**: Disable capture in settings, restart, hold the activation key,
and confirm nothing is captured and the user is told why.

- [X] T066 [P] [US4] Implement `FirstRunDisclosureWindow` in
      `src/Winly.App/Panel/FirstRunDisclosureWindow.xaml(.cs)`: plain-language
      disclosure of what is captured, when, and where it is sent, shown before any
      capture occurs on first launch (FR-026)
- [X] T067 [US4] Extend `CompanionPanelWindow` (T038) with settings UI for
      `ActivationKeyCombination`, `CompanionVisibilityMode`, `ScreenCaptureEnabled`,
      `MicrophoneCaptureEnabled`, and `RunAtLogin`, persisting every change via
      `ISettingsStore` (FR-030)
- [X] T068 [US4] Enforce capture gating in `src/Winly.App/App.xaml.cs`: when
      `ScreenCaptureEnabled` or `MicrophoneCaptureEnabled` is `false`, activation MUST
      NOT call `IDisplayCapture`/`IMicrophoneCapture` and MUST inform the user why
      (FR-028)
- [X] T069 [P] [US4] Implement a persistent, glanceable capture-active indicator
      (tray icon state and/or overlay badge) shown whenever screen or microphone
      capture is active, in `src/Winly.App/Tray/TrayIconHost.cs` and
      `src/Winly.App/Overlay/CompanionOverlayWindow.xaml(.cs)` (FR-027)
- [X] T070 [US4] Enforce `CompanionVisibilityMode` in
      `src/Winly.App/Overlay/CompanionOverlayWindow.xaml(.cs)`:
      `AlwaysVisible` keeps the overlay shown continuously; `InteractionOnly` shows it
      only for the duration of an interaction and withdraws afterward (FR-025)
- [X] T071 [P] [US4] Implement a run-at-login registrar writing/removing the
      `Software\Microsoft\Windows\CurrentVersion\Run` registry value, in
      `src/Winly.Platform/Settings/RunAtLoginRegistrar.cs` (research.md §9)
- [X] T072 [P] [US4] Unit test: settings changes persist across a simulated
      reload (`ISettingsStore` round-trip), in
      `tests/Winly.Core.Tests/Settings/SettingsPersistenceTests.cs` (FR-030)

**Checkpoint**: US4 complete — SC-006 (first launch to first successful question under
3 minutes) is measurable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Requirements that span every user story rather than belonging to one.

- [X] T073 [P] Implement uniform failure handling in `src/Winly.App/App.xaml.cs`:
      every `IChatProvider`/`ISpeechToTextProvider`/`ITextToSpeechProvider`/
      `IMicrophoneCapture`/`IDisplayCapture`/`IAudioPlayback` failure is logged via
      Serilog with diagnosable context and surfaced as a short plain-language message
      with no error codes or provider names, returning the state machine to `Idle`
      (FR-031, SC-010)
- [X] T074 [P] Unit test: every mapped failure type produces a user-facing message
      containing no error codes, exception names, or provider names, in
      `tests/Winly.Core.Tests/FailureMessageMappingTests.cs` (SC-010)
- [X] T075 [P] Add an idle-state entry to `tests/manual-verification.md` confirming no
      open microphone or display capture handle and negligible CPU while idle
      (SC-005, FR-032)
- [ ] T076 [P] Run `quickstart.md` end to end on a real mixed-DPI multi-monitor Windows
      machine and record the Windows version and monitor configuration tested in
      `tests/manual-verification.md`
- [X] T077 [P] Confirm no third-party credential appears anywhere in the built client
      output by searching all `src/*/bin/**` artifacts for the three provider key
      values (SC-008, FR-034)
- [X] T078 [P] Add `THIRD-PARTY-NOTICES.txt` at the repository root preserving the
      license notices for every dependency listed in plan.md's Technical Context
      (Constitution Principle III)
- [ ] T079 [P] Build a latency-measurement harness recording key-release-to-first-
      audible-word timing across at least 20 real activations on a broadband
      connection; record median and p95 against the ≤4s/≤7s targets in
      `tests/manual-verification.md` (SC-001)
- [ ] T080 [P] Conduct and document a pointing-accuracy test across a 10-application
      test set, recording the percentage of attempts landing within the intended
      element's bounds against the ≥80% target, in `tests/manual-verification.md`
      (SC-003)
- [ ] T081 [P] Run a 20-consecutive-activation soak test and confirm no activation
      leaves the application in a state requiring restart; record the run in
      `tests/manual-verification.md` (SC-009)
- [ ] T082 [P] Verify no capture-related file writes occur: monitor the file system
      (e.g., Process Monitor) across a multi-activation session and confirm no
      screen frame or audio buffer is ever written to disk; record the result in
      `tests/manual-verification.md` (FR-029)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story phase.
- **User Story 1 (Phase 3)**: Depends on Foundational only. Internally sequential
  (M1 → M3 → M4 → M5 → M6) because each milestone's demo builds on the previous one's
  wiring — this is the one story where later tasks genuinely depend on earlier ones
  within the same phase.
- **User Story 2 (Phase 4)**: Depends on Foundational and on US1's `IChatProvider` /
  `DisplayCapture` / `ICompanionOverlay` plumbing (T033, T032, T023) being in place —
  pointing designations ride on the same `/chat` response US1 already consumes.
- **User Story 3 (Phase 5)**: Depends on Foundational and on US1's `IChatProvider`
  wiring (T037/T039) — history rides on the same request.
- **User Story 4 (Phase 6)**: Depends on Foundational and on US1's
  `CompanionPanelWindow` (T038) and `CompanionOverlayWindow` (T028) existing to extend.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Story Independence Note

US2, US3, and US4 each read from the same `/chat` exchange US1 produces, but none of
them modify US1's own acceptance criteria — each can be implemented, tested, and
demoed on its own once Foundational + US1 exist, matching spec.md's Independent Test
for each story.

### Parallel Opportunities

- All `[P]`-marked Setup tasks (T003–T006) run in parallel.
- All `[P]`-marked Foundational tasks (T008–T010, T013–T017, T019, T021) run in
  parallel once T007 exists.
- Within US1: T022/T023 in parallel; T026/T027/T028/T029 in parallel; T031/T032/T033
  in parallel; T035 parallel with T034; T041/T043 in parallel; T048/T050 in parallel.
- Once US1 (Phase 3) is complete, US2, US3, and US4 can be staffed and built in
  parallel by different developers.
- All `[P]`-marked Polish tasks (T073–T078) run in parallel.

---

## Parallel Example: User Story 1, M1 sub-phase

```bash
# Interfaces (no shared files):
Task: "Define IActivationKeyMonitor in src/Winly.Core/Abstractions/IActivationKeyMonitor.cs"
Task: "Define ICompanionOverlay in src/Winly.Core/Abstractions/ICompanionOverlay.cs"

# Platform + UI implementations (no shared files):
Task: "Implement LowLevelKeyboardHookActivationMonitor in src/Winly.Platform/Input/LowLevelKeyboardHookActivationMonitor.cs"
Task: "Implement NativeInputMethods in src/Winly.Platform/Input/NativeInputMethods.cs"
Task: "Implement CompanionOverlayWindow in src/Winly.App/Overlay/CompanionOverlayWindow.xaml(.cs)"
Task: "Implement SquareBuddyControl in src/Winly.App/Overlay/SquareBuddyControl.xaml(.cs)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (critical — blocks every story).
3. Complete Phase 3: User Story 1, milestone by milestone (M1 → M3 → M4 → M5 → M6),
   stopping to validate the demo at each milestone checkpoint per Constitution
   Principle IV.
4. **STOP and VALIDATE**: run `quickstart.md` sections 1–4. This is the MVP.

### Incremental Delivery

1. Setup + Foundational → nothing user-visible yet, but the Worker is live and the
   app runs.
2. User Story 1 → the entire ask/hear loop → demo-able MVP (SC-001, SC-002).
3. User Story 2 → pointing → demo-able increment (SC-003, SC-004).
4. User Story 3 → conversation continuity → demo-able increment.
5. User Story 4 → consent, settings, visibility control → demo-able increment
   (SC-006).
6. Polish → cross-cutting resilience and compliance checks (SC-005, SC-007, SC-008,
   SC-009, SC-010).

### Parallel Team Strategy

With multiple developers, once Foundational is done and US1 has reached its M6
checkpoint:

- Developer A: User Story 2 (pointing)
- Developer B: User Story 3 (conversation continuity)
- Developer C: User Story 4 (consent/settings)

Each integrates against the same `/chat` plumbing US1 already established, without
modifying US1's own code paths.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- `[Story]` labels trace every implementation task back to spec.md.
- Constitution Principle V is enforced at the project-reference level (T002), not just
  by convention — a Windows-specific type cannot compile into `Winly.Core`.
- Manual-verification entries (T007, T062, T075, T076) exist because
  `Winly.Platform` cannot be exercised in CI (Constitution Principle V) — each must
  record the Windows version and monitor configuration tested.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before moving on.

## Implementation notes (2026-09-12, `/speckit-implement`)

- **Open tasks** T062, T075, T076, T079–T082 need a human on real hardware; rows are
  prepared in `tests/manual-verification.md`. Everything else is done and verified by
  `dotnet build` (0 warnings), `dotnet test` (78 passing), `npm run typecheck`, and a
  scripted launch + simulated activation of the built app.
- **T003** — `Verify.Xunit` is referenced and used for the SSE-reader snapshot only;
  other tests use plain asserts.
- **T004** — `Microsoft.Windows.CsWinRT` was not added: the Windows-targeted TFM already
  ships the WinRT projection (`Microsoft.Windows.SDK.NET.Ref`), and adding CsWinRT on
  top only risks runtime-version conflicts. `H.NotifyIcon.Wpf` lives in `Winly.App`
  (where the tray is), not `Winly.Platform` (which has no WPF).
- **T016/T021** — `JsonFileSettingsStore` stays in `Winly.Platform` per plan.md, so
  `Winly.Core.Tests` targets the Windows TFM to reference it; no test touches a desktop.
- **T020** — composition is hand-wired in `App.xaml.cs` rather than through a DI
  container; the loop itself is `CompanionOrchestrator` in `Winly.Core`, tested with
  NSubstitute fakes (T047 and neighbours).
- **T038/T046** — the typed-input stand-in was not built because voice input (M5) was
  implemented in the same pass; M3's checkpoint is covered by the orchestrator tests.
- **T042** — no in-process resampler: NAudio 3's `WasapiRecorderBuilder.WithFormat`
  asks the audio engine for 16 kHz mono PCM16 directly (research.md §6 amendment).
- **T054/T061** — `ICompanionOverlay.PointTo` takes the mapped `DesktopLocation`, so
  clamping and DPI math stay in `Winly.Core` (contracts/platform-api.md updated).
- **Platform TFM** is `net10.0-windows10.0.22621.0` so `IsBorderRequired` is visible;
  `SupportedOSPlatformVersion` remains 10.0.19041 (plan.md updated).
- **Worker providers**: Anthropic `claude-opus-5` (chat), AssemblyAI streaming v3 token
  (speech-to-text), ElevenLabs streaming synthesis (text-to-speech) — all configurable
  in `worker/wrangler.toml`, documented in `worker/README.md`.
- **TTS swap (2026-09-12)**: replaced OpenAI Audio Speech with ElevenLabs' streaming
  `/v1/text-to-speech/{voice_id}/stream` endpoint for better conversational voice
  quality; the `/tts` route already just forwards the upstream body, so no client-side
  change was needed (Constitution Principle VII's provider-substitutability holds).
