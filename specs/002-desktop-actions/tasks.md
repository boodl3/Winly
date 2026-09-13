---

description: "Task list for Desktop Actions"
---

# Tasks: Desktop Actions

**Input**: Design documents from `/specs/002-desktop-actions/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/worker-api.md](./contracts/worker-api.md)

**Tests**: Included and **not optional**. The Constitution's Development Workflow requires
core logic changes to ship with tests, and platform-layer changes to document manual
verification. Both appear below as tasks.

**Organization**: Grouped by user story. The three milestones in plan.md map to stories:
M1 = US1 + US2, M2 = US3 + US4, M3 = US5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths are included in every task

## Path Conventions

Existing four-project client plus Worker (no new projects):

- `src/Winly.Core/` — testable logic; `src/Winly.Platform/` — interop
- `src/Winly.Providers/` — backend clients; `src/Winly.App/` — WPF composition
- `tests/Winly.Core.Tests/`, `tests/Winly.Providers.Tests/`
- `worker/src/index.ts` — the Cloudflare Worker

---

## Phase 1: Setup

**Purpose**: Establish the baseline this feature is measured against

- [X] T001 Confirm baseline is green before changing anything: run `dotnet test Winly.sln` with `DOTNET_ROOT` set to `%LOCALAPPDATA%\Microsoft\dotnet`, and record the passing test count in the PR description
- [X] T001b **Closed as won't-do (2026-09-13): `SPOTIFY_CLIENT_ID` / `SPOTIFY_CLIENT_SECRET` are deliberately left unset.** The user declined account linking outright — "Winly should be able to do tasks for me as if it was an intern I hired" — so `play` drives Spotify's own window through UI Automation (`SpotifyUiControl`) and needs no account at all; it was verified on hardware the same day. US1's headline demo is therefore verified *without* these secrets, and T017's device-readiness check was removed with the account-side device wait (see CLAUDE.md, "Spotify readiness stops at the window"). What stays unavailable is `queue` and Spotify's own volume slider, both of which degrade with a spoken message rather than failing. Reopen only alongside a `/music/devices` route.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Carry an ordered list of actions from the model to the client. Until all of
this lands, no story below can work — the limit is enforced in four places and fixing any
subset changes nothing observable (plan.md, Summary).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 [P] Create `ActionOutcome` in `src/Winly.Core/Actions/ActionOutcome.cs` with status values `Completed`, `Failed`, `Declined`, `Abandoned`, `Blocked` and fields `Action` (DesktopAction), `Status`, `UserFacingReason` (string, plain language, **no error codes or exception names** per FR-008, empty when `Completed`), `FollowUpSpeech` (string?, null when the action speaks for itself)
- [X] T003 Worker: replace the single-match extraction `/@@DO\s*(\{[\s\S]*?\})\s*@@/.exec(this.pending)` with `matchAll` collecting every designation **in document order** in `worker/src/index.ts`; validate each independently so **a malformed designation is discarded and the valid ones around it are kept** (FR-010); cap output at 5
- [X] T004 Worker: change the terminal `done` event in `worker/src/index.ts` to emit `actions` (ordered array, always present, `[]` when none) **and** keep `action` (deprecated, equal to `actions[0]` or `null`) per [contracts/worker-api.md](./contracts/worker-api.md) §1
- [X] T005 Worker: in the system prompt in `worker/src/index.ts`, replace `"Only ever write one."` with guidance to emit one designation per thing the user asked to happen, in the order they must happen, up to five, an application before anything depending on it; leave the "never mention any designation line" and "only when the user actually asked" instructions unchanged
- [X] T006 Change `DesktopActionParser.Parse` in `src/Winly.Core/Actions/DesktopActionParser.cs` to return `IReadOnlyList<DesktopAction>` (never null, empty when none) using `Regex.Matches`, keeping per-tag validation in `TryParseTag` unchanged, and **strip every** `@@DO …@@` tag from the narration — a residual tag reaching TTS is read aloud as JSON
- [X] T007 [P] Add `DesktopActionParserTests` cases in `tests/Winly.Core.Tests/DesktopActionParserTests.cs` for: multiple tags parsed in order, a malformed tag among valid ones (valid ones survive), all tags stripped from narration, and no tags at all yielding an empty list
- [X] T008 Change `ChatAnswer.Action` to `ChatAnswer.Actions` (`IReadOnlyList<DesktopAction>`, never null) in `src/Winly.Core/Providers/IChatProvider.cs`
- [X] T009 In `src/Winly.Providers/Chat/ProxyChatProvider.cs`, read `actions` when present and otherwise wrap the deprecated `action` into a one-element list, so either deployment order works ([research.md](./research.md) R5)
- [X] T010 [P] Add a test in `tests/Winly.Providers.Tests/` covering a `done` event with `actions`, one with only the legacy `action`, and one with neither

**Checkpoint**: An ordered list of actions now reaches the client. Nothing executes more than the first yet.

---

## Phase 3: User Story 1 - One request completes the whole task (Priority: P1) 🎯 MVP

**Goal**: A request needing two or more actions carries out every one of them, in order, from a single activation.

**Independent Test**: With Spotify installed and closed, hold the key and say "open Spotify and play some jazz." Spotify opens **and** jazz plays, without a second activation.

### Tests for User Story 1

- [X] T011 [P] [US1] Create `tests/Winly.Core.Tests/DesktopActionSequenceRunnerTests.cs` covering: all actions run, strict declared order, a single-action sequence behaves as before, an empty list runs nothing, a list of six actions is truncated to 5 with the sequence reported unfinished (FR-003), and a sequence cancelled mid-run stops before the next action and reports the remainder `Abandoned`
- [X] T012 [P] [US1] Add readiness tests to `tests/Winly.Core.Tests/DesktopActionSequenceRunnerTests.cs`: an action following a launch in the same sequence waits for readiness first, and readiness never arriving fails that action at its bound (FR-005)

### Implementation for User Story 1

- [X] T013 [US1] Add `Task<bool> WaitForApplicationReady(string appName, TimeSpan timeout, CancellationToken cancellationToken)` to `IDesktopActions` in `src/Winly.Core/Actions/DesktopAction.cs`, so readiness stays behind a Core interface (Principle V)
- [X] T014 [US1] Create `DesktopActionSequenceRunner` in `src/Winly.Core/Actions/DesktopActionSequenceRunner.cs`: executes strictly sequentially in declared order (FR-002), never concurrently, truncates to **at most 5 actions** (FR-003), waits for readiness before an action that depends on an application launched earlier in the same sequence (FR-005), and bounds each action individually — **60 s for a readiness wait, 10 s for anything else** (FR-004, FR-005a), with **no aggregate bound on the sequence**. The runner MUST check its `CancellationToken` at each action boundary so a newer activation abandons the sequence before the next action starts, consistent with `001`'s FR-015 and the spec's re-activation edge case
- [X] T015 [US1] Implement `WaitForApplicationReady` in `src/Winly.Platform/Actions/WindowsDesktopActions.cs` per [research.md](./research.md) R1: poll `AppMatcher` against the **live process list** every 250 ms for a matching process owning a visible main window that answers `SendMessageTimeout(WM_NULL)` within 100 ms; **do not** hold or wait on the `Process` handle returned by `Process.Start` — Spotify, Discord and Electron apps launch a stub that exits
- [X] T016 [US1] Add the `SendMessageTimeout` / `WM_NULL` declarations to `src/Winly.Platform/Actions/NativeDesktopMethods.cs`
- [X] T017 [US1] Extend readiness for `Play` and `Queue` in `src/Winly.Platform/Actions/WindowsDesktopActions.cs` to also require that `POST /music/state` reports an active device, via `src/Winly.Providers/Music/ProxyMusicService.cs` — a Spotify window exists seconds before the account has a device, and playing too early returns a 404 that reads as "it didn't work"
- [X] T018 [US1] Replace the single `RunDesktopAction` call in `src/Winly.Core/Companion/CompanionOrchestrator.cs` (~line 323) with the sequence runner, keeping it started **before** the answer finishes playing so applications open while Winly is still speaking, and keeping the `Timer` verb handled in the orchestrator where the voice is available
- [X] T019 [US1] Update the manual-verification matrix in `tests/manual-verification.md` with quickstart scenarios 1–5, including Windows version and monitor configuration columns

**Checkpoint**: The reported defect is fixed. "Open Spotify and play some jazz" does both.

---

## Phase 4: User Story 2 - A failure stops the sequence and says so (Priority: P1)

**Goal**: One step failing stops the sequence rather than carrying on into a state nobody intended, and the user is told where it got to.

**Independent Test**: Ask Winly to open an application that is not installed and then do something with it. It stops after the failure, never attempts the second action, and says which part did not work.

### Tests for User Story 2

- [X] T020 [P] [US2] Add to `tests/Winly.Core.Tests/DesktopActionSequenceRunnerTests.cs`: a failing action stops the sequence (FR-007), every later action is recorded `Abandoned`, outcomes are collected in order, and an action exceeding its own time bound is treated as `Failed`
- [X] T021 [P] [US2] Add tests in `tests/Winly.Core.Tests/FailureMessagesTests.cs` asserting partial-completion phrasing names what completed and what did not and contains **no error codes or exception names** (FR-008)

### Implementation for User Story 2

- [X] T022 [US2] Add stop-on-first-failure and outcome collection to `src/Winly.Core/Actions/DesktopActionSequenceRunner.cs`: halt at the first outcome that is not `Completed`, mark every remaining action `Abandoned`, and return the full ordered outcome list
- [X] T023 [US2] Add partial-completion phrasing to `src/Winly.Core/Companion/FailureMessages.cs` — plain language naming what completed and what did not, no exception text
- [X] T024 [US2] In `src/Winly.Core/Companion/CompanionOrchestrator.cs`, speak or post the partial-completion report after the answer finishes, ensuring a failed action **never interrupts or discards the answer already in progress** (FR-009, Principle VII) and that the next activation works normally
- [X] T025 [US2] Add quickstart scenario 6 to `tests/manual-verification.md`

**Checkpoint**: US1 and US2 together are milestone M1 — chaining works and fails safely.

---

## Phase 5: User Story 3 - Consequential actions are confirmed first (Priority: P2)

**Goal**: Before closing an application, locking or sleeping the machine, or typing into whatever holds focus, Winly says what it is about to do and waits for a spoken answer.

**Independent Test**: Ask Winly to close an application with unsaved work. It states the action aloud and listens; saying no leaves the application open; the same request in a later session asks again.

### Tests for User Story 3

- [X] T026 [P] [US3] Create `tests/Winly.Core.Tests/ConsequentialActionClassifierTests.cs` asserting the classification table in [data-model.md](./data-model.md) verb by verb, **enumerating `DesktopActionKind` exhaustively** so adding a verb without classifying it fails the test rather than defaulting to "not consequential"
- [X] T027 [P] [US3] Create `tests/Winly.Core.Tests/SpokenConfirmationTests.cs` covering agreement words, refusal words, an empty transcript, and an unrecognized transcript — the last two both resolving to **refusal** (FR-012b)
- [X] T028 [P] [US3] Add to `tests/Winly.Core.Tests/DesktopActionSequenceRunnerTests.cs`: a consequential action is not run before agreement (FR-012), a refusal stops the sequence (FR-014), agreement covers exactly one action and is not carried to a later one (FR-013), and non-consequential actions run with no confirmation (FR-015)

### Implementation for User Story 3

- [X] T029 [P] [US3] Create `ConsequentialActionClassifier` in `src/Winly.Core/Actions/ConsequentialActionClassifier.cs` implementing the table in [data-model.md](./data-model.md): `Window`+`close`, `System`+`lock`, `System`+`sleep`, and **all** `Type` are consequential; everything else is not. Where a verb's classification is debatable, classify it consequential
- [X] T030 [P] [US3] Create `SpokenConfirmation` in `src/Winly.Core/Actions/SpokenConfirmation.cs` with the agreement list (`yes`, `yeah`, `yep`, `sure`, `okay`, `ok`, `go ahead`, `do it`, `confirm`, `please do`) and refusal list (`no`, `nope`, `dont`, `cancel`, `stop`, `never mind`); **anything not recognized as agreement, including an empty transcript, is a refusal**
- [X] T031 [US3] Add the confirmation gate to `src/Winly.Core/Actions/DesktopActionSequenceRunner.cs` via an injected confirmation callback, so the runner stays testable headless; a `Declined` outcome stops the sequence, and time spent awaiting confirmation is **excluded** from the per-action bounds
- [X] T032 [US3] Wire the confirmation callback in `src/Winly.Core/Companion/CompanionOrchestrator.cs` per [research.md](./research.md) R2: speak the prompt, **await playback completion before opening the microphone** (otherwise Winly transcribes its own question), then capture and transcribe with a **5 s** bound, showing the `001` capture indicator and calling `StopCapture` in a `finally` (FR-012a)
- [X] T033 [US3] Capture the foreground window handle at activation start in `src/Winly.Core/Companion/CompanionOrchestrator.cs`, before Winly speaks or acts — that is the only moment corresponding to what the user meant by "here"
- [X] T034 [US3] Verify the typing target in `src/Winly.Platform/Actions/UserInputControl.cs`: immediately before `SendInput`, compare `GetForegroundWindow` against the handle captured at activation start and **abandon rather than type elsewhere** if it moved or the window is gone (FR-016)
- [X] T035 [US3] Also abandon typing in `src/Winly.Platform/Actions/UserInputControl.cs` when the foreground window's owning process cannot be opened for query — the signature of an elevated target, into which `SendInput` returns success having delivered nothing. Report it as refused by the OS, never retried or escalated (FR-021)
- [X] T036 [US3] Add quickstart scenarios 7–11 to `tests/manual-verification.md`, noting explicitly that scenario 8 must confirm Winly is not transcribing its **own** prompt as the answer

**Checkpoint**: Irreversible actions are gated. Declining or staying silent stops the sequence.

---

## Phase 6: User Story 4 - Acting is a separate permission from seeing (Priority: P2)

**Goal**: The user can grant sight and hearing without granting hands, and revoke acting at any time — including mid-sequence.

**Independent Test**: Disable actions in settings, restart, then ask Winly to open an application. Nothing opens and Winly says why.

### Tests for User Story 4

- [X] T037 [P] [US4] Add to `tests/Winly.Core.Tests/DesktopActionSequenceRunnerTests.cs`: with acting disabled every action is `Blocked` and none runs (FR-018), and acting disabled **mid-sequence** stops it before the next action (FR-019)

### Implementation for User Story 4

- [X] T038 [US4] Re-check `Settings.DesktopActionsEnabled` **before each action** in `src/Winly.Core/Actions/DesktopActionSequenceRunner.cs`, not once per request, so revoking mid-sequence takes effect at the next boundary (FR-019)
- [X] T039 [US4] In `src/Winly.Core/Companion/CompanionOrchestrator.cs`, ensure that when acting is disabled the user is told why **and any question in the same request is still answered** (FR-018)
- [X] T040 [US4] Confirm the acting toggle is separate from the screen and microphone toggles in `src/Winly.App/Panel/CompanionPanelWindow.xaml` and persists via `src/Winly.Platform/Settings/JsonFileSettingsStore.cs` (FR-017)
- [X] T041 [US4] Add quickstart scenario 12 to `tests/manual-verification.md`

**Checkpoint**: US3 and US4 together are milestone M2 — chaining is safe and Principle VIII compliant except for the record.

---

## Phase 7: User Story 5 - Review what Winly did (Priority: P3)

**Goal**: The user can look back at the actions Winly carried out, independently of what it said at the time.

**Independent Test**: Run several requests that act, then open the action record and confirm each appears with its outcome.

### Tests for User Story 5

- [X] T042 [P] [US5] Create `tests/Winly.Core.Tests/ActionRecordTests.cs` covering: every attempt recorded including `Declined` and `Blocked` (FR-022, FR-023), the 500-entry cap discarding oldest first (FR-027), clearing emptying the record (FR-024), a `Type` entry containing **the action but not the typed text** (FR-026, SC-010), and entries written before a simulated restart still readable afterward (FR-025)

### Implementation for User Story 5

- [X] T043 [P] [US5] Create `ActionRecordEntry` in `src/Winly.Core/Actions/ActionRecordEntry.cs` with `TimestampUtc`, `Verb`, `Target`, `Argument`, `Status`, `Reason` per [data-model.md](./data-model.md) — **metadata only**: no screen images, no audio, no transcript, no clipboard contents, and `Target` stored as the literal `(text)` for `Type` actions
- [X] T044 [P] [US5] Create `IActionRecord` in `src/Winly.Core/Actions/IActionRecord.cs` with append, read and clear, so the record stays behind a Core interface (Principle V)
- [X] T045 [US5] Implement `JsonLinesActionRecord` in `src/Winly.Platform/Settings/JsonLinesActionRecord.cs` per [research.md](./research.md) R4: append one JSON object per line to `%LOCALAPPDATA%\Winly\actions.jsonl`, **separate from the Serilog diagnostic log**; trim to the newest 500 lines on startup and after every 50 appends; clearing deletes the file
- [X] T046 [US5] Record every outcome from `src/Winly.Core/Actions/DesktopActionSequenceRunner.cs`, including `Declined`, `Blocked` and `Abandoned` — an action declined at confirmation must appear as declined rather than being absent (FR-023)
- [X] T047 [US5] Add a review-and-clear view for the record to `src/Winly.App/Panel/CompanionPanelWindow.xaml` and `src/Winly.App/Panel/CompanionPanelWindow.xaml.cs` showing what each action was, what it targeted, whether it succeeded, and when (FR-024)
- [X] T048 [US5] Wire `JsonLinesActionRecord` into composition in `src/Winly.App/App.xaml.cs` (hand-wired, no DI container)
- [X] T049 [US5] Add quickstart scenarios 13–17 to `tests/manual-verification.md`

**Checkpoint**: Milestone M3 — chaining is accountable. Principle VIII fully satisfied.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T050 Verify prompt caching survived the system-prompt edit: deploy with `cd worker && npx wrangler deploy`, run `npx wrangler tail`, send two requests back to back and confirm `chat.usage` shows a **non-zero `cacheRead`** on the second — caching is silently ignored below 1024 tokens, with no error anywhere
- [X] T051 Confirm the deployed Worker *version* is actually the live *deployment*: compare `npx wrangler versions list` against `npx wrangler deployments list` and promote if they differ, allowing ~15–20 s for edge propagation
- [X] T052 Measure single-action latency against `001`'s SC-001 (≤4 s median key-release to first spoken word) and record it, confirming no regression (SC-002)
- [ ] T053 Run the full [quickstart.md](./quickstart.md) validation pass and fill in every manual-verification row with the Windows version and monitor configuration used
- [X] T054 [P] Update `CLAUDE.md`: current status, the action-sequence behavior, per-action bounds, spoken confirmation, and the action record — the spec-drift note about actions existing only in code no longer applies
- [X] T055 Remove the deprecated `action` field from the terminal event in `worker/src/index.ts` and the legacy fallback in `src/Winly.Providers/Chat/ProxyChatProvider.cs` ([contracts/worker-api.md](./contracts/worker-api.md) §1). Done 2026-09-13: the gate held trivially, since the only client ships from this tree and deploys with the Worker. **Not yet deployed** — the live Worker still emits the field until the next `wrangler deploy`
- [X] T056 Define the 20-request benchmark in `tests/action-benchmark.md`: 20 spoken requests of two or three steps each, spanning launch-then-act, window arrangement, system settings and media, including at least three consequential actions and one request that exceeds 5 actions. This is the set SC-001, SC-003, SC-004, SC-005 and SC-008 are all measured against, and it does not exist yet
- [ ] T057 Run the T056 benchmark and record results against each criterion: ≥90% complete every step from one activation (SC-001), zero actions run after an earlier failure (SC-003), 100% of consequential actions confirmed (SC-004), zero typed text in an unverified window (SC-005), and no sequence past 5 actions or an action past its own bound without stopping (SC-008)
- [ ] T058 Run 20 consecutive acting requests and confirm none leaves the application in a state requiring restart (SC-009), consistent with `001`'s SC-009
- [X] T059 Confirm FR-020 held: grep the diff for any new mouse-input P/Invoke (`SendInput` with `MOUSEEVENTF*`, `SetCursorPos`, `mouse_event`) and confirm every action still uses rung 1 or 3 of the constitution's actuation ladder. No coordinate-based mouse input may have been introduced

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)**: depends on Foundational
- **US2 (Phase 4)**: depends on US1 — extends the same `DesktopActionSequenceRunner`
- **US3 (Phase 5)**: depends on US1; independent of US2
- **US4 (Phase 6)**: depends on US1; independent of US2 and US3
- **US5 (Phase 7)**: depends on US1; records outcomes from US2/US3/US4 when those exist
- **Polish (Phase 8)**: depends on all desired stories; T055 additionally gated on client rollout

### Note on story independence

US2–US5 all extend `DesktopActionSequenceRunner.cs`, which US1 creates. They are
independently *testable* and independently *demonstrable*, but not independently
*startable* — the runner has to exist first. This is inherent to the feature: the defect
being fixed is that no sequence exists at all.

### Within each user story

- Tests before implementation; they must fail first
- Core types before the runner; runner before orchestrator wiring
- Platform implementation after the Core interface it satisfies
- Manual-verification rows last, once the behavior is real

### Parallel Opportunities

- **Phase 2**: T002, T007, T010 are parallel. T003, T004 and T005 all edit `worker/src/index.ts` — strictly sequential.
- **Phase 3**: T011 and T012 parallel (tests); T013 blocks T015.
- **Phase 5**: T026, T027, T028 parallel; T029 and T030 parallel (different files); T034 and T035 both edit `UserInputControl.cs` — sequential.
- **Phase 7**: T042, T043, T044 parallel.
- Different stories can run in parallel across developers once Phase 3 lands.

---

## Parallel Example: User Story 3

```bash
# Tests first, all three in parallel (different files):
Task: "ConsequentialActionClassifierTests.cs — exhaustive over DesktopActionKind"
Task: "SpokenConfirmationTests.cs — agree, refuse, silence, unrecognized"
Task: "DesktopActionSequenceRunnerTests.cs — gate, decline, per-action agreement"

# Then the two new Core types in parallel (different files):
Task: "ConsequentialActionClassifier.cs"
Task: "SpokenConfirmation.cs"
```

---

## Implementation Strategy

### MVP (US1 only)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1
2. **STOP and VALIDATE**: "open Spotify and play some jazz" does both
3. This is the reported defect, fixed, and is demonstrable on its own

### Incremental Delivery

1. Foundational → list reaches the client
2. **+ US1 → M1 demo**: chaining works *(MVP)*
3. **+ US2 → M1 complete**: chaining fails safely
4. **+ US3 + US4 → M2**: chaining is safe and permission-gated
5. **+ US5 → M3**: chaining is accountable; Principle VIII fully satisfied

### Constitution note

Shipping M1 before M2 leaves confirmation and recording briefly non-compliant with
Principle VIII. That is declared in plan.md's Complexity Tracking, and the position is
inherited rather than worsened — the pre-existing action code is already non-compliant.
**M2 must not be deferred beyond the next milestone.**

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks
- Per-action time bounds (60 s readiness, 10 s otherwise) are tuning values, not design
  invariants (FR-005b) — changing them must not require changing how sequences run
- Commit after each task or logical group; branch is `002-desktop-actions`
- An action misbehaving during verification: read `%LOCALAPPDATA%\Winly\logs\` first. The
  answer is almost never "the build is stale"
