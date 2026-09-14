# Quickstart: Overlay Annotation and Expressive Pointing

Validation scenarios for `003`. Grouped by user story so each group can be run the moment that
milestone is done — that is what makes the slices independently demonstrable (Principle IV).

Scenarios marked **CI** are covered by `dotnet test` and need no hardware. The rest need a real
desktop and belong in `tests/manual-verification.md`; the prefix in brackets is the row prefix
to use there.

## Prerequisites

```bash
# .NET 10 SDK is a user-local install and is not on PATH
export DOTNET_ROOT="$LOCALAPPDATA/Microsoft/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

- `WINLY_BACKEND_TOKEN` and `WINLY_PROXY_BASE_URL` set at user scope. A process launched from a
  stale terminal will not see a newly-set one — relaunch the shell if the app logs the backend
  as unresolved.
- **Stop any running `Winly.exe` before building.** It locks `Winly.Core.dll` and the build
  fails with MSB3027, which reads like a file-copy error and nothing like stale code.
- For the mixed-DPI scenarios: two monitors at different scale factors (150% / 100% is the
  configuration the existing pointing work was verified on).

```bash
cd "C:/Personal Projects/Project Winly"
dotnet build Winly.sln                     # must stay at zero warnings
dotnet test                                 # 292 existing + this feature's new Core tests
cd worker && npm test                       # 13 existing + the @@SHOW splitter tests
```

---

## US1 — Morph and point

**1. The morph is continuous.** [Morph] — the headline requirement, FR-002.
Ask "where do I change the font size" in an app with such a control. Record the screen at 60 fps
or higher and step through the arrival frames.
*Expect*: at every frame exactly one closed shape. No frame with two shapes, no frame with a
semi-transparent shape, no frame where the outline jumps. Run it again for the return to rest
and confirm the transition reverses rather than snapping.

**2. Vertex correspondence holds.** **CI**
`PointerPresentationTests`: both vertex lists are the same length, index *n* corresponds to
index *n*, and the triangle's two coincident vertices are the pair the square's two are expected
to collapse into. This is the assertion that catches a morph turning inside out.

**3. Travel duration scales with distance.** **CI** + [Morph]
`PointingAnimatorTests` over the duration curve: a 100 px journey clamps to the 0.6 s floor, an
800 px journey is ~1.0 s, a 2000 px journey clamps to the 1.4 s ceiling. On hardware, point at
something adjacent and then at the far corner of a second monitor and confirm the second visibly
takes longer, and that neither exceeds SC-002's 1.5 s.

**4. The pointer faces its direction of travel.** [Morph]
Point at a target above-left, then one below-right. *Expect*: the shape rotates through the
flight rather than snapping at the end, and settles at the same resting angle both times.

**5. Landing is beside the element, and inside the screen.** [Morph]
Point at something in the extreme top-left corner and then the extreme bottom-right.
*Expect*: the companion never covers the element it is indicating, and never leaves the display
— at least 20 px from every edge in both cases.

**6. A new designation mid-flight is taken up, not restarted.** [Morph] — FR-008.
Ask a pointing question and, while the buddy is still travelling, hold the key and ask another.
*Expect*: it redirects from where it is. **Watch specifically for it returning to the resting
square first, or jumping back to the cursor** — either means the abandoned flight's cleanup ran
against the new one, which is the exact shape of the `NAudioPlayback.StopIfCurrent` bug.

**7. The follow resumes correctly after a point.** [Morph] — the R2 bookkeeping trap.
Point at something, wait for the return to rest, then move the mouse.
*Expect*: the buddy follows the cursor smoothly from where it was. A **jump back to a
pre-flight position** means `Canvas.Left`/`Top` were never reconciled with the matrix animation.

---

## US2 — Highlighting

**8. A region is marked on the right display.** [Highlight]
Ask "what is this panel for" with a clearly bounded panel visible. *Expect*: a marker over the
panel's bounds, on the display it is on.

**9. Clicks and keystrokes pass through.** [Highlight] — FR-028, SC-008.
With a marker on screen, click inside it repeatedly and type. *Expect*: every click reaches the
application beneath; typing goes to the app the user was in; focus never moves to Winly.

**10. Mixed-DPI placement.** **CI** + [Highlight] — SC-004, and a constitutional requirement.
`CaptureToDesktopAnnotationMapperTests` over a two-monitor virtual desktop at 100% and 150%:
every point of every kind maps to the same desktop location the existing point mapper produces
for that coordinate. On hardware, mark a region on the 150% monitor and confirm alignment is no
worse than on the primary.

**11. Out-of-bounds geometry is constrained, fully-outside is refused.** **CI** — FR-014.
A region extending past the display edge clamps to the edge; a region entirely off the display,
or naming a display that does not exist, is rejected and reported rather than clamped into a
sliver along the border.

**12. Expiry.** [Highlight] — FR-021.
Get a marker on screen and leave the machine alone. *Expect*: it disappears on its own at 120 s.

**13. Dismissal.** [Highlight] — FR-025, SC-007.
Press **Escape** with a marker on screen. *Expect*: it clears within a second. Then confirm
Escape still reaches the app beneath — open a dialog with an annotation up, press Escape, and
*expect the dialog to close as well*. A consumed Escape would silently break every dialog on
the machine for the annotation's lifetime.
Then, with a fresh marker up, **ask Winly to clear it** ("get rid of that") — FR-026.
*Expect*: it is gone, and Winly says so rather than describing what was on screen. Nothing new is
drawn. Note that the clearing itself is FR-024 doing its job before the model ever answers, so a
failure here is a prompt failure, not a clearing one.

**14. Next activation clears the previous answer's annotations.** [Highlight] — FR-024.
Ask a marking question, then ask an unrelated one. *Expect*: the first marker is gone before the
second answer's annotations appear, and no marker survives an answer that designates none.

**15. Staleness — the honesty property.** [Stale] — FR-014b, SC-011.
With a marker over a window, in four separate trials: drag the window; resize it; close it;
raise another window over it. *Expect*: the annotation is cleared each time, within about a
quarter-second. **Never accept "it moved with the window" or "it stayed put over the new
content" — both are failures**, the second being the confidently-wrong one this rule exists for.
*Known limitation, not a failure*: scrolling inside a window that does not move leaves the
annotation in place, and it is then wrong (research.md R6).

**16. The companion does not cover its own annotation.** [Highlight] — FR-030.
Get a marker on screen and move the mouse across it. *Expect*: the buddy parks beside the marker
and stays there — it must **not** resume following the cursor across its own annotation while it
is up, and must resume once it clears.

**17. Annotations appear with the companion hidden.** [Highlight] — FR-030a.
Set the companion to hidden in the panel and ask a marking question. *Expect*: the marker
appears; the character does not. Then turn annotations off in settings and repeat: nothing is
drawn and the answer is still spoken normally (FR-034).

**18. Idle cost is unmoved.** [Highlight] — the constitutional constraint.
With no annotation on screen, confirm CPU is where it was before this feature. Then with the
maximum on screen, confirm under 3% averaged over a minute (SC-009). The first half is the one
that matters: a staleness poll running unconditionally would fail the constitution outright.

### Accuracy and endurance

These two are separated from the numbered scenarios because each is a session rather than a
check, and each is the sole evidence for a success criterion.

**A1. Placement accuracy across ten applications.** [Highlight] — SC-003.
Across a 10-application test set (reuse 001's, so the numbers are comparable), ask a question
whose answer concerns a clearly bounded element and record whether the region marker's bounds
fall within 10% of that element's.
*Expect*: at least 8 of 10. Run the last three on the 150%-scaled secondary monitor and confirm
the hit rate there is not worse (SC-004) — a marker that is systematically offset on one display
is a DPI bug, not a model accuracy problem, and the two look identical from one trial.

**A2. Twenty consecutive annotated answers.** [Highlight] — SC-005.
Twenty requests in a row that each designate at least one annotation, mixing region markers and
(once US3 lands) drawings, interrupting some mid-answer and letting others expire.
*Expect*: after each clearing condition, nothing remains; at the end, the screen is clean and
the staleness poll has stopped. **Watch specifically for an annotation from an earlier answer
surviving into a later one, or a later one vanishing early** — the second is an automatic
clearing path tearing down a set it was not scheduled for, which `T036a` proves cannot happen in
Core and this run proves does not happen through the dispatcher.

---

## US3 — Explanatory drawing

**19. A requested drawing appears.** [Draw]
With a form or diagram on screen, say "circle the field I need to fill in". *Expect*: one
ellipse, roughly right, cleared the same way a highlight is.

**20. A flow is drawn in order.** [Draw]
"Show me how the data flows through this" over an architecture diagram. *Expect*: boxes and
arrows in the order the model declared them.

**21. Ordinary answers are not decorated.** [Draw] — FR-018.
Ask ten ordinary questions with screenshots attached. *Expect*: nothing drawn for any of them.
A model that decorates unprompted produces the screen-covering the bounds exist to prevent, one
legal annotation at a time — so this is a prompt bug to fix, not a threshold to raise.

**22. The count cap.** **CI** + [Draw] — FR-019, FR-022.
`AnnotationSetTests`: an answer designating 15 annotations admits the first 12 in declared order
and reports 3 dropped. On hardware, confirm the user is *told* something was not shown rather
than it silently vanishing.

**23. The area cap, and that arrows do not trip it.** **CI** — FR-020, and the reason painted
area was chosen over bounding boxes.
`PaintedAreaCalculatorTests`: eight long arrows spanning a display are admitted (their painted
area is tiny, their bounding boxes nearly the whole screen); two large tints exceed 40% and the
second is refused. If a bounding-box rule ever creeps back in, the first of these fails.

**24. Per-monitor area budget.** **CI** — FR-020.
Annotations spread across two displays are budgeted per display; one display's spend never
consumes the other's.

**25. Malformed designations cost one annotation each.** **CI** — FR-032, SC-010.
A batch containing a polyline with one point, a zero-area box, a non-numeric coordinate, an
unknown kind, and two valid shapes: the two valid ones are drawn, four are logged with reasons,
the answer is still spoken.

**26. Failure never blocks the answer.** [Fail loud] — FR-033, SC-010.
Force a placement failure (designate a display that has just been disconnected). *Expect*: the
answer is spoken in full, one plain sentence says nothing could be shown, and the next
activation works.

---

## Benchmark impact

`tests/action-benchmark.md` is not extended by this feature — annotation is not an action and
never enters the action record. But **SC-001's latency budget still applies**: annotations are
carried in the answer that is already streaming and are rendered while it is spoken, so a
request that draws must not move the `activation.latency` medians. Run
`tests/score-benchmark.ps1 -Since "<time>"` over a session containing several drawing requests
and confirm the three stages are where they were. Any movement means annotation work has
strayed onto the critical path — most likely into `CompositionTarget.Rendering`, which is where
this codebase has been bitten before.
