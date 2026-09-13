# Implementation Plan: Desktop Actions

**Branch**: `002-desktop-actions` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification at `specs/002-desktop-actions/spec.md`

## Summary

Lift the one-action-per-request limit, and bring the existing desktop-action capability
under governance by adding the confirmation, bounding, and recording that Constitution
Principle VIII now requires.

The limit is currently enforced in four separate places, which is why it looks like a
model problem rather than a code problem. From the model outward:

| # | Where | What enforces it |
|---|---|---|
| 1 | `worker/src/index.ts`, system prompt | The instruction "Only ever write one." tells the model to emit at most one `@@DO …@@` designation |
| 2 | `worker/src/index.ts`, stream splitter | `/@@DO\s*(\{[\s\S]*?\})\s*@@/.exec(...)` returns the first match only |
| 3 | `worker/src/index.ts`, SSE completion | The terminal event carries a singular `action` field |
| 4 | `src/Winly.Core/Providers/IChatProvider.cs` → `CompanionOrchestrator.cs` | `ChatAnswer.Action` is a single nullable value; the orchestrator awaits `RunDesktopAction` once, then finishes |

All four must change together; fixing any subset changes nothing observable.

Approach: carry an ordered list end to end, execute it in a bounded loop that stops at
the first failure, wait for readiness between a launch and anything depending on it, gate
consequential verbs behind confirmation, and append every attempt to a record.

## Technical Context

**Language/Version**: C# 14 / .NET 10, unchanged. Worker is TypeScript on Cloudflare.

**Primary Dependencies**: No new dependencies. This is a change to existing code in
`Winly.Core`, `Winly.Platform`, `Winly.Providers`, and the Worker.

**Storage**: The action record is appended to `%LOCALAPPDATA%\Winlyctions.jsonl`,
separate from the Serilog application log so the user can review it without reading
diagnostics. Capped at 500 entries, oldest discarded (FR-027). Capture data remains
memory-only per `001`'s FR-029; action records contain metadata only — no captured
content, and not the text of a `type` action (FR-026).

**Testing**: xUnit. The multi-action parser, sequence ordering, stop-on-failure, bound
enforcement, and consequential classification are all in `Winly.Core` and are covered in
CI with fakes. Readiness-waiting and focus verification live in `Winly.Platform` and are
covered by the manual verification matrix.

**Target Platform**: Unchanged — Windows 10 1903+, x64.

**Project Type**: Existing four-project desktop client plus Worker.

**Performance Goals**: A single-action request must not regress against `001`'s SC-001
(≤4 s median key-release to first spoken word). Multi-action sequences add execution
time but no additional model round trips — the whole sequence comes back in the answer
that is already being streamed.

**Constraints**:
- No coordinate-based mouse input is introduced (FR-020, Principle VIII ladder)
- No additional screenshots and no additional model calls per request — the cost profile
  of a chained request equals that of a single-action request today
- Typing must verify focus immediately before it runs (FR-016)
- Maximum 5 actions per request (FR-003)
- Each action is bounded individually — 60 s for a readiness wait, 10 s otherwise
  (FR-004, FR-005, FR-005a). There is no aggregate bound on the sequence
- Confirmation is spoken and answered aloud, resolving within ~5 s, with silence or an
  unrecognized answer counting as refusal (FR-012, FR-012b)

**Scale/Scope**: ~19 files in the client across all four projects (7 new, 12 modified),
1 in the Worker, plus tests. No new projects.

## Constitution Check

*Evaluated against Constitution v1.2.0.*

| Principle | Gate | Status |
|---|---|---|
| I. Secrets never ship in client | No credential touched; the Worker keeps holding them | PASS |
| II. Consent before capture | No change to what is captured or when | PASS — unaffected |
| III. Clean-room and license hygiene | No new dependency, no new asset | PASS |
| IV. Vertical slices | Three slices below, each independently demonstrable | PASS |
| V. Interop quarantined | Sequencing, bounds, classification and parsing land in `Winly.Core`; readiness-waiting and focus verification are the only platform additions | PASS |
| VI. Clarity over cleverness | Names state the whole thing (`DesktopActionSequenceRunner`, `ConsequentialActionClassifier`) | PASS — review criterion |
| VII. Fail loud, soft | Stop-on-failure with a plain-language report is the feature's own FR-007/FR-008 | PASS |
| VIII. Actions opt-in, ordered, bounded | Opt-in exists (`Settings.DesktopActionsEnabled`); ordering, bounds, confirmation and the record are this feature's requirements | PASS on delivery — per-action time bounds satisfy Principle VIII as amended in v1.2.0 |

**Note on retroactive compliance**: the action code predates Principle VIII. This plan is
the remediation; until it lands, the project has a known, documented violation in
confirmation and recording. That is declared rather than hidden, per Governance.

## Project Structure

### Documentation

```
specs/002-desktop-actions/
├── spec.md              # Complete, clarified 2026-09-13
├── plan.md              # This file
├── research.md          # Complete — R1–R5
├── data-model.md        # Complete — entities and the ChatAnswer change
├── quickstart.md        # Complete — 17 validation scenarios
├── contracts/
│   └── worker-api.md    # Delta to the /chat terminal event only
└── tasks.md             # Generate with /speckit.tasks — do not hand-author
```

### Source changes

```
worker/src/index.ts
  · system prompt          — replace "Only ever write one." with ordered-list guidance
  · stream splitter        — exec() → matchAll(), collect in document order
  · terminal SSE event     — action: DesktopAction | null  →  actions: DesktopAction[]

src/Winly.Core/
  Providers/IChatProvider.cs
    · ChatAnswer.Action    → ChatAnswer.Actions (IReadOnlyList<DesktopAction>, never null)
  Actions/
    DesktopActionParser.cs
      · Parse()            → returns IReadOnlyList<DesktopAction>; Regex.Matches, strips all tags
    ConsequentialActionClassifier.cs                                          [new]
      · Window/close, System/lock|sleep, Type → consequential; everything else not
      · exhaustive over DesktopActionKind; an unclassified verb fails a test
    DesktopActionSequenceRunner.cs                                            [new]
      · ordering, count bound, per-action time bounds, stop-on-failure, outcomes
    ActionOutcome.cs                                                          [new]
      · Completed | Failed | Declined | Abandoned | Blocked, + user-facing reason
    SpokenConfirmation.cs                                                     [new]
      · agreement/refusal word lists; anything unrecognized is a refusal
    IActionRecord.cs                                                          [new]
    ActionRecordEntry.cs                                                      [new]
  Companion/
    CompanionOrchestrator.cs
      · single RunDesktopAction call → sequence runner; report partial completion
    FailureMessages.cs
      · add partial-completion phrasing

src/Winly.Platform/
  Actions/
    WindowsDesktopActions.cs
      · WaitForApplicationReady() — AppMatcher poll + WM_NULL probe, 60 s cap (R1)
      · verify foreground window immediately before Type; abandon if it moved (R3)
      · abandon Type when the target process cannot be opened (elevated, FR-021)
  Settings/
    JsonLinesActionRecord.cs                                                  [new]
      · append to %LOCALAPPDATA%\Winly\actions.jsonl; trim to 500 (R4)

tests/Winly.Core.Tests/
  DesktopActionParserTests.cs            · multiple tags, malformed-among-valid, none
  DesktopActionSequenceRunnerTests.cs    · order, stop-on-failure, bounds, decline
  ConsequentialActionClassifierTests.cs  · every verb classified as expected
  SpokenConfirmationTests.cs             · agree, refuse, silence, unrecognized
  ActionRecordTests.cs                   · cap, clear, type-text redaction
```

**Structure decision**: `DesktopActionSequenceRunner` is a new type in `Winly.Core`
rather than a loop inside `CompanionOrchestrator`. The orchestrator is already 470 lines
carrying the whole activation lifecycle; adding ordering, two bounds, confirmation and
outcome collection inline would make the part of this change that most needs tests the
part hardest to test. The runner takes `IDesktopActions` and a confirmation callback, so
it is exercised headless.

## Phase 0 — Research

**Complete** → [research.md](./research.md). Five decisions, each with what was rejected:

| | Question | Decision |
|---|---|---|
| R1 | When is a launched application ready? | Poll `AppMatcher` against the live process list for a visible main window that answers a `WM_NULL` probe; 250 ms interval, 60 s cap. **Not** the launched `Process` handle — Spotify, Discord and every Electron app launch a stub that exits, so that handle never owned a window |
| R2 | How is a spoken confirmation carried out? | Speak the prompt, **wait for playback to end**, then capture and transcribe with a 5 s bound; match against a small agreement/refusal word list. Starting capture early transcribes Winly asking the question |
| R3 | How is the typing target verified? | Capture the foreground window at activation start and compare immediately before `SendInput`; also abandon when the target's process cannot be opened, which is how an elevated window presents |
| R4 | Where does the record live? | JSON-lines at `%LOCALAPPDATA%\Winlyctions.jsonl`, capped at 500 entries |
| R5 | How does the contract change safely? | Worker emits `actions` **and** deprecated `action` for one window; client tolerates either |

The two unknowns this plan originally raised about confirmation mechanics (#2) and typing
focus (#3) were settled in the clarification session and by R2/R3 respectively.

## Phase 1 — Design Outputs

**Complete.** Four artifacts:

- **[research.md](./research.md)** — the five decisions above with alternatives rejected.
- **[data-model.md](./data-model.md)** — Action (unchanged), the derived consequential
  classification table, Action Sequence, Action Outcome, Action Record Entry, and the
  `ChatAnswer.Action` → `ChatAnswer.Actions` change.
- **[contracts/worker-api.md](./contracts/worker-api.md)** — delta to `001`'s contract:
  the `/chat` terminal event gains `actions` and keeps `action` deprecated for one
  rollout window; extraction moves to `matchAll`; system-prompt change and its caching
  caveat.
- **[quickstart.md](./quickstart.md)** — 17 validation scenarios grouped by milestone,
  plus build, Worker `curl` and cache-verification steps.

## Phase 2 — Task Generation Approach

Three vertical slices, each independently demonstrable per Principle IV. Run
`/speckit.tasks` to expand; within each slice, order as contract → core with tests →
platform → orchestrator wiring → manual verification entry.

**M1 — Chaining works.** Worker prompt, worker parser, worker event, `ChatAnswer.Actions`,
parser returning a list, sequence runner with ordering, the count bound, per-action time
bounds, readiness-waiting and stop-on-failure; orchestrator wiring.
*Demo: "open Spotify and play some jazz" does both. This is the reported defect, fixed.*

**M2 — Chaining is safe.** Consequential classification, the spoken confirmation gate,
focus verification before typing, elevated-target refusal, and mid-sequence revocation.
*Demo: closing an unsaved document asks aloud first; saying no stops the sequence.*

**M3 — Chaining is accountable.** Action record, review and clear.
*Demo: run several requests, open the record, see every attempt and outcome.*

M1 alone fixes what the user reported. M2 is what makes the capability compliant with
Principle VIII, and should not be deferred beyond the next milestone. M3 completes it.

## Complexity Tracking

| Violation | Justification | Alternative rejected because |
|---|---|---|
| Ships M1 before M2, leaving confirmation and recording briefly non-compliant with Principle VIII | M1 is the reported defect and is independently demonstrable; the pre-existing code is already non-compliant, so M1 does not worsen the position it inherits | Holding M1 until M2 is ready delays the fix for a gap that already exists in shipped code, and violates Principle IV by bundling two slices into one undemonstrable lump |

## Progress Tracking

- [x] Constitution amended to v1.1.0 (Principle VIII, actuation capability ladder)
- [x] Feature specification complete
- [x] Implementation plan drafted
- [x] Constitution Check — pre-Phase-0: PASS on delivery, documented violation until then
- [x] Clarification session complete (7 questions, 2026-09-13)
- [x] Phase 0 research complete → `research.md`
- [x] Phase 1 design complete → `data-model.md`, `contracts/worker-api.md`, `quickstart.md`
- [x] Constitution amended to v1.2.0 (Principle VIII: per-action wall-clock bounds)
- [x] Constitution Check — post-design: PASS, no outstanding deviations
- [ ] `/speckit.tasks` run → `tasks.md`
- [ ] M1 chaining works
- [ ] M2 chaining is safe
- [ ] M3 chaining is accountable
