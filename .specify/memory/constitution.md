<!--
Sync Impact Report
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

**Version**: 1.0.0 | **Ratified**: 2026-09-12 | **Last Amended**: 2026-09-12
