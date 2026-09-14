---

description: "Task list for 003 — Overlay Annotation and Expressive Pointing"
---

# Tasks: Overlay Annotation and Expressive Pointing

**Input**: Design documents from `/specs/003-overlay-annotation/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/worker-api.md](./contracts/worker-api.md), [quickstart.md](./quickstart.md)

**Tests**: Included and **not optional here**. The constitution's Development Workflow requires
core logic changes to ship with tests, and Principle V is the reason this feature was split the
way it was — the parsing, geometry, area arithmetic and bounds all live in `Winly.Core` precisely
so they can be proven headless. Platform and WPF work carries manual verification rows instead.

**Organization**: Grouped by user story so each milestone is independently demonstrable
(Principle IV).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3, mapping to the spec's user stories

## Path Conventions

Existing four-project client plus the Worker. No new project.
`src/Winly.Core/`, `src/Winly.Platform/`, `src/Winly.App/`, `tests/Winly.Core.Tests/`, `worker/src/`.

---

## Phase 1: Setup

**Purpose**: Nothing is being initialised — this is an existing solution. These are the two
things that waste an hour if skipped.

- [ ] T001 Confirm no `Winly.exe` is running before the first build; a running instance locks `Winly.Core.dll` and the build fails with MSB3027 naming the PID, which reads like a file-copy error and nothing like stale code
- [ ] T002 [P] Create the directories `src/Winly.Core/Annotation/` and `tests/Winly.Core.Tests/Annotation/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**This phase is deliberately empty, and that is the design.**

There is no shared prerequisite. US1 touches only `SquareBuddyControl.xaml` and
`PointingAnimator.cs` and needs nothing from the annotation system; US2 builds the annotation
layer **as part of its own slice**; US3 extends what US2 built. Pulling the annotation layer
forward into a foundational phase would create exactly the horizontal layer Principle IV
forbids — a thing that compiles, is not demonstrable, and defers all integration risk.

**Checkpoint**: proceed straight to User Story 1.

---

## Phase 3: User Story 1 — Morph and point (Priority: P1) 🎯 MVP

**Goal**: The companion travels to a designated position and its body becomes a directional
pointer aimed at it, through one continuous geometric transition, reversing on return to rest.

**Independent Test**: Ask "where do I change the font size" in an app with such a control.
The buddy travels, morphs continuously, faces the control, and reverses when the answer ends.
No annotation code exists yet and none is needed.

### Core geometry and its tests

- [ ] T003 [P] [US1] Add `BezierVertex` (a start point plus the three control points of the segment leaving it) and `PointerPresentation` (`RestingVertices`, `PointingVertices`) in `src/Winly.Core/Pointing/PointerPresentation.cs`, per data-model.md. **No `RestAngleDegrees` constant** — the rest angle is derived in `FlightPath.RestAngle` (T009), because a fixed angle only indicates the target while the landing offset is unclamped. Both vertex lists MUST be four entries; the triangle's apex is expressed as two coincident vertices so every vertex has somewhere to travel
- [ ] T004 [P] [US1] Add `PointerPresentationTests` in `tests/Winly.Core.Tests/Pointing/PointerPresentationTests.cs` asserting the two lists are equal in length, that index *n* corresponds to index *n*, and that exactly two of the triangle's vertices are coincident. This is the assertion that catches a morph turning inside out
- [ ] T005 [P] [US1] Add `FlightPath.ControlPoint(from, to)` in `src/Winly.Core/Pointing/FlightPath.cs` — the midpoint of start→end lifted **perpendicular** to the segment by `min(distance * 0.2, 80)` px (research.md R2). This replaces `PointingAnimator`'s fixed `ArcLift = 120`, which is larger than many of the journeys it is currently applied to
- [ ] T006 [P] [US1] Add `FlightPathTests` in `tests/Winly.Core.Tests/Pointing/FlightPathTests.cs` covering: the lift is perpendicular for a diagonal journey (not merely upward), the 80 px ceiling binds for a long journey, and a zero-length journey does not divide by zero
- [ ] T007 [P] [US1] Add `FlightPath.Duration(distance)` returning `clamp(distance / 800, 0.6, 1.4)` seconds, and `FlightPath.Smoothstep(p) => p * p * (3 - 2 * p)`, in the same file
- [ ] T008 [P] [US1] Add duration/easing tests in `FlightPathTests`: 100 px clamps to the 0.6 s floor, 800 px is ~1.0 s, 2000 px clamps to the 1.4 s ceiling — the band SC-002 requires. Assert smoothstep is 0 at 0, 1 at 1, and 0.5 at 0.5
- [ ] T009 [P] [US1] Add `FlightPath.Landing(target, buddySize, monitor)` in the same file: target + (8, 12) px, then clamped so the whole buddy stays at least 20 px from every edge of its monitor (FR-006). Also add `FlightPath.RestAngle(landing, target)`, returning the angle that aims the pointer from where it **actually landed** at what it is indicating. −35° is not a constant — it is what this formula produces when the clamp does not bind, and a fixed −35° points *away* from a target in a screen corner, where the clamp puts the buddy on the wrong side of it (research.md R2, FR-003)
- [ ] T010 [P] [US1] Add landing tests covering all four corners of a monitor **and a 150%-scaled secondary monitor**, asserting the 20 px margin holds in monitor-DIP space rather than in physical pixels — the constitution requires a mixed-scale case for anything doing this conversion. Add `RestAngle` tests alongside them: the unclamped case is −35° ± 1° (proving the derived value reproduces the cursor-like look), and **every clamped corner still yields an angle pointing at the target** — which is the assertion that pins FR-003

### The companion control

- [ ] T011 [US1] In `src/Winly.App/Overlay/SquareBuddyControl.xaml`, **delete the unused `<Polygon x:Name="Pointer" … Opacity="0"/>`**. It is a half-started attempt at this feature and must not survive alongside the real morph target
- [ ] T012 [US1] In the same file, replace the `Border x:Name="Body"` with a `Path x:Name="Body"` whose `Data` is a `PathGeometry` of one `PathFigure` and **four `BezierSegment`s** at the resting (rounded-square) control points from T003. Keep `Fill="#1B1A16"`, the `DropShadowEffect`, and the existing `TransformGroup` (`BodyScale`, `BodyRotate`, `BodyTranslate`). A `Path` cannot have children, so the eyes grid and `RecordingBadge` become siblings in the same `Grid`, keeping their current alignments and margins
- [ ] T013 [US1] Leave the `0.4` `LayoutTransform` and the UserControl's `Width="28.8" Height="32"` exactly as they are, and keep authoring the geometry at 72×80. Resizing remains one factor in one place
- [ ] T014 [US1] Add a `ToPointer` storyboard in `SquareBuddyControl.xaml.cs` (or as a XAML resource) of **thirteen `PointAnimation`s** — one on `PathFigure.StartPoint` and three per `BezierSegment` (`Point1`, `Point2`, `Point3`) — driving the resting vertices to the pointing vertices over 0.18 s with an ease. Add the reverse. At no point may two shapes be visible or either fade (FR-002)
- [ ] T015 [US1] Animate the eyes grid's `Opacity` to 0 across the morph and back on reverse. **Flagged decision** (research.md R1): the body's outline stays one continuous shape, but a triangle with two eyes squeezed into it reads as a mistake. Keep this as a single animation so it is one line to remove if overruled
- [ ] T016 [US1] Replace `SquareBuddyControl.SetPointing(bool)` so it runs the T014 storyboard rather than toggling opacity on anything, and make it safe to call repeatedly with the same value (the follow loop calls `SetPointing(false)` every frame)

### The flight

- [ ] T017 [US1] Rewrite `src/Winly.App/Overlay/PointingAnimator.Travel` to use `MatrixAnimationUsingPath` with `DoesRotateWithTangent="True"` over a `PathGeometry` holding one `QuadraticBezierSegment` built from `FlightPath.ControlPoint`, with `Duration` from `FlightPath.Duration` and a smoothstep `KeySpline`/easing. Target the `Matrix` of a `MatrixTransform` placed **after** a `ScaleTransform` in a `TransformGroup` on the buddy
- [ ] T018 [US1] Animate that `ScaleTransform` separately with a `DoubleAnimationUsingKeyFrames` approximating `1.0 + sin(progress · π) · 0.3` (peak ~1.3× at mid-flight, 1.0 on landing). It must not touch the matrix — composition through the transform group is what lets the pulse and the tangent rotation coexist (research.md R2)
- [ ] T019 [US1] On flight completion, clear the matrix animation and set `Canvas.Left`/`Canvas.Top` to the landing point before the follow is released. **This is the bookkeeping trap**: a matrix animation moves by render transform and leaves the canvas position untouched, so without this the follow loop snaps the buddy back to its pre-flight position the instant `ReturnToRest` runs
- [ ] T020 [US1] Settle the rotation to `FlightPath.RestAngle(landing, target)` on arrival rather than leaving it at the final tangent angle. **Not a fixed −35°** — see T009; the angle and the landing offset are a matched pair and the edge clamp breaks the pair
- [ ] T021 [US1] Make a designation arriving mid-flight take up from the buddy's **current** position, shape and orientation (FR-008): cancel the in-flight animations, read the current transform, and start the new flight from there without an intermediate return to rest
- [ ] T022 [US1] In `src/Winly.App/Overlay/CompanionOverlayHost.ReturnToRest`, run the reverse morph before releasing the `_pointing` hold, so the buddy is a square again by the time the cursor follow resumes (FR-007). **This must also hold when the interaction is abandoned rather than completed** (FR-009): a new activation arriving mid-morph must not leave the body stuck part-way between square and triangle. `ReturnToRest` is already called on the orchestrator's finally path, so the fix is that the reverse morph is unconditional there — not that a second path is added
- [ ] T023 [US1] Add the `Morph` rows to `tests/manual-verification.md` for quickstart scenarios 1, 3–7 — including the frame-by-frame continuity check, which is the only proof of FR-002 and SC-001

**Checkpoint**: US1 is complete and demonstrable. Pointing looks different and nothing else in the app has changed.

---

## Phase 4: User Story 2 — Highlighting (Priority: P2)

**Goal**: Winly marks out a region of the screen it is talking about, bounded, clearable, and
cleared when it goes stale.

**Independent Test**: Ask "what is this panel for" with a bounded panel on screen. A marker
appears over it, clicks pass through it, it clears on Escape, on the next activation, on expiry,
and when the window moves.

**This phase builds the shared annotation layer.** US3 adds five renderers on top of it and
almost nothing else.

### Core model and its tests

- [ ] T024 [P] [US2] Add `AnnotationKind` (`Region, Ellipse, Line, Arrow, Polyline`) in `src/Winly.Core/Annotation/AnnotationKind.cs`. Declare all five now even though US2 renders only `Region` — the parser and the arity table are shared, and adding enum members later means revisiting every exhaustive switch. **Outline and tint are not separate kinds**: FR-015 lets a region be either or both, so the fill is a `Filled` flag on the annotation. Two kinds would charge a both-at-once highlight two of the twelve permitted annotations for what the user asked for as one thing
- [ ] T025 [P] [US2] Add `Annotation` (`Kind`, `MonitorId`, `PointsInCapturePx`, `Filled`, `Label`) in `src/Winly.Core/Annotation/Annotation.cs` with validation per data-model.md: **exactly 2 points for `Region`/`Ellipse`/`Line`/`Arrow`, 2 to 32 for `Polyline`**; the two corner kinds reject coincident corners (a zero-area region is not drawable); every coordinate finite and non-negative
- [ ] T026 [P] [US2] Add `Annotation.ClampTo(DisplayCapture)` mirroring `PointingTarget.ClampTo` — constrain every point into `[0, WidthPx) × [0, HeightPx)`. An annotation whose points are **all** outside before clamping is rejected, not collapsed onto an edge; clamping everything would silently turn an off-screen designation into a line along the border (FR-014)
- [ ] T027 [P] [US2] Add `AnnotationTests` in `tests/Winly.Core.Tests/Annotation/AnnotationTests.cs` covering each arity rule, coincident corners, negative and non-finite coordinates, partial clamping, and full-outside rejection
- [ ] T028 [P] [US2] Add `AnnotationBounds` in `src/Winly.Core/Annotation/AnnotationBounds.cs` with `MaxCount = 12`, `MaxPaintedAreaFraction = 0.40`, `Lifetime = TimeSpan.FromSeconds(120)` (FR-019/020/021). Comment that `MaxCount` is also written in the Worker's system prompt and that the two must move together — the client's copy enforces, the Worker's stops the model self-censoring a legitimate six-shape diagram
- [ ] T029 [P] [US2] Add `PaintedAreaCalculator` in `src/Winly.Core/Annotation/PaintedAreaCalculator.cs` per research.md R5, keyed off the `Filled` flag rather than off separate kinds: region and ellipse are `w·h` and `π·a·b` when filled and `perimeter × thickness` when not; line/arrow `length × thickness`; polyline the sum of its segments. Add a `ponytail:` comment naming the ceiling — overlap is not subtracted, so it can refuse a drawing that was within the cap but can never permit one that exceeds it, which is the correct direction for this guard
- [ ] T030 [P] [US2] Add `PaintedAreaCalculatorTests` covering quickstart scenario 23: **eight long arrows spanning a display are admitted** (tiny painted area, near-full-screen bounding boxes) while two large tints exceed 40% and the second is refused. If a bounding-box rule ever creeps back in, the first of these fails
- [ ] T031 [P] [US2] Add `WindowIdentity` (a window handle as `nint` plus its rectangle in desktop physical pixels) and `Anchor` (`DesktopPhysicalX/Y`, `WindowIdentity?`) with `IsStale(WindowIdentity? current)` in `src/Winly.Core/Annotation/Anchor.cs`. A `null` recorded identity is **never stale** — an annotation over the desktop wallpaper has no window to lose
- [ ] T032 [P] [US2] Add `AnchorTests` asserting that a changed handle is stale, that the **same handle with a moved or resized rectangle is also stale** (the handle alone misses the common case), and that a null-to-null comparison is not stale
- [ ] T033 [US2] Add `PlacedAnnotation` (`Source`, `PointsInMonitorDip`, `Anchor`, `PlacedAt`, `PaintedArea`) and `CaptureToDesktopAnnotationMapper.Map(annotation, capture, windowIdentity)` in `src/Winly.Core/Annotation/CaptureToDesktopAnnotationMapper.cs`, calling the existing `CaptureToDesktopCoordinateMapper` **once per point**. Do not restate the DPI arithmetic — that arithmetic having exactly one home is a constitutional requirement and duplicating it here is the most likely way this feature breaks mixed-DPI correctness (depends on T025, T029, T031)
- [ ] T034 [US2] Add `CaptureToDesktopAnnotationMapperTests` over a two-monitor virtual desktop at 100% and 150%, asserting every point of every kind maps to the same desktop location the existing point mapper produces for that coordinate (SC-004, quickstart 10)
- [ ] T035 [US2] Add `AnnotationSet` (`SetId`, `Accepted`, `RejectedCount`, `RejectionReason`) with admission in the order data-model.md specifies: drop invalid and missing-display annotations individually, then take in declared order to `MaxCount`, then accumulate painted area **per monitor** and stop admitting to a monitor once it would exceed its share, then record what was dropped and why (depends on T028, T033)
- [ ] T036 [US2] Add `AnnotationSetTests` covering quickstart 22, 24 and 25: 15 designated admits the first 12 in declared order and reports 3 dropped; one display's area budget never consumes another's; a batch of four malformed and two valid shapes admits the two and reports four with reasons
- [ ] T036a [US2] Add **the ownership invariant test** in `tests/Winly.Core.Tests/Annotation/AnnotationOwnershipTests.cs`: a clear scheduled for set *N* must not clear set *N+1*, and a user-initiated clear must clear whatever is current. `SetId` is an ordinary `long`, so this needs no dispatcher, no timer and no screen — which is the entire reason the rule is expressed as a field rather than as a review convention (data-model.md, *Who owns the live set*)
- [ ] T037 [P] [US2] Add `AnnotationDesignationParser` in `src/Winly.Core/Annotation/AnnotationDesignationParser.cs` mirroring `PointingDesignationParser`'s defensive role: recover `@@SHOW {…}@@` tags from the raw answer when the structured field is absent, and strip every occurrence from the spoken text (FR-011)
- [ ] T038 [P] [US2] Add `AnnotationDesignationParserTests`: one tag, several tags in declared order, a malformed tag skipped while its neighbours survive, a tag with an unknown `kind` rejected, and **stripping proven for every case** — a designation reaching the speech synthesiser is the failure this parser exists to prevent

### Contracts and platform

- [ ] T039 [P] [US2] Add `IWindowIdentityProbe` with `WindowIdentity? IdentityAt(double desktopPhysicalX, double desktopPhysicalY)` in `src/Winly.Core/Abstractions/IWindowIdentityProbe.cs`. Document that it must not throw and that a failed probe returns `null`, which reads as "the desktop" and is never stale — the conservative direction for a probe whose failure must not start deleting the user's annotations
- [ ] T040 [US2] Add `long ShowAnnotations(AnnotationSet set)`, `void ClearAnnotations()` and `void ClearAnnotationsIfCurrent(long setId)` to `src/Winly.Core/Abstractions/ICompanionOverlay.cs`, under the existing contract that every member may be called from any thread and none may throw. `ShowAnnotations` **replaces** what is on screen rather than adding to it and returns the `SetId` it minted (FR-024); both clears are idempotent. **The two clear methods are the fix for the six-path race** — an automatic path may only tear down the set it was scheduled for, a user-initiated path may tear down whatever is current. Document that split on the interface, not just in the plan
- [ ] T041 [P] [US2] Add `Annotations` to `ChatAnswer` in `src/Winly.Core/Providers/IChatProvider.cs`, defaulting to empty exactly as `Actions` does — additive, no existing call site changes
- [ ] T042 [P] [US2] Implement `WindowIdentityProbe` in `src/Winly.Platform/Overlay/WindowIdentityProbe.cs` over `WindowFromPoint` and `GetWindowRect`, declared in the existing platform P/Invoke style. Returns `null` on any failure. Add a `ponytail:` comment recording the known ceiling: scrolling inside a window that does not move is undetectable this way, and was accepted knowingly in clarification

### Rendering, lifetime and clearing

- [ ] T043 [US2] Add `src/Winly.App/Overlay/AnnotationLayer.cs` rendering a `PlacedAnnotation` onto the monitor's existing `Canvas`. US2 implements `Region` only — one shape honouring `Filled` for the tint, not two code paths. **Every element is `IsHitTestVisible="False"`** (FR-028), styled from `Theme.xaml`'s palette, and visually distinct from real UI (FR-017) — do not start a second palette
- [ ] T044 [US2] Add `src/Winly.App/Overlay/AnnotationLifetime.cs` owning **one** `DispatcherTimer` at 4 Hz, **started when the first annotation appears and stopped when the last one clears** (research.md R6). A timer running unconditionally would fail the constitution's idle-CPU constraint outright, so this is a correctness requirement, not a nicety. Expiry does **not** get a second timer — a 120-second deadline is a comparison against `PlacedAt` on the tick that is already running, and a second timer over the same collection is a second lifetime to keep in step and one more thing that can fire against a set that is no longer on screen
- [ ] T045 [US2] Implement both checks on that one tick: clear annotations whose `Anchor.IsStale` returns true (FR-014b), and clear annotations older than `AnnotationBounds.Lifetime` (FR-021). Both go through `ClearAnnotationsIfCurrent(setId)` with the id captured when the timer was started — **never the unconditional clear**
- [ ] T046 [US2] Implement `ShowAnnotations` / `ClearAnnotations` / `ClearAnnotationsIfCurrent` in `src/Winly.App/Overlay/CompanionOverlayHost.cs`, marshalled to the UI thread and wrapped in the existing `Guarded` helper, routing each annotation to the overlay window for its monitor and dropping any whose monitor is gone. `ShowAnnotations` mints the next `SetId` and records it as current; `ClearAnnotationsIfCurrent` returns without doing anything when the id it was given is not the current one. Model it on `NAudioPlayback.StopIfCurrent`, which exists for exactly this failure and is the closest thing in the tree to copy
- [ ] T047 [US2] Clear annotations on display reconfiguration in `CompanionOverlayHost.OnDisplaySettingsChanged` via `ClearAnnotationsIfCurrent`, rather than letting `RebuildWindows` redraw them at a stale location (FR-031). Note that `RebuildWindows` already destroys the canvases the annotations live on, so this task is about the bookkeeping set, not the pixels — leaving it out gives a host that believes annotations are live after their windows are gone
- [ ] T048 [US2] Suspend the cursor follow while any annotation is on screen and park the companion beside — never overlapping — the annotation it is presenting, resuming the follow when the last one clears (FR-030). Reuse the existing `_pointing` hold rather than adding a second suspension flag
- [ ] T049 [US2] Make annotations render regardless of `CompanionVisibilityMode` (FR-030a). Hiding the companion hides the character only; turning annotations off is the separate setting in T050
- [ ] T050 [P] [US2] Add an annotations on/off setting to `UserSettings` (default on) with a panel checkbox, persisted like the existing settings (FR-034). With it off, answers are spoken normally and nothing is drawn
- [ ] T051 [US2] Wire **Escape** as the dismissal gesture through the existing low-level keyboard hook: **non-consuming**, and acted on only while at least one annotation is live (research.md R3, FR-025). A consumed Escape would silently break every dialog on the machine for the annotation's lifetime. Calls the unconditional `ClearAnnotations()` — the user means whatever is in front of them, not a particular set
- [ ] T052 [P] [US2] Add a *Clear what's on screen* item to the tray menu as the discoverable second path, calling the same unconditional `ClearAnnotations()` as T051
- [ ] T053 [US2] In `src/Winly.Core/Companion/CompanionOrchestrator.cs`, map and show the answer's annotations alongside the existing `PointTo` call (~line 399–414), and clear them at the start of a new activation (FR-024) and on the abandonment path. Both use the unconditional `ClearAnnotations()` — **deliberately**, because a new request supersedes everything and this is the one automatic path that *should* tear down whatever it finds. Guard the abandonment path on `cancellationToken.IsCancellationRequested` rather than on an exception type, for the reason recorded in CLAUDE.md: a cancelled activation surfaces as whatever the aborted transport happened to throw
- [ ] T054 [US2] Speak the FR-022/FR-032 message through `Announce` when annotations were dropped or could not be placed — **not `Post`**. Add an orchestrator test with a fake overlay that throws on `ShowAnnotations`, asserting the **answer still completes and is spoken in full** (FR-033, SC-010). That assertion is the one that matters: it is cheap to write a failure path that reports correctly and still swallows the answer on its way out. A failure written to a panel nobody has open is the whole of "it says it did something it didn't", and `Announce` already waits its turn behind the answer so it cannot truncate it
- [ ] T055 [US2] Log each rejected annotation with its reason and each placement with its kind, monitor and label, so the log alone distinguishes "the model designated nothing" from "it was designated and refused" (FR-032, Principle VII)

### Worker

- [ ] T056 [P] [US2] Add `@@SHOW` extraction to `DesignationSplitter` in `worker/src/index.ts`: a `/@@SHOW\s*(\{[\s\S]*?\})\s*@@/g` loop mirroring the `@@DO` one, the same pattern added to the strip list, and `@@SHOW` included in the hold-back set so a half-streamed designation is never spoken
- [ ] T057 [P] [US2] Add `annotations` to the terminal `done` event, always present and `[]` when nothing was designated. **Do not cap it in the Worker** — capping in both places hides the truncation from the user (contracts §2)
- [ ] T058 [US2] Add the `@@SHOW` designation, the kind list, the arity table, and the "at most 12" cap to the system prompt; add the region-marking guidance and the rule that `@@SHOW` requires screenshots and otherwise answers `@@NEEDSCREEN@@`. **Also add the FR-026 clearing clause**: "get rid of that" / "clear the boxes" is a request to clear, answered by saying so and designating nothing — never answered as a question *about* the screen. No mechanism sits behind it, because asking Winly anything is a new activation and T053 already clears on activation; the prompt line is the whole of FR-026. **Re-check `cacheRead` is still non-zero on a second consecutive request** — the prompt is cached with a 1 hour TTL and caching is silently ignored below 1024 tokens
- [ ] T059 [P] [US2] Add the six splitter tests from contracts §5 to `worker/src/index.test.ts`
- [ ] T060 [US2] Deploy with `cd worker && npx wrangler deploy` and confirm at 100% with `npx wrangler deployments list`. Verify on the wire with an authenticated `curl` that a region question comes back with a populated `annotations` array
- [ ] T061 [US2] Add the `Highlight` and `Stale` rows to `tests/manual-verification.md` for quickstart scenarios 8–18 **and A1–A2** — A1 is the 10-application accuracy set SC-003 is measured against, and A2 is the 20-consecutive-answer endurance run SC-005 requires, which is also the most likely place a `SetId` leak shows up that T036a cannot see. The staleness rows are the only proof of SC-011, and they must reject **both** "it moved with the window" and "it stayed put over the new content"

**Checkpoint**: US2 is complete. Highlighting works end to end, and the layer US3 needs exists and is proven.

---

## Phase 5: User Story 3 — Explanatory drawing (Priority: P3)

**Goal**: On request, Winly draws ellipses, lines, arrows and polylines to illustrate an answer —
and draws nothing for ordinary answers.

**Note on FR-016's "rectangles":** they already work. Merging box into `Region` means the drawn
rectangle and the highlighted region are one kind, delivered by T043 in US2. This phase adds the
remaining four renderers, not five.

**Independent Test**: With a form on screen, say "circle the field I need to fill in" and get one
ellipse that clears exactly as a highlight does. Ask ten ordinary questions and get nothing drawn.

- [ ] T062 [P] [US3] Extend `AnnotationLayer` with the `Ellipse` renderer (bounding rectangle from the two designated corners)
- [ ] T063 [P] [US3] Extend `AnnotationLayer` with the `Line` renderer
- [ ] T064 [US3] Extend `AnnotationLayer` with the `Arrow` renderer — a line plus a head at the second point, sized from the stroke thickness and rotated to the segment's angle (depends on T063)
- [ ] T065 [P] [US3] Extend `AnnotationLayer` with the `Polyline` renderer over its 2-to-32 vertices
- [ ] T066 [US3] Add the arrowhead's triangle to `PaintedAreaCalculator` so an arrow's area is its shaft plus its head, and extend `PaintedAreaCalculatorTests` accordingly
- [ ] T067 [P] [US3] Add renderer-selection tests asserting the mapping from each of the five `AnnotationKind` values is **exhaustive**, and that `Filled` is honoured by `Region` and `Ellipse` and ignored by the open kinds — a new kind must fail this test until a renderer is decided, the same way 002's consequential classifier is exhaustive over its enum
- [ ] T068 [US3] Add the five drawing examples to the Worker's system prompt in the style of the existing `@@DO` examples ("show me how the data flows" → boxes and arrows; "circle the three buttons I need" → three ellipses)
- [ ] T069 [US3] Add the FR-018 rule to the system prompt: drawing happens **only when the user asked to be shown something**; ordinary answers are never decorated. This carries the same weight as 002's rule against claiming an action without a designation, and fails the same way if omitted — a model that decorates every answer produces the screen-covering the bounds exist to prevent, one legal annotation at a time
- [ ] T070 [US3] Redeploy the Worker and confirm on the wire that "show me how the data flows through this" returns several ordered `@@SHOW` annotations while an ordinary screen question returns `annotations: []`
- [ ] T071 [US3] Add the `Draw` rows to `tests/manual-verification.md` for quickstart scenarios 19–21 and the hardware half of 22

**Checkpoint**: all three stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T072 Run `dotnet build Winly.sln` and confirm **zero warnings**, then `dotnet test` and `cd worker && npm test`
- [ ] T073 Run `tests/score-benchmark.ps1 -Since "<time>"` over a session containing several drawing requests and confirm the three `activation.latency` stages have not moved. Movement means annotation work has strayed onto the critical path — most likely into `CompositionTarget.Rendering`, which is where this codebase has been bitten before
- [ ] T074 Measure idle CPU with no annotation on screen and confirm it is unchanged from before this feature (the constitutional constraint), then measure with 12 annotations up and confirm under 3% over one minute (SC-009)
- [ ] T075 [P] Update `CLAUDE.md`: what the overlay can now do, the three tuning values and where they live, the two-file cap coupling, the R2 canvas/matrix reconciliation trap, and the scroll-staleness ceiling
- [ ] T076 [P] Add a "Where to look before making changes" entry to `CLAUDE.md` for an annotation appearing in the wrong place — `CaptureToDesktopAnnotationMapper` reproduces it headless, `AnnotationLayer` is what was actually drawn, and the log line names the kind and label
- [ ] T077 Work through `quickstart.md` end to end on real hardware with two monitors at different scale factors, and record the results in `tests/manual-verification.md`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: empty by design — nothing blocks the stories
- **US1 (Phase 3)**: depends on Setup only. Touches no annotation code
- **US2 (Phase 4)**: depends on Setup only. **Does not depend on US1** — the two could be built by different people at the same time without conflict, since US1 is confined to `SquareBuddyControl.xaml` and `PointingAnimator.cs`
- **US3 (Phase 5)**: depends on US2. It adds renderers to `AnnotationLayer` and examples to a prompt that US2 created
- **Polish (Phase 6)**: depends on whichever stories were taken

### Within US2

T024–T032 are independent and parallel. T033 needs T025/T029/T031. T035 needs T028/T033.
T043–T049 need T040. T053–T055 need T035 and T046. The Worker tasks T056–T059 are independent
of all the client work and can be done at any point; T060 needs them.

### Parallel opportunities

- T003–T010 (all of US1's Core geometry and its tests) are eight independent files
- T024–T032 (US2's Core model) are nine independent files
- T056/T057/T059 (Worker) run alongside any client task
- T062, T063, T065, T067 (US3's renderers) are independent of each other; T064 needs T063

---

## Parallel Example: User Story 2's Core model

```bash
# Nine independent files, no shared state:
Task: "AnnotationKind in src/Winly.Core/Annotation/AnnotationKind.cs"
Task: "Annotation + validation in src/Winly.Core/Annotation/Annotation.cs"
Task: "AnnotationBounds in src/Winly.Core/Annotation/AnnotationBounds.cs"
Task: "PaintedAreaCalculator in src/Winly.Core/Annotation/PaintedAreaCalculator.cs"
Task: "Anchor + WindowIdentity in src/Winly.Core/Annotation/Anchor.cs"
Task: "AnnotationDesignationParser in src/Winly.Core/Annotation/AnnotationDesignationParser.cs"
Task: "AnnotationTests, PaintedAreaCalculatorTests, AnchorTests, AnnotationDesignationParserTests"
```

---

## Implementation Strategy

### MVP: User Story 1 only

1. Phase 1 (Setup) → Phase 3 (US1)
2. **Stop and validate**: run quickstart scenarios 1–7 on hardware
3. This is shippable on its own. It improves something that already works, adds no new
   failure mode, and cannot leave anything stuck on screen

### Incremental delivery

1. US1 → demo → the companion points properly
2. US2 → demo → Winly can mark out what it is talking about, and the layer is proven
3. US3 → demo → Winly can illustrate an answer

Each step is a vertical slice with a person able to see it work (Principle IV).

### Where the risk actually is

Not in drawing a rectangle, and — since the `SetId` guard — no longer spread across six tasks
either. Six things still clear an annotation, but they are now two kinds of thing, and which
kind each one is has a single-line answer:

| Automatic — `ClearAnnotationsIfCurrent(setId)` | User-initiated — `ClearAnnotations()` |
|---|---|
| Expiry (T045) | Escape (T051) |
| Staleness (T045) | Tray item (T052) |
| Display reconfiguration (T047) | New activation (T053) — deliberately unconditional |

**T036a is the review target**, not the six call sites. It is a Core unit test with no
dispatcher, no timer and no screen in it, and it fails if any automatic path is ever allowed to
tear down a set it did not originate. That was the whole point of making ownership a field
rather than a convention: a rule that has a test does not need a careful reviewer.

What is left to watch by eye is narrower and worth naming: **T046 is the only place two
bookkeeping systems meet** (the minted-and-current `SetId` in the host, and the timer's captured
id in `AnnotationLifetime`), and it is the same shape as the `Canvas.Left`/matrix seam in T019.
Both are bookkeeping, and both look like rendering bugs when they go wrong.

---

## Notes

- `[P]` = different files, no dependency on an incomplete task
- Commit after each task or logical group; the user is sole author on every commit in this repo
- Stop at any checkpoint to validate a story independently
- 002's hardware pass is still open. This feature's manual rows join that queue rather than
  replacing it — do not treat a green `dotnet test` as verification of anything in `Winly.App`
  or `Winly.Platform`
