# Worker API — Delta for Overlay Annotation

**Feature**: `003-overlay-annotation` | **Base**: [001 worker-api.md](../../001-core-companion-loop/contracts/worker-api.md), as amended by [002's delta](../../002-desktop-actions/contracts/worker-api.md)

This is a **delta**, not a replacement. Everything in 001's contract and 002's delta still
holds: routes, the bearer-token gate on every POST, SSE framing, `/tts`, `/transcribe-token`,
`/music/*`, `/spotify/*`, the `@@POINT` / `@@NOPOINT` / `@@DO` / `@@NEEDSCREEN` / `@@NEEDWEB`
designations, and the trust boundary. Only the `POST /chat` terminal event changes, plus the
system-prompt instructions that produce it.

**No new route. No new binding. No new secret.**

---

## 1. New designation: `@@SHOW`

Emitted in the answer text alongside `@@POINT` and `@@DO`, extracted by the stream splitter,
and stripped before the text is forwarded for speech — identical handling to the existing tags.

```
@@SHOW {"kind":"<kind>","monitorId":"<display id>","points":[[x,y],...],"label":"<short name>"}@@
```

| Field | Type | Notes |
|---|---|---|
| `kind` | string | One of `region`, `ellipse`, `line`, `arrow`, `polyline` |
| `filled` | bool, optional | Tint the shape as well as outlining it. Defaults to `false`. Applies to `region` and `ellipse`; ignored otherwise. **A tinted outline is one annotation, not two** |
| `monitorId` | string | A display id from the attached images, exactly as `@@POINT` uses |
| `points` | array of `[x, y]` integer pairs | Pixel coordinates inside that display's attached image; `(0,0)` is its top-left corner |
| `label` | string | Short name of what is being marked. **Never spoken** — used for logging and for the failure message |

**Point arity by kind:**

| Kind | Points | Meaning |
|---|---|---|
| `region`, `ellipse` | exactly 2 | opposite corners of the bounding rectangle |
| `line`, `arrow` | exactly 2 | from, to |
| `polyline` | 2 to 32 | vertices in order |

One tag per annotation, repeated — the same shape as `@@DO`, and for the same reason: a
malformed tag then costs one annotation rather than the whole batch.

`@@SHOW` is independent of `@@POINT`. An answer may carry both (point at a control *and* outline
the panel it lives in), either, or neither. There is no `@@NOSHOW`; the absence of the tag is
the negative case, because unlike pointing there is no ambiguity to resolve — nothing drawn is
the default.

## 2. `POST /chat` — terminal `done` event

### Before

```json
{
  "done": true,
  "pointingTarget": { "...": "unchanged" },
  "actions": [ { "...": "unchanged" } ],
  "needsScreen": false,
  "needsWebSearch": false
}
```

### After

```json
{
  "done": true,
  "pointingTarget": { "...": "unchanged" },
  "actions": [ { "...": "unchanged" } ],
  "annotations": [
    { "kind": "region", "filled": true, "monitorId": "\\\\.\\DISPLAY1", "points": [[420, 180], [980, 460]], "label": "the settings panel" },
    { "kind": "arrow", "monitorId": "\\\\.\\DISPLAY1", "points": [[500, 300], [860, 300]], "label": "request flows left to right" }
  ],
  "needsScreen": false,
  "needsWebSearch": false
}
```

`annotations` is always present and is `[]` when nothing was designated. Order is the order the
model wrote them, and the client preserves it — "the first three shapes" means the first three
declared, the same rule action sequences follow.

The Worker **does not cap the array**, for the same reason it does not cap `actions`: capping
in both places would hand the client exactly the limit and hide the fact that the request was
truncated, so the user could never be told the rest was not shown (FR-022). The cap lives in
`AnnotationBounds.MaxCount` alone.

## 3. Stream splitter

`DesignationSplitter` gains one loop, mirroring the `@@DO` one exactly:

- `/@@SHOW\s*(\{[\s\S]*?\})\s*@@/g` — every match parsed and appended to `annotations`
- the same pattern added to the strip list that produces the spoken text
- `@@SHOW` added to the set of tags that make `pending` hold back text at the first `@@`, so a
  half-streamed designation is never spoken aloud

A tag that fails to parse is skipped and the others are kept. Validation of arity, coordinate
range and monitor identity is **client-side** — the Worker does not know the image dimensions it
attached well enough to be the authority, and `Winly.Core` is where that check is testable.

## 4. System prompt

Four additions:

1. **The designation itself** — the tag shape, the kind list, and the arity table above.
2. **When to use it.** Region marking is offered when an answer is about a bounded area of the
   screen. Drawing is offered **only when the user asked to be shown something** — "show me how
   this flows", "circle the ones I need", "draw a box around it". Ordinary answers are never
   decorated (FR-018). This instruction carries the same weight as 002's rule against claiming
   an action without a designation behind it, and fails the same way if omitted: a model that
   decorates every answer produces exactly the screen-covering this feature's bounds exist to
   prevent, one legal annotation at a time.
3. **The cap**, stated as at most 12 annotations per answer, so the model does not self-censor a
   legitimate six-shape flow diagram.
4. **Clearing on request (FR-026).** When the user asks for what is on screen to be removed —
   "get rid of that", "clear the boxes", "take that off" — the model says it has and designates
   nothing. **No mechanism is needed behind this**: asking Winly anything is a new activation,
   and the client already clears annotations at the start of one (FR-024), so by the time the
   model answers, the screen is clear. What the prompt is preventing is the failure where
   "clear that" is answered as a question *about* the screen — which is the same class of
   mistake as "play the first Procreate video" being answered without screenshots.

Point 3 duplicates `AnnotationBounds.MaxCount`. That duplication is deliberate and is the same
one `MaxActionsPerRequest` already carries: the client's copy **enforces**, the Worker's copy
**stops the model writing less than it should**. They have to move together, and neither is
sufficient alone.

`@@SHOW` requires the screenshots — coordinates cannot be invented. When a drawing is asked for
and no images are attached, the model replies `@@NEEDSCREEN@@` exactly as the `click` verb
already does, and is asked again with them. This costs one round trip and never a wrong answer,
which is the established trade for every expensive attachment in this system.

## 5. Worker tests (`worker/src/index.test.ts`)

Added to the existing 13:

- a single `@@SHOW` is extracted and stripped from the spoken text
- several `@@SHOW` tags are extracted in declared order
- a malformed `@@SHOW` is skipped while its well-formed neighbours survive
- `@@SHOW` and `@@DO` and `@@POINT` in one answer are each extracted into their own field
- a `@@SHOW` split across two stream chunks is never emitted as spoken text
- an answer with no `@@SHOW` yields `annotations: []`, not `null`
