<!--
Sync Impact Report
Version: 1.1.0 → 1.2.0
Added principles: none
Modified sections: VIII. Actions Are Opt-In, Ordered, and Bounded — the wall-clock
  bound moves from the sequence to each action within it.
Removed sections: none
Rationale for MINOR bump: materially changes guidance within an existing principle
  without removing or redefining it. Per-action bounds detect a hung action sooner
  than an aggregate bound (a stuck readiness wait trips its own cap rather than
  waiting out a sequence-wide budget), and do not cut off a legitimately slow
  request because earlier actions were slow. The protection the principle exists
  for — Winly can never sit silently busy forever — is preserved.
Retroactive effect: feature 002's FR-004 was written against the previous wording
  and carried a declared deviation in plan.md's Complexity Tracking. That deviation
  is resolved by this amendment and its row is removed.
Templates requiring review: none
Follow-up TODOs: none

---

Sync Impact Report (superseded by above)
Version: 1.0.0 → 1.1.0
Added principles:
  VIII. Actions Are Opt-In, Ordered, and Bounded
Modified sections: Additional Constraints (added "Actuation capability ladder")
Removed sections: none
Rationale for MINOR bump: adds one principle and one constraint. Nothing previously
permitted becomes forbidden for feature 001, which specifies no actuation at all.
Retroactive effect: the desktop-action code already in src/Winly.Core/Actions,
src/Winly.Platform/Actions and src/Winly.Providers/Music predates any governing
principle or spec. Feature 002 brings it under governance; the gaps it is currently
non-compliant on (no confirmation gate, no audit record, no declared bounds) are
carried as that feature's requirements rather than as a separate remediation.
Templates requiring review:
  - plan-template.md → plans for features that act on the desktop MUST add a
    Constitution Check row for Principle VIII
Follow-up TODOs: none

---
Sync Impact Report (superseded by above)
Version: 0.0.0 → 1.0.0 (initial ratification)
Added principles:
  I. Secrets Never Ship in the Client
  II. Consent Before Capture
  III. Clean-Room Originality and License Hygiene
  IV. Vertical Slices Over Horizontal Layers
  V. Platform Interop Stays Quarantined
  VI. Clarity Over Cleverness
  VII. Fail Loud to the Log, Soft to the User
Added sections: Additional Constraints, Development Workflow, Governance
Removed sections: none
Templates requiring review:
  - plan-template.md → Constitution Check gates must reference Principles I, II, III, V
  - tasks-template.md → task generation must preserve vertical-slice ordering (Principle IV)
Follow-up TODOs: none
-->

# Winly Constitution

Winly is a Windows desktop AI companion: it watches the user's screen on demand,
listens on push-to-talk, answers aloud, and points at on-screen elements. These
principles govern every feature, PR, and dependency decision in the project. They
are non-negotiable; deviations require the amendment procedure in Governance.

## Core Principles

### I. Secrets Never Ship in the Client

Third-party API credentials MUST NOT exist in the distributed application — not in
source, not in compiled binaries, not in config files shipped alongside the binary,
not in the user's registry or AppData. All calls to paid or credentialed third-party
services MUST route through a backend the project controls, which holds the real
credentials server-side.

Where a service requires a direct client connection that a proxy cannot sit inside
(for example a streaming socket), the backend MUST mint a short-lived, narrowly
scoped token for that single session, and the client MUST receive only that token.

Any PR that introduces a credential-shaped literal, or a direct client-to-vendor
call carrying a long-lived key, is rejected regardless of other merits.

**Rationale:** A distributed desktop binary is public. Every string inside it is
extractable with commodity tooling, and an extracted key is billed to the project's
account until it is rotated. This is the single highest-severity failure mode the
project can ship, and it is unrecoverable after release.

### II. Consent Before Capture

Screen capture and microphone capture MUST occur only as the direct result of a
user-initiated action, and MUST NOT run continuously in the background.

While either capture is active, the application MUST display an unambiguous visual
indicator the user can see without opening any panel. The user MUST be able to
disable capture entirely and have that setting persist across restarts. On first
run the application MUST disclose, in plain language, what is captured, when, and
where it is transmitted.

Captured screen and audio data MUST NOT be written to durable local storage or
transmitted to any destination other than the project's own backend, and MUST NOT
be retained past the request that consumed it.

**Rationale:** Windows imposes no equivalent of macOS TCC's per-app screen-recording
gate, so the operating system will not enforce this on the project's behalf. An app
that can silently watch a screen and listen to a room is exactly the shape of a piece
of malware; the visible difference is the consent model, and it has to be built in
rather than bolted on. This is also the difference between a shippable consumer
product and one that cannot survive its first security review.

### III. Clean-Room Originality and License Hygiene

Winly is an independent implementation. Code MUST NOT be copied, transliterated, or
machine-translated from any existing companion application, and the product name,
icon, and visual identity MUST be distinct from any existing product in the category.

Every third-party dependency MUST have its license reviewed and recorded before it
is added. Copyleft-licensed dependencies (GPL family, AGPL, SSPL) MUST NOT be linked
into or distributed with the client application. Permissively licensed dependencies
(MIT, Apache-2.0, BSD) are acceptable, and their notices MUST be preserved and shipped.

Third-party creative assets — avatars, icons, illustrations, fonts, sounds — are
governed by the same rule as code, and their licenses MUST permit commercial
redistribution in a closed-source binary.

**Rationale:** The project intends to ship commercially and closed-source. A single
copyleft dependency compels source disclosure of the entire work it is linked into,
which is unwindable after distribution. Trademark exposure attaches to name and look
independently of whether any code was copied. Both are cheap to avoid in advance and
expensive to remediate afterward.

### IV. Vertical Slices Over Horizontal Layers

Every milestone MUST be independently runnable and demonstrable end to end. Work MUST
NOT be sequenced as "build all the infrastructure, then connect it."

A milestone is complete only when a person can launch the application and observe the
slice working. Partial layers that cannot be exercised by a user do not count as
progress and MUST NOT be merged to the main branch as a milestone completion.

**Rationale:** This project integrates five independently failure-prone subsystems —
global input hooks, screen capture, audio capture, streaming network I/O, and an
always-on-top overlay. Building them in isolation defers all integration risk to the
end, which is where projects of this shape die. Slicing vertically surfaces the
integration failures while there is still budget to respond to them.

### V. Platform Interop Stays Quarantined

All P/Invoke declarations, Win32 handle manipulation, WinRT interop, and other
OS-specific calls MUST live in a dedicated platform layer behind interfaces defined
by the application core. The core orchestration logic MUST NOT reference OS-specific
types directly.

The core MUST be unit-testable in a headless environment with no desktop session, no
audio device, and no network, using fakes that implement the platform interfaces.

**Rationale:** The interop surface is the part of this codebase that cannot be tested
in CI and is most likely to break across Windows versions. Confining it means the
state machine, response parsing, coordinate math, and error handling — where the
logic bugs actually live — remain testable, and the untestable portion stays small
enough to verify by hand.

### VI. Clarity Over Cleverness

Names MUST state what a thing is or does, at a length that makes it unambiguous to a
reader with no prior context; abbreviations and single-character identifiers are not
acceptable outside conventional loop counters. Where a name cannot carry the full
meaning, a comment MUST explain why the code exists, not restate what it does.

A longer, more readable implementation is preferred over a shorter, denser one.

**Rationale:** The non-obvious parts of this codebase — DPI-space coordinate
conversion, window style flags, audio buffer conversion — are already hard to read
because of what they do. Compounding that with terse naming makes the code
unmaintainable by the person who wrote it three months later.

### VII. Fail Loud to the Log, Soft to the User

Every external dependency — each provider call, the capture APIs, the audio device —
MUST have explicit failure handling. Unhandled exceptions MUST NOT be permitted to
terminate the process or leave the application in a state where the hotkey no longer
responds.

Failures MUST be logged with enough context to diagnose them, and surfaced to the
user as a short, non-technical statement of what did not work. Silent failure — where
the user presses the hotkey and nothing happens, with no indication why — is a defect
of the same severity as a crash.

**Rationale:** This application has no window the user is looking at when it fails.
A silent failure is indistinguishable from the app being broken or uninstalled, and
it is the most likely support burden the project will carry.

### VIII. Actions Are Opt-In, Ordered, and Bounded

This principle governs every feature where Winly changes the state of the user's
machine rather than only observing it and speaking. It applies in addition to
Principle II, which governs only what Winly is allowed to see and hear.

Acting MUST be a separate opt-in from screen and microphone capture. Granting Winly
sight or hearing MUST NOT grant it hands, and the setting MUST persist and be
revocable at any time.

Where a single request produces more than one action, those actions MUST run in the
order the model declared, MUST stop at the first failure rather than continuing past
it, and the user MUST be told which actions completed and which did not. A sequence
MUST be bounded in count, and every action within it MUST be bounded in wall-clock
duration; exceeding either bound stops the sequence and reports it.

Any action that is irreversible, destructive, or externally consequential — closing a
window that may hold unsaved work, putting the machine to sleep or locking it,
sending text into whatever currently holds focus, deleting or overwriting anything —
MUST be stated in plain language and explicitly confirmed before it runs. Confirmation
is per action and per occurrence; it MUST NOT be remembered across sessions.

Every action Winly performs MUST be recorded — what it was, what it targeted, whether
it succeeded, and when — in a record the user can review independently of whatever was
spoken at the time.

Winly MUST NOT run elevated and MUST NOT attempt to escalate in order to act on an
elevated target. Where the operating system refuses an action, that refusal MUST be
reported plainly rather than retried silently.

**Rationale:** Windows asks the user's permission for none of this. Synthetic input
needs no consent prompt, no entitlement, and no capability declaration — so every
limit here is one the project imposes on itself, because nothing else will. The
specific risk is concrete rather than theoretical: the existing verb set can type
arbitrary text into whichever window happens to hold focus, close an application
holding unsaved work, and lock the machine. Chaining multiplies the blast radius of
a single misheard sentence, which is precisely why ordering, stopping, bounding, and
confirmation arrive in the same principle that permits chaining at all.

## Additional Constraints

**Platform target.** Windows 10 version 1903 (build 18362) or later, x64. Features
requiring newer APIs MUST degrade gracefully rather than block startup.

**Per-monitor DPI correctness.** The application MUST declare per-monitor DPI
awareness and MUST perform all screen-coordinate math in an explicit, documented
coordinate space. Any conversion between capture-pixel space and desktop-coordinate
space MUST be covered by unit tests, including a mixed-scale-factor multi-monitor case.

**Resource behavior at idle.** When idle — no hotkey held, no request in flight — the
application MUST NOT hold the microphone open, MUST NOT capture frames, and MUST NOT
consume measurable CPU. An always-resident companion that drains battery gets uninstalled.

**Provider substitutability.** Chat, speech-to-text, and text-to-speech MUST each sit
behind an interface with at least the possibility of a second implementation. No
vendor-specific type may appear in the core.

**Actuation capability ladder.** When Winly acts on something, it MUST use the highest
available rung of this ladder, and MUST NOT drop to a lower one where a higher one
exists:

1. A native or service API for the thing being controlled — the media transport, the
   audio session, the shell, an application's own web API.
2. Invocation of a named element through the accessibility tree.
3. Synthetic keyboard input aimed at a focused window.
4. Coordinate-based mouse input derived from a screenshot.

Each rung down is less reliable, harder to verify, and more expensive to run. Rung 1
either succeeds or returns an error the application can act on; rung 4 cannot tell a
successful click from one that landed on nothing. A plan that reaches for a lower rung
where a higher one exists MUST justify it in Complexity Tracking.

## Development Workflow

Branches follow `feature/<short-description>` or `fix/<short-description>`. Commits use
imperative mood and explain why the change exists rather than restating the diff.

Every PR MUST state which principles it touches and confirm compliance. PRs that add a
dependency MUST record the dependency's license in the PR description. PRs that add
P/Invoke MUST confirm the declaration lives in the platform layer.

Core logic changes MUST ship with tests. Platform-layer changes, which cannot be tested
in CI, MUST document the manual verification performed, including the Windows version
and monitor configuration it was verified on.

## Governance

This constitution supersedes conflicting practice elsewhere in the project. Where a
plan, spec, or task list conflicts with it, the constitution wins and the other
document is corrected.

Amendments require: a written statement of the change and its justification, an
assessment of what existing code or documents become non-compliant, and a migration
note for any non-compliant work. Amendments are versioned semantically — MAJOR for
removing or redefining a principle in a backward-incompatible way, MINOR for adding a
principle or materially expanding guidance, PATCH for clarifications that do not change
what is permitted.

Compliance is reviewed at each milestone completion, not continuously. A milestone MUST
NOT be marked complete while a known violation stands unremediated and unjustified.

Complexity that violates a principle MUST be justified in writing in the plan's
Complexity Tracking table, including what simpler alternative was rejected and why.
"It was faster" is not a justification.

**Version**: 1.2.0 | **Ratified**: 2026-09-12 | **Last Amended**: 2026-09-13
