# Feature Specification: Desktop Actions

**Feature Branch**: `002-desktop-actions`

**Created**: 2026-09-13

**Status**: Draft

**Input**: Winly can already carry out a single spoken command on the desktop, but a
request that needs more than one thing to happen stops after the first. Let one request
carry out the whole task, and bring the existing action capability under specification.

## Overview

Winly can already act on the desktop, not merely answer questions about it: it opens
applications, plays and queues music, controls media transport and volume, arranges and
closes windows, changes system settings, types text, reads the clipboard, opens files
and folders, and sets timers.

None of that was ever specified. It was built while feature `001` was in flight, and
`001` governs only asking, answering, and pointing. This specification does two things:
it brings the existing capability under governance, and it fixes the defect that makes
it feel broken — **a request is currently limited to one action, so any task needing
two or more completes only its first step.**

"Open Spotify and play some jazz" is two actions. Today one of them happens.

**Explicitly out of scope**: observing the screen between actions to decide what to do
next, and acting on arbitrary on-screen elements by clicking them. Every action in this
feature is a named command carried out through a native interface, not a simulated click.
That larger capability is a separate future feature and is deliberately not started here.

## Clarifications

### Session 2026-09-13

- Q: How many actions may one request carry? → A: Up to 5, after which the sequence
  stops and reports what it completed.
- Q: What happens when an action in the middle of a sequence fails? → A: The sequence
  stops there; later actions are abandoned, and the user is told what completed and
  what did not.
- Q: Does an action that closes an application or locks the machine need confirmation?
  → A: Yes — those, plus typing text and anything else irreversible, are confirmed
  individually before running.
- Q: How does the user agree to a consequential action? → A: Winly states it aloud and
  briefly reopens the microphone to listen for a spoken yes or no.
- Q: What happens when a spoken confirmation goes unanswered or is not understood?
  → A: After a short fixed window (~5 seconds) it is treated as a refusal and the
  sequence stops.
- Q: Does the action record survive a restart? → A: Yes — it is written to disk as
  action metadata only, capped at a bounded number of recent entries, and user-clearable.
- Q: What bounds a sequence in time? → A: Each action is bounded individually (60 s to
  wait for a launched application to become ready, 10 s for any other action); there is
  no aggregate wall-clock bound on the sequence.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One request completes the whole task (Priority: P1)

The user asks for something that takes more than one step — "open Spotify and put on
some jazz", "open Chrome and go to my email", "turn on dark mode and dim the screen" —
and all of it happens, in the order it needs to happen, from a single activation.

**Why this priority**: This is the defect the feature exists to fix. Everything else
here documents behavior that already ships; this is the part that does not work.

**Independent Test**: With Spotify installed and closed, hold the key and say "open
Spotify and play some jazz." Confirm Spotify opens *and* jazz begins playing, without
a second activation.

**Acceptance Scenarios**:

1. **Given** a request that requires two or more actions, **When** it is carried out,
   **Then** every action runs, not only the first.
2. **Given** a sequence of actions, **When** they run, **Then** they run in the order the
   request implies, so an application is opened before it is asked to do something.
3. **Given** an action that depends on an application that was just launched, **When**
   the dependent action runs, **Then** it waits for that application to be ready rather
   than firing into an application that has not finished starting.
4. **Given** a request that requires only one action, **When** it is carried out,
   **Then** it behaves exactly as it does today, with no added latency or confirmation.
5. **Given** a request that requires no action at all, **When** it is answered, **Then**
   no action runs and the answer is spoken as in feature `001`.
6. **Given** a request implying more actions than the per-request maximum, **When** it is
   carried out, **Then** the sequence stops at the maximum and the user is told the task
   was not finished.

---

### User Story 2 - A failure stops the sequence and says so (Priority: P1)

When one step cannot be carried out — the application is not installed, nothing is
playing, the window is not there — Winly stops rather than carrying on with steps that
depended on it, and tells the user where it got to.

**Why this priority**: Chaining without stopping is worse than not chaining. A sequence
that continues past a failed step performs later actions in a state nobody intended,
which is how a harmless request produces a surprising outcome.

**Independent Test**: Ask Winly to open an application that is not installed and then do
something with it. Confirm it stops after the failure, does not attempt the second
action, and says which part did not work.

**Acceptance Scenarios**:

1. **Given** an action in a sequence fails, **When** the failure occurs, **Then** no
   later action in that sequence runs.
2. **Given** a sequence stopped early, **When** Winly reports back, **Then** it states
   what completed and what did not, in plain language without error codes.
3. **Given** an action fails, **When** the failure is handled, **Then** the spoken answer
   already in progress is not interrupted or lost.
4. **Given** any failure, **When** it has been reported, **Then** the next activation
   works normally.

---

### User Story 3 - Consequential actions are confirmed first (Priority: P2)

Before Winly does something that cannot be casually undone — closing an application that
may hold unsaved work, locking or sleeping the machine, typing text into whatever holds
focus — it says aloud what it is about to do and listens for the user to answer.

**Why this priority**: The existing verb set already contains these, ungated. It is a
present risk rather than a future one, but the capability is usable today without it,
which places it below the two P1 stories.

**Independent Test**: Ask Winly to close an application with unsaved work. Confirm it
states the action aloud and listens, that saying no leaves the application open, and
that the same request in a later session asks again.

**Acceptance Scenarios**:

1. **Given** an action classified as consequential, **When** it is reached, **Then**
   Winly states it aloud in plain language, listens for an answer, and does not run it
   until the user has spoken agreement.
2. **Given** a pending confirmation, **When** the user speaks a refusal, **Then** that
   action does not run and the sequence stops there.
3. **Given** a pending confirmation, **When** the window passes with no recognizable
   agreement, **Then** the action does not run, the microphone closes, and the sequence
   stops as though the user had refused.
4. **Given** the user agrees, **When** the action runs, **Then** the agreement covers
   only that action and not any later one.
5. **Given** a consequential action was confirmed in an earlier session, **When** a
   similar one arises in a new session, **Then** it is confirmed again.
6. **Given** an action that is not consequential — adjusting volume, skipping a track,
   focusing a window — **When** it runs, **Then** it runs without confirmation.

---

### User Story 4 - Acting is a separate permission from seeing (Priority: P2)

The user can let Winly see their screen and hear them without letting it change
anything, and can turn acting off entirely at any time.

**Why this priority**: A setting for this already exists in the application; this
specification makes it a governed requirement rather than an implementation detail, and
covers the case of revoking it mid-task.

**Independent Test**: Disable actions in settings, restart, then ask Winly to open an
application. Confirm nothing opens and Winly says why.

**Acceptance Scenarios**:

1. **Given** actions are disabled, **When** a request would act, **Then** nothing is
   carried out and the user is told actions are turned off.
2. **Given** actions are disabled, **When** the same request also asks a question,
   **Then** the question is still answered.
3. **Given** any action setting is changed, **When** the application restarts, **Then**
   the setting is preserved.
4. **Given** a sequence is running, **When** acting is disabled mid-sequence, **Then**
   the sequence stops before its next action.

---

### User Story 5 - Review what Winly did (Priority: P3)

The user can look back at the actions Winly carried out, independently of what it said
at the time.

**Why this priority**: Valuable for trust and for diagnosing "why did that happen",
but the feature is usable without it.

**Independent Test**: Run several requests that act, then open the action record and
confirm each one appears with its outcome.

**Acceptance Scenarios**:

1. **Given** actions have run, **When** the user opens the record, **Then** each appears
   with what it was, what it targeted, whether it succeeded, and when.
2. **Given** an action was declined at confirmation, **When** the record is reviewed,
   **Then** it appears as declined rather than being absent.
3. **Given** the user clears the record, **When** it is reviewed afterward, **Then** it
   is empty.
4. **Given** actions have run, **When** the application is closed and reopened, **Then**
   the record still lists them.
5. **Given** text was typed, **When** the record is reviewed, **Then** the entry names
   the action and its target but does not contain the text that was typed.

---

### Edge Cases

- **An application is asked to act before it has finished launching.** The dependent
  action waits for readiness up to a bounded time; if readiness never arrives, the
  sequence stops and reports it rather than firing into nothing.
- **The named application is not installed, or no window matches.** The action fails
  cleanly and stops the sequence.
- **A media action is requested with no player running and nothing playing.** Treated as
  a clean failure, not an exception.
- **Text is to be typed but no window holds focus, or focus changes mid-sequence.**
  Typing into an unintended target is the worst outcome in this feature; the action must
  verify its target still holds focus immediately before typing, and abandon if not.
- **A spoken confirmation is never answered, or the room is too noisy to recognize one.**
  The confirmation window closes on its own and the action is refused rather than left
  pending; the microphone does not stay open waiting.
- **The target window is elevated.** The action cannot be carried out; this is reported
  as such rather than retried or escalated.
- **The user activates again while a sequence is running.** The running sequence is
  abandoned at its next boundary and the new request takes precedence, consistent with
  `001`'s FR-015.
- **The same request implies two actions that conflict** (mute and set volume to 40).
  They run in declared order; the last one wins. No attempt is made to reconcile them.
- **A sequence would exceed the per-request maximum of 5 actions.** It stops at the
  maximum and reports the task unfinished.
- **An action exceeds its own time bound** — a launched application never becomes ready
  because it is sitting on a modal update prompt, for instance. The action is treated as
  a failure at its bound, the sequence stops, and the user is told where it got to. Winly
  never waits indefinitely on an action that will never complete.
- **The model returns a malformed or unrecognized action.** It is discarded rather than
  guessed at; if nothing valid remains, the request is treated as answer-only.

## Requirements *(mandatory)*

### Functional Requirements

**Carrying out more than one action**

- **FR-001**: One request MUST be able to carry out more than one action.
- **FR-002**: Actions MUST run sequentially in the order the model declared them, never
  concurrently.
- **FR-003**: A request MUST NOT carry out more than 5 actions; a sequence reaching that
  limit MUST stop and report the task as unfinished.
- **FR-004**: Every action MUST be individually bounded in wall-clock duration. An action
  exceeding its bound MUST be treated as a failure, stopping the sequence per FR-007 and
  reporting what completed. There is no aggregate bound on the sequence as a whole, so an
  action that legitimately takes time is never cut off because earlier actions were slow.
  This satisfies Constitution Principle VIII as amended in v1.2.0: the sequence is bounded
  in count, each action in duration.
- **FR-005**: When an action depends on an application that an earlier action in the same
  sequence launched, the dependent action MUST wait for that application to become ready
  before running, for no longer than 60 seconds.
- **FR-005a**: Any action other than waiting for a launched application to become ready
  MUST complete within 10 seconds.
- **FR-005b**: The bounds in FR-005 and FR-005a are tuning values, not design invariants;
  changing them MUST NOT require changing how sequences are carried out.
- **FR-006**: A request requiring exactly one action MUST behave as it does today, with
  no additional latency.

**Failure**

- **FR-007**: When an action fails, no later action in that sequence MUST run.
- **FR-008**: After a sequence stops early, the user MUST be told which actions completed
  and which did not, in plain language containing no error codes or exception names.
- **FR-009**: A failing action MUST NOT interrupt or discard the spoken answer already in
  progress.
- **FR-010**: A malformed or unrecognized action MUST be discarded rather than
  approximated; a request left with no valid actions MUST be treated as answer-only.

**Confirmation**

- **FR-011**: Actions that close an application, lock or sleep the machine, type text
  into a focused window, or are otherwise irreversible or destructive MUST be classified
  as consequential.
- **FR-012**: A consequential action MUST be stated aloud in plain language, after which
  Winly MUST briefly reopen the microphone and listen for a spoken agreement or refusal.
  The action MUST NOT run until the user has spoken agreement.
- **FR-012a**: The microphone reopened for a confirmation MUST be subject to the same
  consent and indicator rules as any other capture, and MUST close as soon as the
  confirmation is resolved.
- **FR-012b**: A confirmation MUST resolve within a fixed window of approximately 5
  seconds. Silence, or speech containing no recognizable agreement, MUST be treated as a
  refusal; the microphone MUST close and the sequence MUST stop, and the user MUST be
  told the action was not carried out.
- **FR-013**: Agreement MUST apply to exactly one action and MUST NOT be remembered
  across actions or across sessions.
- **FR-014**: Declining MUST stop the sequence at that point.
- **FR-015**: Actions that are not consequential MUST run without confirmation.
- **FR-016**: Before text is typed, the intended target MUST be verified as still holding
  focus; if it does not, the action MUST be abandoned rather than typed elsewhere.

**Permission and scope**

- **FR-017**: Acting MUST be controlled by a setting separate from screen and microphone
  capture, and that setting MUST persist across restarts.
- **FR-018**: When acting is disabled, no action MUST be carried out, the user MUST be
  told why, and any question in the same request MUST still be answered.
- **FR-019**: Disabling acting while a sequence is running MUST stop it before its next
  action.
- **FR-020**: Every action MUST be carried out through the highest available rung of the
  constitution's actuation capability ladder; this feature MUST NOT introduce
  coordinate-based mouse input.
- **FR-021**: Winly MUST NOT elevate in order to carry out an action; an action refused
  by the operating system MUST be reported as refused.

**Record**

- **FR-022**: Every action attempt MUST be recorded with what it was, what it targeted,
  its outcome, and when it occurred.
- **FR-023**: An action declined at confirmation, or refused by an unanswered
  confirmation, MUST be recorded as declined.
- **FR-024**: The user MUST be able to review and to clear this record.
- **FR-025**: The record MUST be written to durable local storage and MUST survive an
  application restart.
- **FR-026**: The record MUST contain action metadata only — the verb, the target, the
  outcome, and the time. It MUST NOT contain captured screen images, captured audio,
  transcribed speech, clipboard contents, or the literal text of a typed action.
- **FR-027**: The record MUST be capped at a bounded number of most recent entries, with
  the oldest discarded once the cap is reached, so it cannot grow without limit.

### Key Entities

- **Action**: One thing Winly was asked to carry out — a verb, what it acts on, and any
  amount or qualifier that verb needs. Carries whether it is consequential.
- **Action Sequence**: The ordered set of actions belonging to one request, together with
  its count bound and the point it reached. Time bounds belong to the individual actions,
  not to the sequence.
- **Action Outcome**: What became of one action — completed, failed with a user-facing
  reason, declined, or abandoned because an earlier action stopped the sequence.
- **Action Record Entry**: One durable line describing an attempted action and its
  outcome, reviewable independently of the spoken answer. Holds metadata only — verb,
  target, outcome, timestamp — and is kept on disk under a bounded entry count.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across a benchmark of 20 two-or-three-step spoken requests, at least 90%
  complete every step from a single activation.
- **SC-002**: A request needing one action completes no slower than it does today,
  measured as no regression against `001`'s SC-001 latency budget.
- **SC-003**: Zero benchmark requests result in an action running after an earlier action
  in the same sequence failed.
- **SC-004**: 100% of consequential actions in the benchmark are confirmed before
  running; zero run unconfirmed.
- **SC-005**: Zero benchmark requests produce typed text in a window other than the one
  verified to hold focus immediately before typing.
- **SC-006**: Every sequence that stops early produces a spoken or displayed report
  naming what completed and what did not.
- **SC-007**: 100% of attempted actions, including declined ones, appear in the action
  record with the correct outcome, and are still present after an application restart.
- **SC-010**: Zero action record entries contain screen images, audio, transcribed
  speech, clipboard contents, or typed text.
- **SC-008**: No benchmark sequence exceeds 5 actions without stopping, and no individual
  action runs past its own time bound without being treated as a failure.
- **SC-009**: 20 consecutive acting requests complete without leaving the application in
  a state requiring restart, consistent with `001`'s SC-009.

## Assumptions

- Feature `001` is complete and shipped; its activation, capture, answering, and failure
  reporting behavior is reused unchanged and not restated here. Its rule that captured
  screen and audio data is never written to disk is unaffected: the action record added
  here stores neither.
- The existing eleven verbs — open, play, queue, media, volume, window, system, type,
  clipboard, open-path, timer — are the vocabulary for this feature. Adding verbs is
  valuable but is a separate change and is not required to fix the defect this feature
  addresses.
- Native and service integrations are the preferred mechanism, not a compromise. The
  existing music integration is more reliable than simulating clicks would be, and the
  constitution's capability ladder makes that preference binding rather than incidental.
- No sequence is expected to approach the per-action bounds in normal use; they exist to
  detect an action that has hung, not to pace ordinary work.
- "Ready" for a launched application means its main window exists and accepts input;
  precisely how that is determined is an implementation concern for the plan.
- Deciding actions by looking at the screen between steps is out of scope, as is any
  form of simulated mouse input. A request that cannot be expressed in the existing
  verbs is answered rather than attempted.
- Which specific actions count as consequential is a judgment encoded in a ruleset that
  errs toward confirming; tuning it is expected to continue past this feature.
