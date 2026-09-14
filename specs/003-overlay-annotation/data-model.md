# Data Model: Overlay Annotation and Expressive Pointing

All types below live in `Winly.Core` and compile against plain `net10.0` — no Windows types,
per Principle V. Nothing here is persisted (FR-035).

---

## AnnotationKind

```
enum AnnotationKind { Region, Ellipse, Line, Arrow, Polyline }
```

`Region` is FR-015's region marker; the other four are FR-016's drawing shapes. `Ellipse` serves
both purposes ("circle the three buttons I need"), so it is one kind rather than two.

Outline and tint are **not** separate kinds. FR-015 allows a region to be an outline, a tint, or
both, and a user who asks for one region highlighted has asked for one thing — so the fill is a
flag on the annotation (`Filled`), not a second annotation. Two kinds would silently charge a
both-at-once highlight two of the twelve permitted annotations.

**Point arity, validated at parse time:**

| Kind | Points | Meaning |
|---|---|---|
| Region, Ellipse | exactly 2 | opposite corners of the bounding rectangle |
| Line, Arrow | exactly 2 | from, to |
| Polyline | 2 or more, capped at 32 | the vertices in order |

The polyline cap is a parser-level sanity bound, not one of the three tuning bounds: it stops a
single malformed designation carrying thousands of points before any of the area arithmetic
runs.

## Annotation

One thing drawn on screen.

| Field | Type | Notes |
|---|---|---|
| `Kind` | `AnnotationKind` | |
| `MonitorId` | `string` | Names the display, as `PointingTarget.MonitorId` does |
| `PointsInCapturePx` | `IReadOnlyList<(int X, int Y)>` | In that display's captured-image space — the same space `@@POINT` uses |
| `Filled` | `bool` | Whether the shape is tinted as well as outlined. Meaningful for `Region` and `Ellipse`; ignored by the open kinds |
| `Label` | `string` | Short name of what is being marked. Never spoken; used for logging and for the failure sentence (FR-032) |

**Validation** (rejects the annotation, not the batch — FR-032, and the edge case for malformed
geometry):

- point count matches the kind's arity
- for the two-corner kinds, the corners are not coincident — a zero-area region is not drawable
- every coordinate is finite and non-negative
- `MonitorId` names a display present in the captures this answer was formed from

**`ClampTo(DisplayCapture)`** mirrors `PointingTarget.ClampTo`: constrains every point into
`[0, WidthPx) × [0, HeightPx)`, satisfying FR-014's "constrain" half. An annotation whose points
are *all* outside before clamping is rejected rather than collapsed onto an edge — that is
FR-014's "MUST NOT be drawn" half, and the distinction matters because clamping everything would
silently turn an off-screen designation into a line along the border.

## PlacedAnnotation

An `Annotation` after mapping, as it exists on screen. This is the type `ICompanionOverlay`
receives; it is separate from `Annotation` so that "designated" and "on screen" are not the
same object, which is what makes the lifetime rules testable.

| Field | Type | Notes |
|---|---|---|
| `Source` | `Annotation` | |
| `PointsInMonitorDip` | `IReadOnlyList<(double X, double Y)>` | Device-independent pixels relative to that monitor's origin — the overlay window's coordinate space |
| `Anchor` | `Anchor` | See below |
| `PlacedAt` | `DateTimeOffset` | Start of the lifetime in FR-021 |
| `PaintedArea` | `double` | Square DIPs, from `PaintedAreaCalculator` |

Produced by `CaptureToDesktopAnnotationMapper.Map(annotation, capture, windowIdentity)`, which
calls the existing `CaptureToDesktopCoordinateMapper` once per point. It does not restate the
DPI arithmetic — that arithmetic having exactly one home is a constitutional requirement, and
duplicating it here is the most likely way this feature would break mixed-DPI correctness.

## Anchor

What makes a `PlacedAnnotation` stale. It does **not** move the annotation (FR-014a).

| Field | Type | Notes |
|---|---|---|
| `DesktopPhysicalX/Y` | `double` | The probe point — the centroid of the annotation's points |
| `WindowIdentity` | `WindowIdentity?` | What the probe returned at placement; `null` when nothing was there (the desktop itself), which is a valid and never-stale anchor |

**`WindowIdentity`** is an opaque comparison token: a window handle as a `nint` plus its
rectangle in desktop physical pixels. Both halves are needed — the handle alone misses a move
or a resize of the same window, which is the common case.

**`IsStale(WindowIdentity? current)`** returns true when the recorded identity and the current
one differ. A `null` recorded identity is never stale: an annotation drawn over the desktop
wallpaper has no window to lose.

## AnnotationSet

One answer's annotations, admitted against the bounds as a unit.

| Field | Type | Notes |
|---|---|---|
| `SetId` | `long` | Monotonically increasing, minted when the set is shown. See "Who owns the live set" below — this field is the whole of the race fix |
| `Accepted` | `IReadOnlyList<PlacedAnnotation>` | What will be shown |
| `RejectedCount` | `int` | How many were dropped |
| `RejectionReason` | `string?` | For FR-022's one-sentence message and for the log |

**Admission**, in this order — the order is the behaviour, not an implementation detail:

1. Drop annotations that fail validation or map to a display that is gone. Each is logged
   individually (FR-032).
2. Take annotations in declared order until `AnnotationBounds.MaxCount` is reached.
3. Accumulate `PaintedArea` per monitor; stop admitting to a monitor once the running total
   would exceed `MaxPaintedAreaFraction` of that monitor's area.
4. If anything was dropped at steps 2 or 3, record the count and reason.

Declared order is preserved throughout, so "the first three shapes" means the first three the
model wrote — the same principle as action sequences in 002. A set that admits nothing is a
valid outcome: the answer is still spoken, and the user is told nothing could be shown
(FR-033).

Area is accumulated **per monitor**, not across the desktop, because FR-020 bounds the share of
"the display they are on". A two-monitor answer is not permitted to spend one display's budget
on the other.

## AnnotationBounds

The three tuning values, in one place, satisfying FR-023.

| Constant | Value | Requirement |
|---|---|---|
| `MaxCount` | 12 | FR-019 |
| `MaxPaintedAreaFraction` | 0.40 | FR-020 |
| `Lifetime` | 120 s | FR-021 |

Changing any of these must not require touching parsing, mapping, rendering or clearing — which
is what the admission order above is structured to guarantee. `MaxCount` is also stated in the
Worker's system prompt; see research.md R7 for why the duplication is deliberate and what has to
move with it.

## PointerPresentation

The companion's shape, as data rather than as XAML, so the square/triangle correspondence is
unit-testable (research.md R4).

| Field | Type | Notes |
|---|---|---|
| `RestingVertices` | `IReadOnlyList<BezierVertex>` | Four, the rounded square |
| `PointingVertices` | `IReadOnlyList<BezierVertex>` | Four, the triangle — two coincident at the apex |
| `RestAngle(landing, target)` | `double` | **Derived**, not a constant — the angle that aims the pointer from where it landed at what it is indicating (FR-003). Returns −35° whenever the (8, 12) landing offset was not clamped, so the cursor-like look is what the formula produces rather than a magic number. See research.md R2 |

`BezierVertex` is a start point plus the three control points of the segment leaving it. The
invariant the tests exist to pin: **both lists have the same length, and index *n* in one is the
vertex that travels to index *n* in the other.** A mismatched correspondence produces a morph
that visibly turns inside out — cheap to assert, expensive to notice by eye.

---

## Changes to existing types

**`ChatAnswer`** (`Winly.Core/Providers/IChatProvider.cs`) gains
`IReadOnlyList<Annotation>? Annotations = null`, defaulting to empty exactly as `Actions`
already does. Additive; no existing call site changes.

**`ICompanionOverlay`** gains three members, all under the existing contract that every member
may be called from any thread and none may throw:

```
long ShowAnnotations(AnnotationSet set);   // returns the SetId it minted
void ClearAnnotations();                   // user-initiated: clears whatever is current
void ClearAnnotationsIfCurrent(long setId);// automatic: clears only the set it was scheduled for
```

`ShowAnnotations` replaces whatever is on screen rather than adding to it, so one answer's
annotations can never accumulate on top of another's (FR-024). Both clear methods are
idempotent.

---

## Who owns the live set

There are six ways an annotation can disappear — expiry, staleness, Escape, the tray item, a
new activation, and display reconfiguration. Treated as six independent paths calling one
`ClearAnnotations()`, they are six things that can each tear down a set that is no longer the
one they were scheduled against. **That is not a hypothetical**: it is precisely the bug
`NAudioPlayback.StopIfCurrent` exists to fix, where an abandoned answer's `finally` reached the
shared output device *after* the replacement answer had started playing and stopped the new
one. Losing that race was the common case rather than the rare one, because tearing down the
old thing is literally what starting the new thing does.

The annotation version is identical: a 120-second expiry timer or a staleness tick scheduled
for the answer at 12:00:00 fires while the answer from 12:00:40 is on screen, and wipes it.

So the same guard, and the same split:

| Path | Method | Why |
|---|---|---|
| Expiry | `ClearAnnotationsIfCurrent(setId)` | Scheduled for one specific set |
| Staleness | `ClearAnnotationsIfCurrent(setId)` | Probing one specific set's anchors |
| Display reconfiguration | `ClearAnnotationsIfCurrent(setId)` | Acting on one specific set's monitors |
| Escape | `ClearAnnotations()` | The user means *this*, whatever it is |
| Tray item | `ClearAnnotations()` | Same |
| New activation | `ClearAnnotations()` | Deliberately unconditional — the new request supersedes everything, and this is the one path that *should* tear down whatever it finds |

**The invariant, and the only thing that needs reviewing:** an automatic path may never tear
down a set it was not scheduled for; a user-initiated path always may. Every clearing path is
one of those two lines. This is a Core-testable rule (see `AnnotationSetTests`), not a
comment — `SetId` is an ordinary `long` and "does a stale expiry clear a newer set" is a unit
test with no dispatcher, no timer and no screen in it.

**And one fewer moving part:** the expiry check does not get its own timer. The staleness poll
already ticks at 4 Hz while annotations exist (research.md R6), and expiry is a comparison
against `PlacedAt`. A 120-second deadline does not need 250 ms resolution, and two timers over
the same collection is a second lifetime to keep in step with the first for no benefit.

**`IWindowIdentityProbe`** is new, and is the only new interop surface:

```
WindowIdentity? IdentityAt(double desktopPhysicalX, double desktopPhysicalY);
```

Returns `null` where no window occupies the position. Must not throw; a failure to probe is
reported as `null`, which reads as "the desktop", which is never stale — the conservative
direction for a probe whose failure should not start deleting the user's annotations.
