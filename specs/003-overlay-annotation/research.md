# Research: Overlay Annotation and Expressive Pointing

Seven unknowns, resolved before design. Each entry records what was chosen, why, and what was
rejected — so a later session can disagree with the reasoning rather than re-derive it.

---

## R1 — How does one shape become another without a cross-fade?

**Decision.** The companion's body becomes a single `Path` whose `Data` is a `PathGeometry`
holding **one `PathFigure` with four cubic `BezierSegment`s**. The rounded square and the
triangle are the same four segments with different control points. The transition is a
`Storyboard` of `PointAnimation`s — one on `PathFigure.StartPoint` and three on each segment's
`Point1`, `Point2`, `Point3` — thirteen in total, all real dependency properties, all
animatable.

The triangle is expressed with the **same four corners as the square**, two of them coincident
at the apex. That is the standard trick for making vertex counts agree, and it is the only
reason every vertex has somewhere to travel.

**Rationale.**

- WPF cannot tween between two arbitrary `Geometry` objects. There is no `GeometryAnimation`,
  and `PathGeometry.Figures` is not animatable. Animating the *vertices* of a fixed-topology
  figure is the only native route.
- Four Béziers rather than four `LineSegment`s because the companion's body is a rounded
  square (`CornerRadius=18` on a 64×64 body) and that rounding is most of its character.
  `LineSegment` gives a sharp-cornered square, which silently redesigns the mascot. `ArcSegment`
  would round it, but its `Size` is a `Size` dependency property with no built-in animation
  type, so the corners could not change radius during the morph — the triangle would keep the
  square's corner radius exactly.
- Every control point of a `BezierSegment` is a `Point` DP, so the whole outline is animatable
  with one animation type and no custom interpolation.

**Consequences that must be handled, not discovered later.**

- A `Path` cannot have children. The eyes and the recording badge are currently inside the
  `Border` that is being replaced, so they become siblings in the same `Grid`, drawn over the
  path and positioned by the alignment and margins they already use.
- **The eyes fade out across the morph.** This is a choice, and it is the one place this plan
  bends the "no cross-fade" instruction — so it is called out rather than buried. The
  instruction is about the *body*: the body's outline must be one continuous shape throughout,
  and it is. The eyes are features drawn on the body, and a triangular cursor with two eyes
  squeezed into it reads as a mistake rather than as a pointer. If that judgement is wrong it
  is a one-line change (drop the opacity animation and reposition the eyes toward the
  triangle's base), so it is cheap to overrule after seeing it.
- The `LayoutTransform` scale of `0.4` stays exactly where it is. The geometry continues to be
  authored at 72×80.

**Alternatives rejected.**

| Alternative | Why not |
|---|---|
| Two shapes cross-fading | Explicitly rejected by the feature owner, and it looks like a bug — for a moment there are two companions. |
| Swap `Path.Data` at the midpoint of a scale-down/scale-up | A disguised swap. FR-002 forbids a discontinuous shape change. |
| Custom `AnimationTimeline<Geometry>` doing vertex interpolation | Real, and a lot of code, to reach exactly what thirteen `PointAnimation`s already do. Violates Principle VI. |
| Render the body to a `DrawingVisual` per frame | Puts geometry construction in the render path, which is the trap already recorded in CLAUDE.md about `CompositionTarget.Rendering`. |

---

## R2 — Tangent rotation: `MatrixAnimationUsingPath`, or a per-frame loop?

**Decision.** `MatrixAnimationUsingPath` with `DoesRotateWithTangent="True"`, animating the
`Matrix` of a `MatrixTransform` that sits in a `TransformGroup` **after** a separately-animated
`ScaleTransform`. The scale pulse is an ordinary `DoubleAnimation` on that `ScaleTransform`,
and the morph is its own `Storyboard` on the path geometry. Three independent animations,
composed by the transform group, no manual frame loop.

**Rationale.**

- It carries position *and* tangent-facing rotation from one `PathGeometry`, which is the two
  hardest parts of the flight, natively and with no `atan2`.
- The worry about it — that it overwrites the whole matrix and so cannot carry a scale pulse —
  is real but is solved by composition rather than by abandoning it. A `TransformGroup`
  multiplies its children, so a `ScaleTransform` animated by its own `DoubleAnimation` and a
  `MatrixTransform` animated by the path animation coexist without either touching the other's
  properties.
- **Clicky's stated reason for using a 60fps timer does not transfer.** Its problem was
  implicit animation fighting per-frame rotation updates. WPF's is explicit, keyframed, on one
  property, and — critically — Winly's own per-frame cursor follow is *already suspended*
  during a point (`CompanionOverlayHost._pointing`), so there is nothing left running that
  could fight it.

**The trap to handle.** The existing code positions the buddy with `Canvas.Left`/`Canvas.Top`,
and `CompanionOverlayWindow.CurrentPosition` reads those back to start the next ease from. A
matrix animation moves the element by render transform and leaves `Canvas.Left`/`Top`
untouched, so the moment the follow loop resumes it would snap the buddy back to wherever it
was before the flight. On landing, the animation is cleared and `Canvas.Left`/`Top` are set to
the landing point, so the two position systems agree before the follow is released. This is
exactly the kind of seam that looks like a jitter bug and is really a bookkeeping bug.

**Flight parameters**, reimplemented from the described behaviour (see the Principle III note
in plan.md — these are parameters, not ported code):

| | Value |
|---|---|
| Path | Quadratic Bézier; control point = midpoint of start→end, lifted perpendicular by `min(distance * 0.2, 80)` px |
| Duration | `clamp(distance / 800, 0.6, 1.4)` s — satisfies SC-002's 0.5–1.5 s window |
| Easing | Smoothstep, `t = p²(3 − 2p)` |
| Scale pulse | `1.0 + sin(progress · π) · 0.3`, peaking ~1.3× mid-flight |
| Landing offset | target + (8, 12) px, then clamped ≥20 px from every screen edge (FR-006) |
| Rest angle | **Derived** from the landing vector — the angle that aims the pointer from where it actually landed at the position it is indicating |

Note the perpendicular lift replaces the current fixed `ArcLift = 120`, which is larger than
many of the journeys it is applied to — today a short hop arcs absurdly high.

**The rest angle is derived rather than fixed, and that is a correction to the supplied values.**
Clicky's −35° is not an arbitrary cursor angle: with the buddy landed at target + (8, 12) — down
and right of the target — the direction back to the target is up-left at `atan2(-12, -8)`, which
is −34° from vertical. The angle and the offset are a **matched pair**, and −35° only indicates
the target because the offset put the buddy exactly there.

FR-006's edge clamp breaks that pair. A target in the bottom-right corner cannot be approached
from below-right without leaving the screen, so the buddy is clamped up-left of it — and a fixed
−35° then points away from the thing it is supposed to be indicating, which is FR-003 violated
in exactly the case a user is most likely to notice.

Computing the angle from the landing vector is **less** code than a constant plus a guard against
clamping in a direction that would break it, and it is correct everywhere rather than correct in
the common case. It also reproduces −35° exactly whenever the clamp does not bind, so the
cursor-like look is preserved for free rather than preserved by a magic number.

**Alternatives rejected.** A `CompositionTarget.Rendering` loop computing position, tangent and
scale by hand: more code, puts three animations' worth of arithmetic on the UI thread every
frame, and re-solves what the framework already solves. It stays available as a fallback if the
matrix/scale composition misbehaves in practice, and that fallback is worth one manual
verification row rather than a second implementation.

---

## R3 — What is the single dismissal action (FR-025)?

**Decision.** **Escape**, observed through the low-level keyboard hook that is already
installed for the activation key, **non-consuming**, and acted on **only while at least one
annotation is on screen**. A tray menu item, *Clear what's on screen*, is the discoverable
second path.

**Rationale.**

- Escape already means "dismiss this" everywhere in Windows. Nothing needs teaching.
- The hook exists. No second global hook, no new interop, no new failure mode at startup.
- Non-consuming matters, and is the same rule 001's FR-003 already applies to the activation
  key: Escape must still reach the application underneath, or Winly would silently break
  every dialog on the machine while an annotation happened to be up.
- Gating on "annotations exist" means the hook's behaviour is unchanged in every other moment,
  so the risk is bounded to a window that is at most 120 seconds long.

**Accepted cost.** An Escape pressed for an unrelated reason clears annotations. That is the
correct outcome anyway — the user pressed dismiss — and the alternative is a key nobody will
guess.

**Alternatives rejected.** Clicking the annotation (the overlay is click-through by FR-028 and
making it otherwise would undo the feature's central safety property); a second configurable
hotkey (a settings row, a conflict-detection story, and a thing to remember, for a gesture
Escape already provides); voice only (FR-025 explicitly requires a path that is neither a panel
nor speech, because speech is exactly what is unavailable when something is covering the
screen).

---

## R4 — Where does the work live, given Principle V?

**Decision.**

| Layer | Gets |
|---|---|
| `Winly.Core` | Designation parsing, capture→desktop mapping for point lists, painted-area arithmetic, count/area/lifetime admission, the clearing rules, and the square/triangle vertex correspondence as data |
| `Winly.Platform` | One new thing only: `WindowIdentityProbe`, reading which window occupies a desktop position |
| `Winly.App` | Rendering the shapes, the morph storyboards, the expiry timer, the staleness poll, the Escape wiring |

**Rationale.** The test-value is concentrated almost entirely in Core. "Does a 40%-area drawing
get refused", "does a polyline land correctly on a 150%-scaled secondary monitor", "does a
malformed designation drop just itself and not the batch" are all decidable with no desktop
session, and they are the questions that will actually be got wrong. What cannot be tested
headless is reduced to a single interop call with a trivial signature.

The vertex correspondence being *data in Core* rather than points in XAML is the non-obvious
part, and it is there so the correspondence itself — that square vertex *n* maps to triangle
vertex *n*, with two coincident — is unit-testable. A wrong correspondence produces a morph
that visibly turns inside out, and that is a bug better caught by an assertion than by eye.

---

## R5 — How is "painted area" computed (FR-020)?

**Decision.** Analytic area per shape, summed, **overlap not subtracted**:

| Kind | Area counted |
|---|---|
| Region | `w · h` when `Filled`, otherwise perimeter × stroke thickness |
| Ellipse | π·a·b if filled; perimeter × thickness if stroked |
| Line, Arrow | length × thickness (plus the arrowhead's triangle) |
| Polyline | Σ segment length × thickness |

**Rationale.** FR-020 exists because a tint can swallow a screen. Bounding boxes were rejected
in clarification for a concrete reason: four arrows crossing a display have bounding boxes
covering nearly all of it while painting almost nothing, so a bounding-box rule would refuse
exactly the drawing the feature exists to enable.

Ignoring overlap over-counts. That means the calculator can refuse a drawing that was actually
within the cap, and can **never** permit one that exceeds it. For a guard whose whole purpose
is to stop the screen being covered, being wrong in the conservative direction is correct.
Computing the true union area of arbitrary rotated shapes is a large amount of geometry for a
case a model has no reason to produce. Recorded as a `ponytail:` comment naming the ceiling.

**Alternatives rejected.** Rasterising to an offscreen mask and counting pixels (accurate,
costs a render per answer, and puts a GPU operation in the answer path); bounding boxes
(rejected in clarification, above).

---

## R6 — How is staleness detected (FR-014b), without costing anything at idle?

**Decision.** **One** `DispatcherTimer` at **4 Hz, started when the first annotation appears and
stopped when the last one clears.** Each tick asks `IWindowIdentityProbe` which window is at
each live annotation's anchor point and compares it — handle plus window rectangle — against
what was recorded at placement. A mismatch clears that annotation.

**That same tick also enforces expiry.** FR-021's 120-second lifetime is a comparison against
`PlacedAt`, and a 120-second deadline does not need its own timer at 250 ms resolution. Two
timers over the same collection would be a second lifetime to keep in step with the first, for
no benefit — and one more thing that can fire against a set that is no longer on screen (see
"Who owns the live set" in data-model.md).

**Rationale.**

- Bounded work: at most 12 probes, four times a second, for at most 120 seconds. `WindowFromPoint`
  and `GetWindowRect` are cheap synchronous calls.
- **Zero cost at idle**, which is a constitutional constraint rather than a preference. A timer
  that ran unconditionally would fail "MUST NOT consume measurable CPU" outright.
- 4 Hz is chosen against human perception, not against machine capability: a marker that
  survives a quarter-second past a window move is not noticeable; one that survives two seconds
  is exactly the confidently-wrong failure this rule exists to prevent.
- Comparing the window *rectangle* as well as the handle is what catches a move or a resize
  that leaves the same window under the same point — the handle alone would miss it.

**Alternatives rejected.** `SetWinEventHook` on `EVENT_OBJECT_LOCATIONCHANGE` (a global hook
that fires for every window movement anywhere on the system, including every animating one,
to serve at most 12 subscribers — far more expensive than the poll it replaces, and a second
global hook to install and tear down correctly); checking only on the next activation (leaves
the stale annotation visible for up to 120 s, which is the failure); tracking scroll position
(no general way to read it across applications, and the clarification deliberately chose
invalidation over tracking).

**Known ceiling, worth recording.** Scrolling *within* a window that does not move does not
change the window identity, so the annotation survives a scroll and is then wrong. This is the
limit of the invalidation approach and it was accepted knowingly in clarification — the
alternative is per-application scroll tracking, which does not generalise. `ponytail:` comment
at the probe.

---

## R7 — What does the designation look like on the wire?

**Decision.** One repeated in-band tag, exactly mirroring how `@@DO` already works:

```
@@SHOW {"kind":"region","filled":true,"monitorId":"<display id>","points":[[x1,y1],[x2,y2]],"label":"<short name>"}@@
```

`kind` ∈ `region | ellipse | line | arrow | polyline`, with an optional `filled` flag rather than
separate outline and tint kinds (FR-015 treats both-at-once as one annotation). `points` are pixel coordinates in
that display's attached image, the same space `@@POINT` already uses. The Worker extracts every
occurrence into an `annotations` array on the terminal `done` event and strips them from the
spoken text; `AnnotationDesignationParser` recovers them client-side if it does not, mirroring
`PointingDesignationParser`'s defensive role.

**Rationale.**

- **One uniform `points` array across all five kinds is the whole reason this stays small.**
  Box, tint and ellipse take two points (opposite corners of the bounding rectangle); line and
  arrow take two (from, to); polyline takes two or more. One parser path, one mapper call per
  point, one validation rule ("at least two points, and exactly two unless polyline"). Giving
  each kind its own field shape would multiply the parser, the mapper and the tests by six for
  no expressive gain.
- Reusing the existing capture-pixel space means `CaptureToDesktopCoordinateMapper` is reused
  per point rather than restated, and the mixed-DPI correctness that is already proven for
  pointing extends to annotations for free. This is the single biggest reason not to invent a
  new coordinate convention here.
- Repeated tags rather than one tag holding an array, because the splitter already has exactly
  this loop for `@@DO` (`/@@DO\s*(\{[\s\S]*?\})\s*@@/g`) and a malformed tag then costs one
  annotation instead of the whole batch.

**The coupling this creates, stated plainly.** The cap of 12 will be written in the Worker's
prompt *and* in `AnnotationBounds`. This is the same two-file coupling that already exists for
`MaxActionsPerRequest`, and it exists for the same reason: the client's copy enforces, the
Worker's copy stops the model self-censoring a legitimate request. They must move together, and
a comment at each should say so.
