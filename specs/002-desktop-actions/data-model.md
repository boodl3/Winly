# Data Model: Desktop Actions

**Feature**: `002-desktop-actions` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

Four entities from the spec, plus the change to the existing `ChatAnswer`. `DesktopAction`
and `DesktopActionKind` already exist in `src/Winly.Core/Actions/DesktopAction.cs` and are
unchanged by this feature — the eleven verbs are the vocabulary, not something this
feature extends.

---

## Action

Already exists. One thing Winly was asked to carry out.

| Field | Type | Notes |
|---|---|---|
| `Kind` | `DesktopActionKind` | One of the eleven verbs |
| `Target` | `string` | Meaning is per-verb; documented on the enum members |
| `Argument` | `string` | Only `Window` uses it (focus/minimize/close/left/…) |
| `Amount` | `int` | Volume %, brightness %, or timer seconds |
| `Relative` | `bool` | `Amount` is a signed step rather than an absolute value |

**Unchanged.** Whether an action is consequential is *not* stored on it — it is derived by
`ConsequentialActionClassifier` from `Kind` and `Argument`, so the rule lives in one place
and cannot drift between a parsed action and an executed one.

**Validation** (existing, in `DesktopActionParser.TryParseTag`): an unknown verb, a
`Volume` or brightness `System` without an amount, a `Timer` outside 1…86 400 s, a
`Window` missing either half, or any other verb with an empty target all yield `null` and
the action is discarded (FR-010).

---

## Consequential classification

Derived, not stored. A pure function of the action.

| Verb / shape | Consequential | Why |
|---|---|---|
| `Window` + `close` | **Yes** | May discard unsaved work |
| `System` + `lock` | **Yes** | Ends the session |
| `System` + `sleep` | **Yes** | Ends the session |
| `Type` (any) | **Yes** | Sends text into whatever holds focus |
| `Window` + focus/minimize/maximize/restore/left/right | No | Reversible, no data at risk |
| `System` + darkmode/lightmode/brightness/mute/wifi/bluetooth/showdesktop | No | Reversible setting |
| `Open`, `OpenPath` | No | Additive |
| `Play`, `Queue`, `Media`, `Volume` | No | Reversible |
| `Clipboard` (read/copy) | No | Reads only; does not write the clipboard |
| `Timer` | No | Reversible, nothing external |

The rule errs toward confirming (spec Assumptions): a verb whose classification is
debatable is classified consequential until decided otherwise. Adding a verb without
classifying it MUST fail a test rather than default to "not consequential" — enforced by
a test that enumerates `DesktopActionKind` and asserts every member is covered.

---

## Action Sequence

New. The ordered set of actions belonging to one request.

| Field | Type | Notes |
|---|---|---|
| `Actions` | `IReadOnlyList<DesktopAction>` | In declared order; never null, may be empty |
| `Outcomes` | `IReadOnlyList<ActionOutcome>` | One per action attempted; shorter than `Actions` when stopped early |

**Invariants**:

- At most 5 actions (FR-003). A parsed list longer than 5 is truncated to the first 5 and
  the sequence reports the task unfinished.
- Executed strictly sequentially (FR-002); never in parallel.
- Stops at the first non-`Completed` outcome (FR-007).
- **No aggregate time bound.** Each action is bounded individually (FR-004): 60 s for a
  readiness wait (FR-005), 10 s for anything else (FR-005a). Time spent waiting for a
  spoken confirmation is outside both bounds — that clock belongs to the user.

**State**: a sequence is *running*, then terminally *completed* (every action `Completed`)
or *stopped* (an outcome that is not `Completed`). It is not resumable; the next
activation starts a new one.

---

## Action Outcome

New. What became of one action.

| Value | Meaning | Stops the sequence |
|---|---|---|
| `Completed` | Carried out | No |
| `Failed` | Could not be carried out; carries a user-facing reason | Yes |
| `Declined` | User refused, or the confirmation window passed unanswered (FR-012b) | Yes |
| `Abandoned` | An earlier action stopped the sequence, so this never ran | — |
| `Blocked` | Acting is disabled in settings (FR-018, FR-019) | Yes |

| Field | Type | Notes |
|---|---|---|
| `Action` | `DesktopAction` | What was attempted |
| `Status` | enum above | |
| `UserFacingReason` | `string` | Plain language, no error codes or exception names (FR-008). Empty when `Completed`. |
| `FollowUpSpeech` | `string?` | What the action wants said afterward, e.g. the clipboard's contents |

`Failed` and `Declined` are distinct because the record must tell them apart (FR-023) —
"I couldn't find Spotify" and "you said no" are different events with the same visible
result.

---

## Action Record Entry

New. One durable line; see [research.md R4](./research.md) for storage.

| Field | Type | Notes |
|---|---|---|
| `TimestampUtc` | `DateTimeOffset` | When the attempt occurred |
| `Verb` | `string` | The `DesktopActionKind` name |
| `Target` | `string` | **Redacted for `Type`** — stored as `(text)`, never the text itself (FR-026) |
| `Argument` | `string` | As on the action |
| `Status` | `string` | The `ActionOutcome` status name |
| `Reason` | `string` | The user-facing reason, empty when completed |

**Constraints**:

- Metadata only. No screen images, no audio, no transcript, no clipboard contents, and no
  typed text (FR-026, SC-010). `Clipboard` entries record that a read happened, never what
  was read.
- Capped at 500 entries, oldest discarded (FR-027).
- Survives restart (FR-025); user-clearable (FR-024).
- Every attempt is recorded, including `Declined` and `Blocked` (FR-022, FR-023).

---

## ChatAnswer (modified)

`src/Winly.Core/Providers/IChatProvider.cs`.

| Before | After |
|---|---|
| `DesktopAction? Action = null` | `IReadOnlyList<DesktopAction> Actions` — never null, empty when there are none |

The singular property is removed from the client. The *wire* keeps both fields for one
deprecation window ([research.md R5](./research.md)); the provider collapses them into
`Actions` at the boundary, so nothing downstream of `IChatProvider` knows the old shape
ever existed.

`DesktopActionParser.Parse` correspondingly returns `IReadOnlyList<DesktopAction>` and
strips every `@@DO …@@` tag from the narration rather than only the first — a residual
second tag reaching the speech synthesizer would be read aloud as JSON.
