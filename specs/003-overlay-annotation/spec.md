# Feature Specification: Overlay Annotation and Expressive Pointing

**Feature Branch**: `003-overlay-annotation`

**Created**: 2026-09-14

**Status**: Draft

**Input**: The companion should change shape as it arrives at something it is indicating,
should be able to highlight a region of the screen it is talking about, and should be able
to draw simple explanatory shapes on screen when the user asks it to illustrate something.

## Overview

Today Winly can travel to a position and sit there. That is the whole of its ability to
show the user something. It cannot indicate *which way* it is pointing, it cannot mark out
a region, and it cannot illustrate anything — so any answer whose natural form is a picture
("how does the data flow here", "which three buttons do I need", "this whole panel is the
part you care about") has to be delivered entirely in words.

This feature gives the companion three related abilities, in increasing order of ambition:

1. **A directional pointer.** The companion changes shape on arrival — a continuous
   geometric transition from its resting body into a pointer aimed at the thing it is
   indicating, held while relevant, and reversed when it returns to rest.
2. **Highlighting.** Winly can mark out a region of the screen it is talking about.
3. **Explanatory drawing.** On request, Winly can draw simple shapes — rectangles,
   ellipses, arrows, lines, freehand polylines — on top of the screen to illustrate an
   answer.

Everything the companion draws sits in the same always-on-top, click-through surface the
companion itself occupies. Nothing here changes the state of the user's machine; this
feature is entirely about what Winly can *show*, and is governed by Principle II (consent
before capture) and Principle VII (fail loud to the log, soft to the user) rather than by
Principle VIII, which governs acting.

**Deliberately out of scope.** Text labels, callouts, and speech bubbles drawn on screen;
annotation of anything not currently visible; any annotation the user can interact with
(click, drag, resize); persistence of annotations across restarts; freehand input *from*
the user. These are each a separate feature, and several of them undo the click-through
property this one depends on.

## Clarifications

### Session 2026-09-14

- Q: What should make an annotation disappear — and is there any condition under which one could still be on screen with the user unsure how to remove it? → A: Whichever comes first of three: the next activation, an explicit dismissal by the user, or a timeout. Deliberately *not* the end of the spoken answer — the user starts looking at a drawing at the moment Winly stops talking about it.
- Q: How many annotations may be on screen at once, how much of a display may they cover, and how long may they live? → A: At most 12 annotations, covering at most 40% of the display, for at most 120 seconds. Area counted is painted area, not bounding boxes.
- Q: When an annotation marks something inside a window and that window moves, scrolls, is covered, or closes, what happens to it? → A: Annotations are positioned in desktop coordinates and do not follow anything, but each records the window that was beneath it when placed. If that window stops being the one there — moved, resized, closed, or no longer frontmost — the annotation is cleared rather than left pointing at whatever took its place.
- Q: Where is the companion while an annotation is on screen, and what if the user has hidden it? → A: The companion suspends cursor-following and attaches beside the annotation for its whole lifetime, never overlapping it, then resumes following when it clears. Hiding the companion hides the character only — annotations still appear, because someone who asks for a circle wants the circle. The separate annotation setting is how to turn annotations off.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The companion points at something, and looks like it is pointing (Priority: P1)

The user asks where a control is. The companion travels to it as it does today, but on
arrival its body becomes a pointer shape aimed at the control, so there is no ambiguity
about which of the several nearby things it means. When the answer is over it returns to
resting position and its resting shape.

**Why this priority**: It improves a capability that already exists and ships today, it
carries the least new risk of the three, and it is the one that makes the other two legible
— an annotation with an undirected blob beside it reads as two unrelated things on screen.
It is also a prerequisite in practice: the same on-screen surface, the same coordinate
resolution, and the same lifetime rules are exercised by it first with only one moving part.

**Independent Test**: Ask "where do I change the font size" in an application that has such
a control. Confirm the companion travels there, that its shape changes continuously rather
than swapping or fading, that the resulting pointer is aimed at the control, and that it
reverses when the answer finishes.

**Acceptance Scenarios**:

1. **Given** an answer designating an on-screen position, **When** the companion arrives at
   it, **Then** its body has become a directional pointer aimed at that position.
2. **Given** the companion is changing shape, **When** the transition is observed frame by
   frame, **Then** at every moment there is exactly one continuous shape on screen — never
   two shapes overlapping, never one fading into another, never a discontinuous jump.
3. **Given** the companion is travelling to a designated position, **When** it is in motion,
   **Then** its orientation follows its direction of travel rather than snapping to the final
   orientation at the end.
4. **Given** the companion has arrived, **When** it comes to rest, **Then** it sits beside the
   indicated element rather than covering it, and remains wholly within the bounds of the
   display it is on.
5. **Given** a designated position far across the desktop and another one close by, **When**
   the companion travels to each, **Then** the longer journey takes longer, and neither
   journey exceeds the stated maximum travel time.
6. **Given** the companion is pointing, **When** it is dismissed or the interaction ends,
   **Then** it returns to its resting shape by reversing the same transition.
7. **Given** the companion is mid-travel to one position, **When** a new designation arrives,
   **Then** it redirects from wherever it currently is, without resetting to its resting
   shape or jumping.

---

### User Story 2 - See the region Winly is talking about (Priority: P2)

Winly's answer concerns a region rather than a point — a panel, a block of text, a group of
controls. It marks that region out on screen while it talks about it, so the user can see
the boundary of what is being discussed rather than inferring it from a single pointed-at
pixel.

**Why this priority**: It is the smallest possible step past pointing and it carries most of
the new risk that explanatory drawing carries — a second thing on screen, a lifetime, a
bound, a way to get rid of it — with only one shape to render. Getting it right is what
makes story 3 cheap; getting it wrong is what makes story 3 impossible.

**Independent Test**: Ask "what is this panel for" with a clearly bounded panel on screen.
Confirm a region marker appears over the panel's bounds, that clicking inside it reaches the
application underneath, and that it goes away on its own without the user doing anything.

**Acceptance Scenarios**:

1. **Given** an answer designating a region, **When** the answer is delivered, **Then** a
   marker appears over that region's bounds on the correct display.
2. **Given** a region marker is on screen, **When** the user clicks anywhere within it,
   **Then** the click reaches the application beneath and the marker does not consume it.
3. **Given** a region marker is on screen, **When** the user types, **Then** keyboard input
   goes to the application they were working in.
4. **Given** a region marker is on screen, **When** its clearing condition occurs, **Then**
   it disappears and leaves nothing behind.
5. **Given** a region on a display with a different scaling factor from the primary, **When**
   the marker is placed, **Then** it aligns with the intended region as accurately as it does
   on the primary display.
6. **Given** a designated region extending past the edge of its display, **When** it is
   placed, **Then** it is constrained to within that display rather than drawn off-screen.
7. **Given** a region marker and the companion are both on screen, **When** the companion
   comes to rest, **Then** it does not sit on top of the region it is marking.

---

### User Story 3 - Ask Winly to draw an explanation (Priority: P3)

The user asks for something illustrated — "show me how the data flows through this",
"circle the three buttons I need", "draw a box around the bit I should be editing". Winly
draws simple shapes on top of the screen that illustrate the answer, and talks through them.

**Why this priority**: It is the most expressive of the three and the one the other two are
prerequisites for. It is also the one most likely to produce a mess on screen if unbounded,
so it is worth landing last, on rules that have already been proven by story 2.

**Independent Test**: With a diagram or a form on screen, say "circle the field I need to
fill in" and confirm exactly one shape is drawn, in roughly the right place, and that it
clears the same way a highlight does.

**Acceptance Scenarios**:

1. **Given** a request to illustrate something, **When** the answer is delivered, **Then**
   one or more simple shapes are drawn on screen alongside the spoken answer.
2. **Given** a request with no illustrative intent, **When** the answer is delivered, **Then**
   nothing is drawn — Winly does not decorate ordinary answers.
3. **Given** an answer that would draw more shapes than are permitted, **When** it is
   delivered, **Then** only the permitted number are drawn and the user is told that not
   everything was drawn.
4. **Given** an answer whose shapes would together cover more than the permitted share of a
   display, **When** it is delivered, **Then** the drawing is refused or reduced and the user
   is told, rather than the screen being covered.
5. **Given** shapes are on screen, **When** the user asks Winly to clear them or dismisses
   them directly, **Then** they all disappear at once.
6. **Given** shapes are on screen, **When** the user starts a new request, **Then** the
   previous shapes are gone before the new answer's own annotations appear.
7. **Given** a drawn shape, **When** the user looks at it, **Then** it is visually
   distinguishable from the real interface beneath it — a drawn rectangle is never mistaken
   for a real control.

---

### Edge Cases

- **The display holding an annotation is disconnected or rearranged while it is on screen.**
  The annotation is removed rather than redrawn at a stale location on some other display.
- **The window an annotation refers to moves, is minimised, is covered, or closes while the
  annotation is on screen.** The annotation is cleared. The user is never left with a marker
  floating over unrelated content while Winly talks about something no longer visible.
- **A designated position or region names a display that no longer exists, or that was never
  captured.** Nothing is drawn and the failure is reported plainly rather than guessed at.
- **A designated geometry is malformed** — negative extents, zero area, coordinates that are
  not numbers, an unknown shape type, a polyline with one point. It is rejected; the rest of
  the answer's annotations still render.
- **A designated region is larger than the display it is on.** It is constrained rather than
  clipping the whole screen into a tint.
- **The user activates again while annotations are on screen and an answer is being spoken.**
  The in-flight answer is abandoned per the existing rule, and its annotations go with it.
- **The answering service designates annotations for a request that asked for none.** The
  bound on count and area still applies; unprompted decoration is a defect, not a feature.
- **Screen content is protected from capture, or the foreground window is elevated.** Winly
  may be unable to draw over it or may have had nothing to look at. It must not assert a
  position it could not see, and must behave predictably rather than appearing broken.
- **A secure-desktop prompt or the lock screen appears while annotations are on screen.** No
  annotation is visible on the secure desktop, and the application recovers afterwards.
- **The user has set the companion to hidden.** Annotations still appear; the character does not.
- **Annotations are on screen and the user never asks for anything again.** They must not
  remain indefinitely.
- **The companion's resting position coincides with the region it is marking.** It must move
  rather than obscure its own annotation.

## Requirements *(mandatory)*

### Functional Requirements

**Directional pointing**

- **FR-001**: When the companion indicates a designated position, its body MUST change from
  its resting shape into a directional pointer shape.
- **FR-002**: The change of shape MUST be a continuous geometric transition. At no point may
  two distinct shapes be simultaneously visible, may one shape fade while another appears, or
  may the shape change discontinuously between one moment and the next.
- **FR-003**: The pointer shape MUST be oriented such that it indicates the designated
  position.
- **FR-004**: While travelling to a designated position, the companion's orientation MUST
  follow its direction of travel, and MUST settle to a stated resting orientation on arrival.
- **FR-005**: Travel duration MUST increase with the distance travelled, between a stated
  minimum and a stated maximum, so that a short hop is not slow and a cross-desktop journey is
  not a jump.
- **FR-006**: The companion MUST come to rest offset from the designated position rather than
  on top of it, and the resting position MUST be constrained to leave the companion wholly
  within the bounds of the display, at a stated minimum distance from every edge.
- **FR-007**: On returning to rest, the companion MUST reverse the transition back to its
  resting shape rather than snapping to it.
- **FR-008**: A designation arriving while the companion is mid-travel or mid-transition MUST
  be taken up from the companion's current position, shape and orientation, without an
  intermediate return to rest.
- **FR-009**: The transition and the travel MUST be interruptible at any point without leaving
  the companion in a partially-transformed state once the interaction ends.

**Annotations — designation and placement**

- **FR-010**: An answer MUST be able to designate one or more annotations to be shown while it
  is spoken, and MUST be able to designate none.
- **FR-011**: Annotation designations MUST be carried in-band within the answer text, in the
  same shape as the existing pointing designation, MUST be removed from the text before it is
  spoken, and MUST be recoverable by the application itself if the answering service does not
  extract them.
- **FR-012**: Each annotation MUST name the display it belongs to and MUST express its geometry
  in that display's captured-image coordinate space, so that it can be resolved to a desktop
  location.
- **FR-013**: The application MUST resolve annotation geometry to desktop locations that are
  correct for the display each annotation belongs to, including where displays have differing
  scaling factors.
- **FR-014**: An annotation extending beyond the bounds of its display MUST be constrained to
  within that display. An annotation lying wholly outside its display, or naming a display that
  does not exist, MUST NOT be drawn.
- **FR-014a**: Annotations MUST be positioned in desktop coordinates and MUST NOT follow a window
  as it moves or scrolls.
- **FR-014b**: Each annotation MUST record which window occupied its position at the moment it was
  placed. Where that window subsequently moves, is resized, is closed, or ceases to be the
  frontmost window at that position, the annotation MUST be cleared. An annotation MUST NOT remain
  on screen over content it was not describing — an annotation that has gone stale is removed, not
  corrected and not left in place.

**Annotation kinds**

- **FR-015**: The application MUST support marking out a region of the screen, as an outline, a
  tint, or both. Outline and tint are two presentations of **one** annotation, not two
  annotations — a highlight that is both must cost one against the bounds in FR-019, because a
  user asking for one region highlighted has asked for one thing.
- **FR-016**: The application MUST support drawing rectangles, ellipses, straight lines, arrows,
  and open polylines of multiple points.
- **FR-017**: Every annotation MUST be visually distinguishable from the real interface beneath
  it, so that a drawn shape is not mistaken for a real control.
- **FR-018**: The application MUST NOT draw explanatory shapes for requests that did not ask for
  an illustration.

**Bounds**

- **FR-019**: The number of annotations shown at once MUST be bounded at 12.
- **FR-020**: The total screen area annotations may obscure MUST be bounded at 40% of the display
  they are on. The area counted MUST be the area actually painted, not the bounding boxes of the
  annotations — several long arrows crossing a screen obscure almost nothing, and counting their
  bounding boxes would refuse them.
- **FR-021**: Every annotation MUST disappear without any user action 120 seconds after it
  appeared, if nothing has cleared it sooner.
- **FR-022**: An answer designating more annotations than permitted, or annotations exceeding the
  permitted area, MUST have the excess dropped rather than the request failing, and the user MUST
  be told that not everything was shown.
- **FR-023**: The three bounds in FR-019, FR-020 and FR-021 are tuning values. Changing any of
  them MUST NOT require changing how annotations are designated, placed, rendered, or cleared.

**Lifetime and clearing**

- **FR-024**: An annotation MUST be cleared by whichever of these occurs first: a new activation
  begins, the user explicitly dismisses it, or its lifetime expires. An annotation MUST NOT be
  cleared merely because the spoken answer has finished, since that is the moment the user starts
  looking at it.
- **FR-025**: The user MUST be able to clear every annotation on screen immediately, in a single
  action, without opening any panel and without speaking.
- **FR-026**: The user MUST be able to clear annotations by asking Winly to, in the same way they
  ask for anything else. This requires no clearing mechanism of its own: asking Winly anything
  is a new activation, and FR-024 already clears on activation. What it does require is that
  Winly **answer sensibly** — "get rid of that" must be understood as a request to clear rather
  than answered as a question about the screen, and must designate nothing in reply.
- **FR-027**: No sequence of events may leave an annotation on screen with no available means of
  removing it.

**Presentation and non-interference**

- **FR-028**: Annotations MUST NOT accept keyboard focus and MUST NOT intercept mouse or pointer
  input intended for the windows beneath them.
- **FR-029**: Annotations MUST be displayed above other windows and MUST be placeable on any
  connected display.
- **FR-030**: While annotations are on screen, the companion MUST suspend its idle cursor-following
  and take up a position beside the annotation it is presenting, and MUST NOT overlap any
  annotation. When the last annotation clears, it MUST resume its normal idle behaviour.
- **FR-030a**: Where the user has set the companion to hidden, annotations MUST still be shown.
  The visibility setting governs whether the character is drawn, not whether Winly may show the
  user anything; turning annotations off is FR-034's separate setting.
- **FR-031**: Annotations on a display that is removed or reconfigured MUST be cleared rather
  than redrawn at a stale location.

**Failure, settings, and privacy**

- **FR-032**: An annotation that cannot be placed MUST NOT be discarded silently; the reason MUST
  be logged with enough context to diagnose it and the user MUST be told, in one plain sentence,
  that it could not be shown.
- **FR-033**: A failure to place or draw any annotation MUST NOT prevent the answer being spoken,
  and MUST leave the application able to service the next activation.
- **FR-034**: The user MUST be able to turn annotation off entirely, and that setting MUST persist
  across restarts. With it off, answers are spoken as normal and nothing is drawn.
- **FR-035**: Annotation geometry MUST NOT be written to durable storage and MUST NOT be retained
  after the annotations it describes have been cleared.

### Key Entities

- **Annotation**: One thing drawn on screen for the duration of an answer. Has a kind, a display
  it belongs to, a geometry in that display's captured-image coordinate space, and a lifetime.
- **Annotation Set**: The annotations designated by a single answer. Bounded in count and in total
  area; cleared as a unit.
- **Region Marker**: An annotation kind covering a rectangular area of the screen, drawn as an
  outline, a tint, or both. One annotation regardless of which, per FR-015.
- **Drawn Shape**: An annotation kind representing a rectangle, ellipse, line, arrow, or polyline
  drawn to illustrate an answer.
- **Pointer Presentation**: The companion's shape and orientation — its resting shape, its
  directional pointer shape, and its position between the two.
- **Anchor**: The desktop position an annotation occupies, together with the identity of the window
  that was beneath that position when the annotation was placed. The anchor does not move the
  annotation; it is what allows the annotation to be recognised as stale and removed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across 20 recorded pointing interactions, no frame shows the companion as anything
  other than a single continuous shape — no overlap, no fade, no discontinuity.
- **SC-002**: The companion reaches any designated position anywhere on the desktop within 1.5
  seconds of starting to travel, and takes at least 0.5 seconds for any journey, so the motion is
  always followable.
- **SC-003**: For answers marking out a named interface element, the region marker's bounds fall
  within 10% of the intended element's bounds in at least 80% of attempts across a 10-application
  test set.
- **SC-004**: Annotation placement accuracy on a secondary display with a different scaling factor
  from the primary is statistically indistinguishable from accuracy on the primary.
- **SC-005**: Across 20 consecutive annotated answers, no annotation remains on screen after its
  clearing condition has occurred, and none remains longer than the stated maximum lifetime.
- **SC-006**: At no point do annotations obscure more than the stated maximum share of any single
  display.
- **SC-007**: The user can remove every annotation on screen in one action, taking effect within
  1 second, without opening a panel.
- **SC-008**: With the maximum permitted annotations on screen, clicks and keystrokes reach the
  application beneath in 100% of attempts.
- **SC-009**: With the maximum permitted annotations on screen and the companion at rest, the
  application consumes under 3% processor time averaged over one minute.
- **SC-010**: No annotation failure — malformed designation, missing display, exceeded bound —
  prevents the answer being spoken or leaves the application unable to service the next
  activation, across 20 deliberately malformed designations.
- **SC-011**: Across 20 trials in which the annotated window is moved, resized, closed, or sent
  behind another window while its annotation is on screen, the annotation is removed every time and
  is never observed over content it was not placed on.
- **SC-012**: Every user-visible annotation failure states what did not work in one sentence
  containing no error codes, coordinates, or service names.

## Assumptions

- The answering service is capable of designating a region or a set of shapes with enough
  positional accuracy to be useful. Where it is not, this specification governs the application's
  behaviour around the designation, not its accuracy.
- The user's existing setting for whether the companion is visible at all continues to govern the
  companion; annotation has its own separate on/off setting.
- Annotation is a presentation capability and does not change the state of the user's machine, so
  it is not governed by the actuation principle and requires no per-occurrence confirmation.
- Existing behaviour is unchanged where this feature does not mention it: the companion still
  follows the cursor when idle and when no annotation is on screen, a new activation still
  abandons an in-flight answer, and designations are still stripped before speech.
- Annotating content the operating system protects from capture is not achievable and is not
  attempted.
