# Feature Specification: Core Companion Loop

**Feature Branch**: `001-core-companion-loop`
**Created**: 2026-09-12
**Status**: Draft
**Input**: A screen-aware desktop companion the user talks to by holding a key; it sees
the screen, answers aloud, and points at what it is describing.

## Overview

Winly is a resident desktop companion. It has no main window. The user holds a key,
asks a question out loud, and Winly answers out loud — with the user's screen as
context, so "what does this error mean" and "where do I change that setting" are
answerable without the user describing anything.

This feature covers the complete core loop: voice in, screen as context, spoken answer
out, and a visible companion character that can point at on-screen elements.

**Demo-phase visual scope**: the companion is rendered as a simple square body with two
black eyes. No illustrated, licensed, or externally sourced artwork is in scope. The
character is deliberately primitive so the interaction can be proven before any visual
identity work begins.

## Clarifications

### Session 2026-09-12

- Q: What is the maximum duration the activation key can be held before capture is automatically cut off? → A: 30 seconds
- Q: When a second instance of the application is launched while one is already running, what should happen? → A: The second instance shows a brief notification ("Winly is already running") and exits immediately.
- Q: What should the default activation key combination be before the user changes it in settings? → A: Windows key + Left Alt, held together.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask about what is on screen and hear an answer (Priority: P1)

A user working in some application encounters something they do not understand. Without
switching windows, copying text, or typing, they hold a key, ask a question aloud,
release the key, and hear a spoken answer that accounts for what is currently on their
screen.

**Why this priority**: This is the entire reason the product exists. Every other story
is an enhancement of this one. If only this ships, the product is still usable and still
differentiated from typing into a chat window in a browser tab.

**Independent Test**: Launch the application, open any application containing an error
message or unfamiliar interface, hold the key, ask "what am I looking at", release, and
confirm a spoken answer arrives that references something actually on screen.

**Acceptance Scenarios**:

1. **Given** the application is running and idle, **When** the user holds the activation
   key and speaks, **Then** the companion indicates visually that it is listening for the
   entire duration the key is held.
2. **Given** the user has spoken and released the key, **When** the request is being
   processed, **Then** the companion indicates visually that it is working, and that
   indication persists until the spoken answer begins.
3. **Given** a question that relates to visible screen content, **When** the answer is
   produced, **Then** the answer references specific elements present on the screen at
   the moment the key was released.
4. **Given** a question unrelated to the screen, **When** the answer is produced, **Then**
   the answer addresses the question directly without forcing screen content into it.
5. **Given** an answer has been produced, **When** it is delivered, **Then** it is spoken
   aloud, and the spoken phrasing contains no markup, list syntax, or symbols that read
   unnaturally.
6. **Given** multiple displays are connected, **When** a question is asked, **Then**
   content from all connected displays is available as context, and the display
   containing the cursor is treated as the primary one.

---

### User Story 2 - See the companion point at what it is describing (Priority: P2)

When the answer concerns a specific on-screen element — a menu, a button, a field — the
companion physically moves to that element and points at it, so the user does not have
to translate a verbal description into a location.

**Why this priority**: This is the product's principal differentiator over any text-based
assistant, but the product is coherent without it. It depends on P1 being correct, since
pointing at the wrong thing is worse than not pointing.

**Independent Test**: With P1 working, ask "where do I change the font size" in an
application that has such a control, and confirm the companion travels to that control's
location and remains there while the answer is spoken.

**Acceptance Scenarios**:

1. **Given** an answer that identifies a specific on-screen element, **When** the answer
   is delivered, **Then** the companion moves to that element's location and indicates it.
2. **Given** an answer that concerns no specific element, **When** the answer is
   delivered, **Then** the companion does not move to an arbitrary location.
3. **Given** the identified element is on a display other than the one containing the
   cursor, **When** the companion points, **Then** it points on the correct display.
4. **Given** displays with different scaling factors, **When** the companion points,
   **Then** the indicated position corresponds to the intended element on every display
   regardless of its scaling.
5. **Given** the companion is pointing, **When** the user clicks anywhere, **Then** the
   click reaches the application underneath and is not intercepted by the companion.
6. **Given** the companion is visible, **When** it appears or moves, **Then** it never
   takes keyboard focus from the application the user is working in.

---

### User Story 3 - Continue a conversation without repeating context (Priority: P3)

The user asks a follow-up question that only makes sense in light of what was already
discussed, and the companion answers accordingly rather than treating each activation
as an unrelated request.

**Why this priority**: Substantially improves the experience and is inexpensive once P1
exists, but the product demonstrates its value on single questions alone.

**Independent Test**: Ask a question, receive an answer, then ask "why?" or "what about
the other one?" and confirm the second answer is coherent with the first.

**Acceptance Scenarios**:

1. **Given** a prior exchange in the current session, **When** the user asks a follow-up,
   **Then** the answer reflects the earlier exchange.
2. **Given** a long-running session, **When** conversation history grows, **Then** the
   application continues to respond within its normal latency expectations.
3. **Given** the application is restarted, **When** the user asks a question, **Then**
   prior-session conversation content is not present in the new session.

---

### User Story 4 - Control when the companion can see and hear (Priority: P3)

The user decides whether the companion is visible, and can stop it from capturing the
screen or microphone at all, with that decision persisting across restarts.

**Why this priority**: Required for the product to be trustworthy and installable by a
cautious user, but it is a control surface over behavior defined by the stories above.

**Independent Test**: Disable capture in settings, restart the application, hold the
activation key, and confirm nothing is captured and the user is told why.

**Acceptance Scenarios**:

1. **Given** first launch, **When** the application starts, **Then** the user is shown
   what is captured, when, and where it is sent, before any capture occurs.
2. **Given** the user disables capture, **When** the activation key is held, **Then** no
   screen or audio capture occurs and the user is informed capture is off.
3. **Given** any setting is changed, **When** the application is restarted, **Then** the
   setting is preserved.
4. **Given** capture is active, **When** the user looks at the screen, **Then** an
   indicator distinguishes active capture from idle without opening any panel.
5. **Given** the companion is set to hidden, **When** the user activates it, **Then** it
   appears for the duration of the interaction and withdraws afterward.

---

### Edge Cases

- **No microphone present, or the microphone is held exclusively by another application.**
  The user is told the input device is unavailable; the application remains responsive.
- **The activation key combination is already claimed by another application.** The
  conflict is detectable and the user can choose a different combination.
- **The user holds the key but says nothing, or speaks unintelligibly.** No request is
  issued; the companion returns to idle without producing an answer to silence.
- **The user holds the key for an extended period.** Capture is bounded to a maximum of
  30 seconds, after which it stops automatically so a stuck key cannot record
  indefinitely or produce an unbounded request.
- **Network is unavailable, or a provider is slow or returns an error.** The failure is
  surfaced in plain language and the application returns to a state where the next
  activation works.
- **Screen content is protected (DRM-protected video, or an application that blocks
  capture).** Those regions may capture as blank; the companion must not assert facts
  about content it could not see.
- **An elevated (administrator) window is in the foreground.** The companion may be
  unable to draw over it or read it; behavior must be predictable and explained rather
  than appearing broken.
- **A secure-desktop prompt (UAC) or the lock screen is active.** No capture occurs.
- **A display is disconnected, added, or rearranged between capture and pointing.**
  The companion must not point at coordinates on a display that no longer exists.
- **The identified element's coordinates fall outside the captured area.** The position
  is constrained to a valid location rather than sending the companion off-screen.
- **The user activates again while an answer is still being spoken.** The in-flight
  answer is abandoned and the new request takes precedence.
- **The application is launched twice.** A single instance remains in control of the
  activation key; the second launch shows a brief "already running" notification and
  exits immediately.

## Requirements *(mandatory)*

### Functional Requirements

**Activation and voice input**

- **FR-001**: The application MUST run without a taskbar window, presenting itself
  through a system tray entry and an on-screen companion only.
- **FR-002**: The application MUST detect a press-and-hold of a configurable key
  combination system-wide, including while another application has focus. The default
  combination, before the user changes it, MUST be Windows key + Left Alt held
  together.
- **FR-003**: The activation key MUST continue to function in the application the user
  is working in — the hook MUST observe the key without consuming it in a way that
  breaks other software.
- **FR-004**: The application MUST capture microphone audio for exactly the duration the
  activation key is held, subject to a maximum of 30 seconds, after which capture stops
  automatically and the application returns to idle.
- **FR-005**: The application MUST convert captured speech to text and MUST determine
  when the user's utterance is complete after key release.
- **FR-006**: The application MUST NOT issue a request when the resulting transcript is
  empty.

**Screen context**

- **FR-007**: The application MUST capture the contents of all connected displays at the
  moment the activation key is released.
- **FR-008**: The application MUST identify which display contains the cursor and mark it
  as primary context.
- **FR-009**: Each captured display MUST carry its dimensions, so that any position
  referenced in an answer can be resolved to an actual desktop location.

**Answering**

- **FR-010**: The application MUST submit the transcript together with the captured
  displays and receive a natural-language answer.
- **FR-011**: The application MUST speak the answer aloud.
- **FR-012**: Answers MUST be phrased for listening rather than reading — no markup,
  bullet syntax, or symbols that read unnaturally aloud.
- **FR-013**: The application MUST retain the current session's exchanges and include
  them as context in subsequent requests within the same session.
- **FR-014**: Session conversation content MUST NOT persist across application restarts.
- **FR-015**: A new activation MUST cancel any in-flight request and any answer currently
  being spoken.

**Pointing**

- **FR-016**: An answer MUST be able to designate a specific on-screen position and a
  short label for what is at that position, or explicitly designate that no position is
  relevant.
- **FR-017**: Any designation MUST be removed from the text before it is spoken.
- **FR-018**: The application MUST translate a designated position into a desktop
  location that is correct for the display it belongs to, including when displays have
  differing scaling factors.
- **FR-019**: The companion MUST animate to the designated location rather than jumping
  to it instantly.
- **FR-020**: A designated position outside the valid bounds of its display MUST be
  constrained to within that display rather than discarded or followed off-screen.

**Companion presentation**

- **FR-021**: The companion MUST render as a square body with two black eyes for the
  demo phase, drawn entirely by the application with no external image assets.
- **FR-022**: The companion MUST visually distinguish at minimum: idle, listening,
  working, and speaking.
- **FR-023**: The companion MUST be displayed above other windows and MUST span all
  connected displays.
- **FR-024**: The companion MUST NOT accept keyboard focus and MUST NOT intercept mouse
  input intended for the windows beneath it.
- **FR-025**: The user MUST be able to keep the companion permanently visible or have it
  appear only during an interaction and withdraw afterward.

**Consent, settings, and failure**

- **FR-026**: On first launch, the application MUST disclose what is captured, when, and
  where it is transmitted, before any capture occurs.
- **FR-027**: The application MUST present a persistent, glanceable indication whenever
  screen or microphone capture is active.
- **FR-028**: The user MUST be able to disable screen capture, microphone capture, or
  both; when disabled, activation MUST NOT capture and MUST inform the user why.
- **FR-029**: Captured screen and audio data MUST NOT be written to durable storage and
  MUST NOT be retained after the request that consumed it completes.
- **FR-030**: User settings MUST persist across restarts.
- **FR-031**: Every failure of an external dependency MUST be surfaced to the user as a
  short plain-language statement, and MUST return the application to a state where the
  next activation works.
- **FR-032**: The application MUST NOT capture audio, capture frames, or consume
  measurable processor time while idle.
- **FR-033**: Only one instance of the application MUST hold the activation key at a time.
  A second launch attempt MUST show a brief notification that the application is already
  running and MUST exit immediately without affecting the running instance.
- **FR-034**: Credentials for any third-party service MUST NOT be present in the
  distributed application in any form.

### Key Entities

- **Session**: The period between application start and exit. Holds the ordered
  conversation exchanges and is discarded on exit.
- **Exchange**: One user utterance and the answer produced for it.
- **Display Capture**: One display's captured image at a moment in time, with the
  dimensions needed to resolve positions within it and a flag for whether it held the
  cursor.
- **Pointing Target**: A position and a short label designating an on-screen element,
  resolved to a specific display.
- **User Settings**: Activation key combination, companion visibility mode, capture
  permissions, and an optional run-at-login preference (default: off). Persisted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From key release to the first audible word of the answer, elapsed time is
  at most 4 seconds at the median and 7 seconds at the 95th percentile, on a broadband
  connection.
- **SC-002**: The companion reflects a state change within 150 milliseconds of the
  activation key being pressed, so the user never doubts whether it heard them.
- **SC-003**: For questions naming a visible interface element, the companion points
  within the bounds of the intended element in at least 80% of attempts across a
  10-application test set.
- **SC-004**: Pointing accuracy on a secondary display with a different scaling factor
  from the primary is statistically indistinguishable from accuracy on the primary.
- **SC-005**: While idle, the application consumes under 1% processor time averaged over
  five minutes and holds no open capture handle on the microphone or displays.
- **SC-006**: A new user completes first launch — disclosure, permissions, first
  successful question — in under 3 minutes without external instructions.
- **SC-007**: No sequence of provider failure, device unavailability, or display
  reconfiguration leaves the application unable to service the next activation.
- **SC-008**: Inspection of the distributed application reveals no third-party
  credential.
- **SC-009**: 20 consecutive activations complete without the application entering a
  state requiring restart.
- **SC-010**: Every user-visible failure states what went wrong in one sentence
  containing no error codes, exception names, or provider names.

## Assumptions

- The user is on Windows 10 version 1903 or later, x64, with a working microphone,
  speakers or headphones, and an internet connection.
- The user's screen content is in a language the answering service handles, and the user
  speaks a language the transcription service handles. Multi-language support is not
  specified here.
- Answer quality is bounded by the capability of the underlying services; this
  specification governs the application's behavior around them, not their accuracy.
- The demo-phase square companion is a deliberate placeholder. Visual identity — final
  character design, naming, iconography — is out of scope for this feature and will be
  specified separately, subject to the originality and licensing constraints in the
  project constitution.
- Payment, accounts, licensing enforcement, usage metering, and update delivery are out
  of scope for this feature.
- Capture of content the operating system protects from capture is not achievable and is
  not attempted.
