# Worker API — Delta for Desktop Actions

**Feature**: `002-desktop-actions` | **Base**: [001 worker-api.md](../../001-core-companion-loop/contracts/worker-api.md)

This is a **delta**, not a replacement. Everything in `001`'s contract still holds:
routes, authentication posture, SSE framing, the `/tts`, `/transcribe-token`, `/music/*`
and `/spotify/*` routes, and the trust boundary. Only the `POST /chat` terminal event
changes, plus the system prompt instruction that produces it.

---

## 1. `POST /chat` — terminal `done` event

### Before

```json
{
  "done": true,
  "pointingTarget": { "...": "unchanged" },
  "action": { "action": "open", "target": "Spotify" },
  "needsScreen": false,
  "needsWebSearch": false
}
```

`action` was a single object or `null`.

### After

```json
{
  "done": true,
  "pointingTarget": { "...": "unchanged" },
  "actions": [
    { "action": "open", "target": "Spotify" },
    { "action": "play", "target": "jazz" }
  ],
  "needsScreen": false,
  "needsWebSearch": false,
  "awaitingReply": false
}
```

| Field | Type | Notes |
|---|---|---|
| `actions` | `DesktopAction[]` | **Authoritative and sole.** Ordered as the model declared them. Always present; `[]` when there are none. Not capped — see §2. |
| `awaitingReply` | `boolean` | The answer asked the user something it needs an answer to. Produced by an `@@LISTEN@@` designation, which is stripped from the spoken text like every other. The client reopens the microphone for one reply rather than making the user press the activation key again; it is a hint, and a client that ignores it simply behaves as before. |

`pointingTarget`, `needsScreen` and `needsWebSearch` are unchanged. The shape of an
individual `DesktopAction` object is unchanged — same eleven verbs, same fields, same
per-verb meanings as `001`.

### The singular `action` field is gone

It was carried for one deprecation window on the reasoning in [research.md R5](../research.md):
the Worker deploys to every user at once and the client does not. That gate held trivially —
the only client ships from this tree and deploys alongside the Worker — so both halves were
removed (T055). A client reading `action` will now see no actions at all, which is correct:
no such client exists.

### Client tolerance

The client reads `actions` and nothing else. A `done` event without the field yields an
answer-only turn rather than an error, so a Worker *version* written but never promoted to
the active deployment degrades to speaking without acting rather than failing — a failure
mode this project has hit before, and which otherwise presents as the change having
silently done nothing.

---

## 2. Ordering guarantee

`actions` is in the order the model wrote the `@@DO …@@` designations in its response, and
the client executes them in that order (FR-002). The Worker MUST NOT sort, deduplicate, or
reconcile them — two actions that conflict (mute, then set volume to 40) run in declared
order and the last one wins, per the spec's Edge Cases.

Truncation to 5 is the client's responsibility alone (FR-003). The Worker MUST NOT cap
`actions`: handing the client exactly 5 when the model wrote 12 is indistinguishable, from
the client's side, from a request that genuinely asked for 5 — so the user would never be
told the rest did not happen. The Worker forwards every designation it parsed and lets the
client both truncate and report it.

---

## 3. Extraction change

The stream splitter currently takes the first designation only:

```js
const actionMatch = /@@DO\s*(\{[\s\S]*?\})\s*@@/.exec(this.pending);
```

It must collect every designation in document order (`matchAll`), validating each
independently. **A malformed designation is discarded and the valid ones around it are
kept** (FR-010) — one bad tag must not void the whole sequence.

Tag stripping from the spoken text already uses a global regex and is unchanged; it must
stay global, since any residual `@@DO …@@` reaching TTS is read aloud as JSON.

---

## 4. System prompt change

The instruction

> Only ever write one.

is replaced with guidance to emit one designation per thing the user asked to happen, in
the order they should happen, up to ten — an application before anything that depends on
it. (Five originally; raised on 2026-09-13, since one hold is one utterance and people list
several jobs in it. The cap that counts is still `DesktopActionSequenceRunner`'s, not this one.) The prohibition on mentioning designation lines in the spoken answer is unchanged, as
is the instruction to add designations only when the user actually asked for something to
happen.

**Cost note**: the system prompt is cached with a 1 hour TTL and caching is silently
ignored below 1024 tokens. This edit changes its length. After deploying, confirm
`cacheRead` is still non-zero on a second consecutive request (`npx wrangler tail`) —
a prompt that drops under the threshold stops being cached with no error anywhere.

---

## 5. Unchanged: the trust boundary

`001`'s rule stands verbatim and is load-bearing here. The client never executes anything
the Worker sends: an app name is resolved against what is actually running or installed
and launched with no arguments, a URL opens only when its scheme is http or https, a path
is searched for under the user's own profile, and player URIs are built client-side from
a spoken name. Chaining multiplies how many strings arrive per request; it does not change
what any of them is permitted to do.
