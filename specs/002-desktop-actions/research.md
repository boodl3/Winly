# Phase 0 Research: Desktop Actions

**Feature**: `002-desktop-actions` | **Date**: 2026-09-13 | **Plan**: [plan.md](./plan.md)

Five unknowns. Three were named in the plan; two arrived with the clarification session
(record storage, and the rollout window created by changing the Worker's terminal event).

---

## R1 — When is a just-launched application ready to be acted on?

**Decision**: Poll for a *matched* main window, not for the process that was launched.
Every 250 ms, run the existing `AppMatcher` against the live process list; ready means a
matching process owns a visible main window that answers a `SendMessageTimeout(WM_NULL)`
probe within 100 ms. Give up at 60 s (FR-005) and fail the action.

For `Play` and `Queue` against Spotify, readiness is additionally that `POST /music/state`
reports an active device — a Spotify window can exist for several seconds before the
account has a device to play on, and issuing playback before then returns a 404 that
reads to the user as "it didn't work".

**Rationale**: The obvious approach — hold the `Process` returned by `Process.Start` and
call `WaitForInputIdle` — fails on exactly the applications this feature exists to chain.
Spotify, Discord, Teams and every Electron application launch a stub that spawns the real
process and exits; the handle we hold is dead within a second and never owned a window.
`WaitForInputIdle` also throws for a process with no message pump, and returns immediately
for some that have one. Matching on what is actually running is the same technique
`AppMatcher` already uses for window verbs, and it is indifferent to how many processes
the launch fanned out into.

The `WM_NULL` probe is what separates "a window exists" from "a window is pumping
messages": a splash screen satisfies the first and not the second.

**Alternatives considered**:

- `Process.WaitForInputIdle` on the launched handle — rejected above; wrong process.
- Poll for `MainWindowHandle != 0` only — accepts a splash screen as ready, which is the
  precise failure the readiness wait exists to prevent.
- A fixed sleep — unreliable on a cold disk and wasteful on a warm one; it is the shape of
  fix that works on the developer's machine and not the user's.

---

## R2 — How is a spoken confirmation carried out?

**Decision**: Speak the prompt through the existing TTS path, **wait for its playback to
finish**, then `IMicrophoneCapture.StartCapture` and `ISpeechToTextProvider.Transcribe`
with a 5 s bound (FR-012b). Classify the transcript against a small agreement list
(`yes`, `yeah`, `yep`, `sure`, `okay`, `ok`, `go ahead`, `do it`, `confirm`, `please do`)
and a refusal list (`no`, `nope`, `dont`, `cancel`, `stop`, `never mind`). Anything else,
including an empty transcript, is a refusal. The capture indicator from `001` is shown for
the duration, and `StopCapture` runs in a `finally` (FR-012a).

**Rationale**: Waiting for playback to finish is not politeness — Winly's own voice is
playing through the user's speakers, and starting capture before it ends transcribes
Winly asking the question. The likeliest self-transcription is the prompt's own final
words, which is how "should I close Word?" becomes a confirmation of itself.

Treating an unrecognized transcript as refusal rather than re-asking follows from the
clarification, and is the correct default for the one gate standing in front of
irreversible actions: a false refusal costs the user one repeated sentence, a false
agreement costs them unsaved work.

**Alternatives considered**:

- Overlay prompt with confirm/cancel buttons — unambiguous and cheaper, but the user is
  mid-interaction with their hands on the keyboard and their attention on their own work;
  rejected in clarification.
- Sending the transcript to the model to judge agreement — a second round trip inside the
  latency budget, for a decision a twenty-word list makes correctly.
- Re-asking once on silence — doubles the time the microphone is live for no safety gain.

---

## R3 — How is the typing target verified?

**Decision**: Record the foreground window handle at activation start (key-down), before
Winly speaks or acts. Immediately before `SendInput`, call `GetForegroundWindow` and
compare. A different handle, or a window that has since been destroyed, means abandon and
report (FR-016). Additionally, resolve the foreground window's owning process and abandon
if it cannot be opened for query — that is the signature of an elevated target, which
UIPI will silently drop input into (FR-021).

**Rationale**: Activation start is the only moment that corresponds to what the user meant
by "here". Checking against the foreground window at *execution* time compares it to
itself and verifies nothing.

The residual race — focus changing between the check and `SendInput` — cannot be closed
without blocking input, which is a worse intervention than the risk it removes. It is
bounded to microseconds and is accepted explicitly rather than silently.

The elevated-target case matters because it fails *silently*: `SendInput` returns success
having delivered nothing, so without the process probe a user typing into an elevated
console would be told it worked.

**Alternatives considered**:

- `AttachThreadInput` + `SetForegroundWindow` to force focus back — makes Winly steal
  focus from whatever the user moved to, converting a safe abandonment into a surprise.
- Block input during typing (`BlockInput`) — requires elevation, which Principle VIII
  forbids.

---

## R4 — Where does the action record live?

**Decision**: JSON-lines at `%LOCALAPPDATA%\Winly\actions.jsonl`, one object per attempt,
next to `settings.json` and separate from the Serilog diagnostic log. Capped at 500
entries: on startup, and after every 50 appends, the file is rewritten keeping the newest
500 lines. Clearing (FR-024) deletes the file.

**Rationale**: JSON-lines makes an append an append — no read, no parse, no rewrite on the
hot path, and a truncated final line from a hard power-off costs one entry rather than the
file. 500 entries is roughly a month of heavy use and stays well under a megabyte, so the
trim is rare and cheap when it happens.

Keeping it out of the Serilog log is a requirement, not tidiness: the user is meant to
review this (FR-024), and a diagnostic log is not reviewable by a person who did not write
the application.

**Alternatives considered**:

- SQLite — a new dependency and a Principle III license review for a capped append-only
  list that is read once in a settings panel.
- A second Serilog sink — couples a user-facing record to diagnostic formatting and
  retention, and makes "clear the record" mean "clear some of the logs".
- In-memory only — cannot answer "why did that happen yesterday", which is the record's
  entire purpose; rejected in clarification.

---

## R5 — How does the Worker's contract change without breaking installed clients?

**Decision**: For one deprecation window the Worker's terminal `done` event carries
**both** `actions` (the ordered array, authoritative) and `action` (the first element, or
`null`) — and the client reads `actions` when present and otherwise wraps `action` into a
one-element list.

**Rationale**: The Worker and the client ship independently, and the Worker ships to
everyone at once. A Worker that emits only `actions` immediately breaks every installed
client that has not updated — not degrading them to single actions, but to none at all,
because `ChatAnswer.Action` would be permanently null. Emitting both costs a few bytes per
response and makes the order of the two deployments irrelevant.

The client-side tolerance covers the reverse case: an updated client pointed at a Worker
version that has not been promoted yet, which this project has hit before.

**Alternatives considered**:

- Version the route (`/chat/v2`) — a whole second route and its tests for one field whose
  old and new shapes can coexist in the same object.
- Cut over both at once — assumes every client updates simultaneously, which is not true
  of a desktop binary.

**Removal condition**: drop `action` from the Worker once no client older than this
feature is in use. Tracked as a task, not left to memory.
