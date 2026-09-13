# Quickstart: Validating Desktop Actions

**Feature**: `002-desktop-actions` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

How to prove this feature works end to end. Scenarios are grouped by the milestone that
delivers them, so each group is runnable as soon as its milestone lands rather than only
at the end.

---

## Prerequisites

- Windows 10 build 19041+ (per-monitor capture floor), x64
- The .NET 10 SDK, which on this machine is a user-local install **not on PATH**
- Spotify installed, signed in, Premium (playback control is Premium-only)
- `SPOTIFY_CLIENT_ID` and `SPOTIFY_CLIENT_SECRET` set as Worker secrets **and promoted to
  the live deployment**. Without them every music scenario degrades to a `spotify:search:`
  link rather than failing, so scenario 1 will look half-broken rather than blocked
- The Worker deployed with this feature's contract delta
- Microphone and speakers working — confirmation is spoken and answered aloud

```bash
export DOTNET_ROOT="$LOCALAPPDATA/Microsoft/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

---

## Build and unit tests

Everything that can be tested headlessly is in `Winly.Core`.

```bash
dotnet test Winly.sln
```

Expected: zero warnings, all tests green — the existing suite plus new coverage for
multi-action parsing, sequence ordering, stop-on-failure, per-action bounds, consequential
classification, and confirmation handling. A failure in
`ConsequentialActionClassifierTests` that names an unhandled verb means a verb was added
without being classified; that test is deliberately exhaustive over `DesktopActionKind`.

## Worker check

Actions ride the `/chat` terminal event, so verify the wire shape directly rather than
through the client — the client collapses every backend failure into one generic spoken
message by design (`001` FR-031), so it cannot tell you which end broke.

```bash
curl -N -X POST https://winly-backend.boodle-doobee.workers.dev/chat -H "content-type: application/json" -d '{"transcript":"open spotify and play some jazz","installId":"00000000-0000-0000-0000-000000000000"}'
```

Expected in the terminal `done` event: an `actions` array with **two** entries in order
(`open` then `play`), and a deprecated `action` field equal to the first. See
[contracts/worker-api.md](./contracts/worker-api.md).

After deploying, confirm prompt caching survived the system-prompt edit:

```bash
cd worker && npx wrangler tail
```

Send two requests back to back; `chat.usage` on the second must show a non-zero
`cacheRead`. Zero means the prompt fell under the 1024-token threshold and is silently
no longer cached.

---

## M1 — Chaining works

| # | Do this | Expect |
|---|---|---|
| 1 | With Spotify closed: *"open Spotify and play some jazz"* | Spotify opens **and** jazz plays, one activation (US1, FR-001) |
| 2 | *"open Chrome and put it on the left half"* | Chrome opens, then snaps left — in that order (FR-002) |
| 3 | *"turn it down a bit"* | Single action, behaves as before, no added latency (FR-006, SC-002) |
| 4 | *"what does this button do?"* | Answer only, no action runs (US1 scenario 5) |
| 5 | A request implying six or more actions | Stops after 5, says the task is unfinished (FR-003) |

Scenario 1 is the reported defect. Before this milestone it opens Spotify and stops.

## M2 — Chaining is safe

| # | Do this | Expect |
|---|---|---|
| 6 | Open an app that is not installed, then act on it | Stops at the failure, second action never runs, plain-language report (US2, FR-007/8) |
| 7 | *"close Word"* with unsaved work | Winly states it aloud and listens; saying **no** leaves Word open (US3, FR-012) |
| 8 | Same, then say nothing | After ~5 s it is treated as a refusal and the sequence stops (FR-012b) |
| 9 | *"turn the volume down"* | Runs with no confirmation (FR-015) |
| 10 | Start a typing request, then click into another window before it types | Typing is abandoned, not sent elsewhere (FR-016, SC-005) |
| 11 | Launch an app that hangs on a modal (e.g. a pending Spotify update) and chain onto it | Fails at 60 s, sequence stops, user is told — never waits forever (FR-005) |
| 12 | Uncheck desktop actions in settings, restart, ask Winly to open something | Nothing opens, Winly says actions are off, any question is still answered (FR-017/18) |

Scenario 8 is the one most easily broken by a regression in the wrong direction: verify
Winly is not transcribing its *own* prompt as the answer (see [research.md R2](./research.md)).

## M3 — Chaining is accountable

| # | Do this | Expect |
|---|---|---|
| 13 | Run several acting requests, open the action record | Every attempt listed with verb, target, outcome, time (FR-022, SC-007) |
| 14 | Decline a confirmation, check the record | Appears as **declined**, not absent (FR-023) |
| 15 | Type something, check the record | Entry shows the action but **not** the typed text (FR-026, SC-010) |
| 16 | Close and reopen Winly, check the record | Still there (FR-025) |
| 17 | Clear the record, review it | Empty (FR-024) |

---

## Diagnosing a failure

`%LOCALAPPDATA%\Winly\logs\` first. Every action logs what it did or why it could not.
The answer is almost never "the build is stale" — read the log before assuming.

The action record at `%LOCALAPPDATA%\Winly\actions.jsonl` is user-facing and holds metadata
only; it tells you *what was attempted and what became of it*, while the Serilog log tells
you *why*. Use both.

---

## Manual verification rows

Scenarios 1–17 above need rows in `tests/manual-verification.md` recording the Windows
version and monitor configuration they were verified on, per Constitution Development
Workflow. Platform-layer work — readiness-waiting, focus verification, the record file —
cannot be covered in CI, so these rows are the only evidence it works.
