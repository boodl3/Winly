# Implementation Plan: Overlay Annotation and Expressive Pointing

**Branch**: `003-overlay-annotation` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification at `specs/003-overlay-annotation/spec.md`

## Summary

Give the overlay three things it does not have: a companion whose body becomes a directional
pointer, a way to mark out a region, and a way to draw simple shapes. All three land on the
same per-monitor transparent window that already exists, and all three are designated through
the same in-band channel `@@POINT` and `@@DO` already use.

The shape of the work is set by one fact about the existing code: **the overlay's only
positional vocabulary is a single point.** `ICompanionOverlay.PointTo` takes one
`DesktopLocation`, `CaptureToDesktopCoordinateMapper.Map` maps one `PointingTarget`, and
`CompanionOverlayWindow` owns one `Buddy` on a `Canvas`. Everything in this feature is either
a second visual on that canvas or a second kind of geometry through that mapper, so the plan
is mostly *widening what already exists* rather than building a parallel system.

Three decisions carry the feature, and each is argued in [research.md](./research.md):

| | Decision |
|---|---|
| The morph (R1) | The body becomes one `Path` over a four-segment cubic Bézier `PathGeometry`. Square and triangle are the same four segments at different control points, so `PointAnimation` on each is the whole transition. No second shape exists to cross-fade with. |
| Travel and rotation (R2) | `MatrixAnimationUsingPath` with `DoesRotateWithTangent`, composed with a separately-animated `ScaleTransform` in a `TransformGroup`. Clicky's reason for a manual 60fps loop does not apply here. |
| Annotation geometry (R5, R7) | Every annotation kind is a list of points plus a kind. One designation shape, one parser path, one mapper, five renderers. |

The rest is bounds and lifetime, which is where the feature's real risk sits — not in drawing
a rectangle, but in guaranteeing the rectangle goes away, **and that it is the right
rectangle that goes away.** Six things can clear an annotation, and three of them are timers
or probes scheduled against a set that may no longer be on screen by the time they fire. That
is the `NAudioPlayback.StopIfCurrent` bug exactly — an abandoned thing tearing down its own
replacement — so it takes the same guard: a `SetId` on every set, and clearing paths split into
"clears only the set it was scheduled for" and "clears whatever the user is looking at". The
rule is stated once in data-model.md under *Who owns the live set*, and it is a Core unit test
rather than a review convention.

## Technical Context

**Language/Version**: C# 14 / .NET 10, unchanged. Worker is TypeScript on Cloudflare Workers.

**Primary Dependencies**: None new. WPF's animation and `System.Windows.Shapes` primitives
already ship with `Winly.App`; the window-identity probe is two existing-style P/Invokes in
`Winly.Platform`. No new package reference in any project, so no Principle III license review
is triggered.

**Storage**: None. Annotation geometry is memory-only for the lifetime of the annotation
(FR-035), which is the same rule capture data already lives under (001 FR-029). Nothing is
appended to the action record — annotations are not actions.

**Testing**: xUnit, as today. The split is deliberate and is what makes this feature
CI-verifiable at all: designation parsing, geometry mapping across mixed DPI, painted-area
arithmetic, count/area/lifetime enforcement, and the morph's vertex correspondence are all
`Winly.Core` and are covered headless. Rendering, the window-identity probe, and how the
morph actually *looks* are `Winly.App`/`Winly.Platform` and go in the manual matrix.

**Target Platform**: Unchanged — Windows 10 1903+ x64, per-monitor DPI aware.

**Performance Goals**:
- The morph and travel run at the display's refresh rate with no dropped frames at 165 Hz.
- Travel completes in 0.5–1.5 s scaled by distance (SC-002).
- With 12 annotations on screen and the companion at rest: under 3% of one core (SC-009).
- **Idle cost must not move at all.** The staleness poll runs only while annotations exist.

**Constraints**:
- Every new visual is `IsHitTestVisible="False"` — FR-028, and 001's FR-024 before it.
- No new capture of any kind. This feature draws over what was already captured.
- The per-frame cursor follow already owns `CompositionTarget.Rendering` on the UI thread;
  nothing added here may run in that handler beyond arithmetic (see "Bugs that cost time").
- Bounds are 12 annotations / 40% painted area / 120 s, and FR-023 requires all three to be
  changeable without touching placement, rendering or clearing.

**Scale/Scope**: Four new `Winly.Core` files plus one interface, one new `Winly.Platform`
file, two new `Winly.App` files plus edits to three existing ones, and one Worker change
(prompt + splitter + terminal event). Three user stories, each independently demonstrable.

## Constitution Check

*GATE: passed before Phase 0. Re-checked after Phase 1 — result at the bottom of this section.*

| Principle | Assessment |
|---|---|
| **I — Secrets never ship in the client** | No credential is touched. The Worker change is prompt text and tag extraction; no new route, no new binding. **Pass.** |
| **II — Consent before capture** | This feature captures nothing. It renders over screen content that was already captured under 001's consent model, and holds that geometry only for the annotation's lifetime (FR-035). The capture indicator is unaffected. **Pass.** |
| **III — Clean-room originality** | Worth stating explicitly, because this is the first feature to take numbers from another product. Clicky's flight parameters — arc lift, duration clamp, smoothstep, the −35° rest angle, the (8,12) landing offset — were supplied as *described behaviour*, not as source. They are reimplemented against a different animation system in a different language; no code is copied, transliterated, or machine-translated, and the resulting companion (a rounded square with eyes, in the user's own portfolio palette) shares no visual identity with it. No new dependency, so no license to record. **Pass.** |
| **IV — Vertical slices** | Three user stories, each shippable and demonstrable alone: US1 improves pointing with no annotation system at all; US2 adds region marking; US3 adds the remaining shapes. There is deliberately **no** "build the annotation layer" milestone — that layer is built *as* US2. **Pass.** |
| **V — Platform interop quarantined** | One new interop surface: reading which window occupies a desktop position, for FR-014b. It goes behind `IWindowIdentityProbe` in `Winly.Core.Abstractions` with its implementation in `Winly.Platform`. Everything decidable headless — parsing, mapping, painted area, bounds, lifetime, vertex correspondence — is in `Winly.Core` and unit-tested. **Pass.** |
| **VI — Clarity over cleverness** | The morph is the one genuinely clever mechanism here, so the vertex correspondence between square and triangle is documented at the geometry rather than left to be re-derived; the coincident-vertex trick is invisible otherwise. **Pass.** |
| **VII — Fail loud to log, soft to user** | FR-032/033. An annotation that cannot be placed is logged with its reason and summarised to the user in one sentence, and never prevents the answer being spoken. **Pass.** |
| **VIII — Actions opt-in, ordered, bounded** | **Does not apply, and that is a deliberate reading.** Principle VIII governs features where Winly "changes the state of the user's machine rather than only observing it and speaking". Drawing on a click-through, non-focusable overlay changes nothing about the machine — no window is moved, nothing is typed, nothing is invoked. So no per-occurrence confirmation is required, and annotations are correctly absent from the action record. The bounding this feature does have (FR-019/020/021) exists for legibility, not for blast radius. |
| **Actuation ladder** | Not engaged for the same reason. The one thing this feature reads from the OS — which window is at a position — is an observation, and it is taken at rung 1 (a direct platform API) rather than inferred from a screenshot. |
| **Per-monitor DPI correctness** | The constitution requires the capture-pixel → desktop-coordinate conversion to be unit-tested including a mixed-scale multi-monitor case. This feature extends that conversion from one point to a list of points, so the existing tests are extended rather than duplicated, with a mixed-DPI case per annotation kind. **Pass, and a required deliverable.** |
| **Resource behavior at idle** | The staleness poll (R6) exists only while at least one annotation is live, so idle cost is unchanged. This is a gate on the design, not an aspiration: a timer that runs unconditionally would violate the constraint outright. **Pass.** |

**Post-Phase-1 re-check**: no gate changed. The design added one interface, no dependency, no
storage, no capture, and no unconditional timer. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/003-overlay-annotation/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R7, the decisions above with their rejected alternatives
├── data-model.md        # Phase 1 — Annotation, AnnotationSet, Anchor, PointerPresentation
├── quickstart.md        # Phase 1 — validation scenarios
├── contracts/
│   └── worker-api.md    # Phase 1 — delta over 001's contract: the @@SHOW designation
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/Winly.Core/                            # net10.0, no Windows types
├── Abstractions/
│   ├── ICompanionOverlay.cs               # EDIT — ShowAnnotations / ClearAnnotations
│   └── IWindowIdentityProbe.cs            # NEW  — the only new interop surface (FR-014b)
├── Annotation/                            # NEW directory
│   ├── Annotation.cs                      # kind + monitor + points + label + placed-at
│   ├── AnnotationKind.cs                  # Region, Ellipse, Line, Arrow, Polyline (fill is a flag)
│   ├── AnnotationSet.cs                   # one answer's annotations; admission against the bounds
│   ├── AnnotationBounds.cs                # 12 / 40% / 120s — the tuning values (FR-023)
│   ├── PaintedAreaCalculator.cs           # FR-020: painted area, not bounding boxes
│   ├── AnnotationDesignationParser.cs     # @@SHOW …@@ fallback, mirroring PointingDesignationParser
│   └── CaptureToDesktopAnnotationMapper.cs# reuses CaptureToDesktopCoordinateMapper per point
├── Pointing/                              # unchanged
└── Providers/IChatProvider.cs             # EDIT — ChatAnswer gains Annotations

src/Winly.Platform/                        # net10.0-windows, all interop
└── Overlay/
    └── WindowIdentityProbe.cs             # NEW — WindowFromPoint + GetWindowRect

src/Winly.App/Overlay/                     # WPF composition
├── SquareBuddyControl.xaml(.cs)           # EDIT — Border body becomes a morphable Path
├── PointingAnimator.cs                    # EDIT — MatrixAnimationUsingPath, scale pulse, distance duration
├── AnnotationLayer.cs                     # NEW  — renders an AnnotationSet onto the monitor's Canvas
├── AnnotationLifetime.cs                  # NEW  — expiry timer + staleness poll, live only while annotated
├── CompanionOverlayWindow.xaml(.cs)       # EDIT — hosts the layer; landing offset and edge clamp
└── CompanionOverlayHost.cs                # EDIT — suspends the follow while annotated (FR-030)

src/Winly.App/                             # EDIT — Esc dismissal wiring, tray "Clear what's on screen"
src/Winly.Core/Companion/CompanionOrchestrator.cs  # EDIT — map and show annotations beside PointTo

tests/Winly.Core.Tests/Annotation/         # NEW — parser, mapper (mixed DPI), area, bounds, lifetime
tests/Winly.Core.Tests/Pointing/           # EDIT — morph vertex correspondence, travel duration curve

worker/src/index.ts                        # EDIT — @@SHOW in the prompt, splitter, terminal event
worker/src/index.test.ts                   # EDIT — splitter tests for @@SHOW
tests/manual-verification.md               # EDIT — Morph / Highlight / Draw / Stale rows
```

**Structure Decision**: the existing four-project client plus the Worker, unchanged. No new
project. The directory `src/Winly.Core/Annotation/` sits alongside `Pointing/` because it is
the same kind of thing — designation in, desktop geometry out — and reuses its mapper rather
than restating the DPI arithmetic. `AnnotationLifetime` is deliberately in `Winly.App` and not
in `Core`: the *rules* (what clears an annotation, when it expires) are Core and tested; the
timer that enacts them is a dispatcher concern.

## Phase ordering and why

Per Principle IV, the milestones follow the spec's story priorities, with one adjustment the
spec owner asked about explicitly:

1. **US1 — morph and point.** Touches no annotation code at all. Demonstrable the moment it
   builds: ask where a control is and watch the buddy travel and become a pointer. Carries the
   least risk because nothing about it can leave something stuck on screen.
2. **US2 — highlighting.** This is where the shared annotation layer gets built — as part of
   the slice, not before it. It brings designation, mapping, bounds, lifetime, staleness and
   clearing, all exercised by exactly one shape. That is the cheapest possible way to prove
   rules that six shapes will later depend on.
3. **US3 — explanatory drawing.** Adds five renderers, five prompt examples, and FR-018. On
   top of a proven US2 this is nearly additive.

The temptation is to build the annotation layer first as its own milestone. That is a
horizontal layer, it is not demonstrable, and Principle IV forbids it.

## Complexity Tracking

No Constitution Check gate failed, so this table is empty.

One thing is worth recording here anyway, because it is a deliberate simplification rather
than a violation and a future session should not mistake it for an oversight:
`PaintedAreaCalculator` sums per-shape analytic area and **does not subtract overlap**, so two
stacked tints count twice. It can therefore refuse a drawing that would in fact have been
within the cap, but can never permit one that exceeds it. Being wrong in the conservative
direction is the correct trade for a guard whose purpose is to stop the screen being covered,
and computing true union area of arbitrary rotated shapes is a great deal of code for a case
the model has no reason to produce. Recorded as a `ponytail:` comment at the calculator.
