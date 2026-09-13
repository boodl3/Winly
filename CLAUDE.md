# Winly — Project Context for Claude

This file exists so a new Claude session can get oriented without re-reading every
spec/plan/tasks file from scratch. Details still live in their source files — this is
a map and a summary of decisions, not a duplicate of the content.

## What Winly is

A Windows desktop AI companion with no main window. The user holds a key, speaks a
request, and Winly answers aloud using the current screen as context — pointing at
on-screen elements, and carrying out desktop actions, when the request calls for it. See
[.specify/memory/constitution.md](.specify/memory/constitution.md) for the seven
non-negotiable principles (secrets never in the client, consent before capture,
clean-room originality, vertical slices, interop quarantined, clarity over cleverness,
fail loud/soft).

## Current status (2026-09-13)

`Winly.sln` builds with zero warnings, **143 unit tests pass** (`dotnet test Winly.sln`),
the Worker typechecks and is deployed at
`https://winly-backend.boodle-doobee.workers.dev`.

**`001-core-companion-loop`** is specified and implemented (`/speckit-implement` ran
2026-09-12): the voice-in → screen-context → spoken-answer → pointing loop, demo-phase
companion (square body, two black eyes, no external art). Seven manual-verification tasks
remain open (T062, T075, T076, T079–T082) with rows in `tests/manual-verification.md`.

**A large amount of capability has landed on top of 001 and is not in spec.md or
tasks.md.** It is described below and in the contracts, but it has never been through
`/speckit-specify`, and it is now big enough to deserve its own feature. That is the main
piece of process debt in the repo.

| Artifact | Path | Status |
|---|---|---|
| Spec | [specs/001-core-companion-loop/spec.md](specs/001-core-companion-loop/spec.md) | Complete for 001; **silent about everything since** |
| Plan | [specs/001-core-companion-loop/plan.md](specs/001-core-companion-loop/plan.md) | Complete for 001 |
| Research | [specs/001-core-companion-loop/research.md](specs/001-core-companion-loop/research.md) | Complete (9 unknowns resolved) |
| Data model | [specs/001-core-companion-loop/data-model.md](specs/001-core-companion-loop/data-model.md) | Complete for 001 |
| Contracts | [specs/001-core-companion-loop/contracts/](specs/001-core-companion-loop/contracts/) | **Current** — worker-api.md tracks the live routes |
| Quickstart | [specs/001-core-companion-loop/quickstart.md](specs/001-core-companion-loop/quickstart.md) | Complete for 001 |
| Tasks | [specs/001-core-companion-loop/tasks.md](specs/001-core-companion-loop/tasks.md) | 82 tasks; 75 done, 7 manual-verification open |

Toolchain note: the .NET 10 SDK on this machine is a user-local install at
`%LOCALAPPDATA%\Microsoft\dotnet` (not on PATH); prefix commands with that directory and
set `DOTNET_ROOT` to it before launching `Winly.exe`. Wrangler needs
`CLOUDFLARE_ACCOUNT_ID=472f7c7eec4af8d90f722f71c6343694` for anything beyond `deploy`
(creating a KV namespace fails with a bare "Authentication error" without it).

### What works, and what is waiting on setup

Verified live against the deployed Worker: all eleven action verbs are emitted correctly,
both escape hatches work in both directions, the music routes degrade correctly without
credentials, and token counts are as documented below. Verified on this machine: the
brightness WMI call, and app-name matching against the real process list.

**Blocked on the user:** `SPOTIFY_CLIENT_ID` / `SPOTIFY_CLIENT_SECRET` are not set, so
nothing that needs the Spotify account works — playing a named song, and moving Spotify's
own volume. Everything degrades to the Windows mixer and a `spotify:search:` link instead
of failing. Setup steps are in `worker/README.md`.

**Never verified:** most client-side action execution on real hardware — window snapping
at non-100% scaling, typing, clipboard, the radio toggles, timers firing. Rows are
prepared in `tests/manual-verification.md`.

## What Winly can do

The companion loop from 001, plus an action system layered on the same channel.

**Eleven verbs**: `open`, `play`, `queue`, `media`, `volume`, `window`, `system`, `type`,
`clipboard`, `openpath`, `timer`. Shapes and examples are in the Worker's system prompt
(`worker/src/index.ts`) and the contract (`contracts/worker-api.md`); the authoritative
validation is `Winly.Core.Actions.DesktopActionParser`.

**Actions ride the designation channel, not tool use.** The model emits `@@DO {…}@@`
alongside `@@POINT …@@`; the Worker lifts it into the `done` SSE event; the client carries
it out *while the answer is still being spoken*. A tool-use round trip would put a second
model call between the question and the answer, which is the whole latency budget.

**The trust boundary is in `WindowsDesktopActions`, not the prompt.** An app name is
resolved against what is actually running or installed and launched with **no arguments**;
a URL opens only when its scheme is http or https; a path is searched for under the user's
own profile; player URIs are built client-side from a spoken name. No model text ever
reaches a command line. `UserSettings.DesktopActionsEnabled` (panel checkbox, default on)
turns the whole system off.

**Deliberate omissions.** Nothing irreversible: no emptying the recycle bin, no deleting,
no security settings. And `type` sends plain Unicode characters only — it structurally
cannot press a modifier combination, so dictation can never become "press
ctrl+shift+whatever in whatever happens to be focused".

**Music goes through the user's own Spotify account.** `GET /spotify/login` and
`/spotify/callback` do the authorization-code round trip in the browser; the refresh token
lands in the `MUSIC_TOKENS` KV namespace keyed by `UserSettings.InstallId`, and the client
holds nothing but that opaque GUID (Constitution Principle I). Playback control is
Premium-only on Spotify's side.

## How the expensive parts are kept cheap

Measured through the Worker's `chat.usage` log (`npx wrangler tail`), never estimated.
Steady-state, cache warm, one 1356x848 display:

| Turn | Fresh input | Cached | Output |
|---|---|---|---|
| Command ("turn it down a bit") | 121 | 2,446 | 32 |
| Screen question | 1,610 | 2,446 | 90 |

Four things get it there, and each is easy to undo by accident:

- **The system prompt is cached with a 1 hour TTL.** Caching is silently ignored below
  1024 tokens, and the prompt sat just under that for a long time doing nothing at all. If
  you shorten it, check `cacheRead` is still non-zero on a second request in a row.
- **Screenshots are capped by pixel area (1.15 MP), not just longest edge.** Above that
  the vision model bills for detail it cannot use.
- **Screenshots are attached only when the wording calls for them**
  (`ScreenContextHeuristic`). They are the entire non-cached cost of a turn.
- **The web search tool is attached only when the wording calls for it**
  (`WebSearchHeuristic`). Its definition alone is ~2,700 input tokens — more than this
  entire system prompt — billed on every turn it is attached to whether or not a search
  happens. Measured: 2,446 cached tokens without it, 5,084 with.

Both heuristics are conservative and both are allowed to be wrong: the model replies
`@@NEEDSCREEN@@` or `@@NEEDWEB@@` and the orchestrator asks once more with whatever was
requested. **A miss costs one round trip, never a wrong answer.** Reuse this pattern if a
third expensive attachment ever appears.

The screenshots are captured at key-down regardless, in parallel with listening — so the
retry costs a round trip and not another capture, and JPEG encoding stays off the critical
path. Key release to first spoken word measures 1.9–3.2 s against SC-001's 4 s median.

Also: only the last six exchanges travel with a question (`ConversationSession.Recent`);
the session still keeps all of them in memory, so FR-013 is unchanged.

## Architecture, at a glance

Four .NET 10 client projects (Constitution Principle V enforced via project reference
direction, not just convention):

- `Winly.Core` (`net10.0`, no Windows types) — state machine, coordinate math, designation
  parsing, the two context heuristics, the reminder scheduler, all interfaces. Fully
  unit-testable and where nearly all 143 tests live.
- `Winly.Platform` (`net10.0-windows10.0.22621.0`, `SupportedOSPlatformVersion` 19041) —
  all P/Invoke, WinRT, D3D, WASAPI, WMI. Not unit-testable except the settings store;
  verified manually per `tests/manual-verification.md`. `Actions/` holds `AppMatcher`,
  `WindowControl`, `SystemControl`, `UserInputControl`, `NativeDesktopMethods` and
  `WindowsDesktopActions`.
- `Winly.Providers` (`net10.0`) — HTTP/WebSocket clients to the backend proxy, including
  `Music/ProxyMusicService`.
- `Winly.App` (`net10.0-windows10.0.22621.0`) — WPF composition root, tray, overlay,
  settings panel. Composition is hand-wired in `App.xaml.cs` (no DI container); the
  orchestration loop itself is `Winly.Core.Companion.CompanionOrchestrator`, so it is
  unit-tested with fakes.

Plus `worker/` — a Cloudflare Worker (TypeScript) holding every third-party credential
server-side (Constitution Principle I):

| Route | Purpose |
|---|---|
| `POST /chat` | SSE; Anthropic `claude-sonnet-5`, vision, optional web search tool |
| `POST /tts` | ElevenLabs `eleven_flash_v2_5`, streamed as headerless PCM |
| `POST /transcribe-token` | Short-lived AssemblyAI token for a direct client websocket |
| `POST /music/{status,state,play,transport,volume}` | The user's Spotify account |
| `GET /spotify/{login,callback}` | The only browser-facing routes; OAuth round trip |

Bindings: `MUSIC_TOKENS` KV (`ad0f2161eabb4ca3a0f26bfc4aa4f288`). Non-secret vars live in
`wrangler.toml`. Full file-by-file client layout: plan.md's "Project Structure" section.

## Deploying the backend (hard-won lessons)

Deploy with `cd worker && npx wrangler deploy`. Provider keys are Worker secrets, never in
the client. Two traps cost hours — check both before concluding a key is wrong:

- **A secret write often does not reach production.** `wrangler secret put` and dashboard
  edits create a new Worker *version*, but it is frequently never promoted to the active
  *deployment* — live traffic keeps serving the old value, so the change looks like it
  silently did nothing. Compare `npx wrangler versions list` (look for "Updated secret: X")
  against `npx wrangler deployments list` (what is at 100%), and promote when they differ:
  `npx wrangler versions deploy <version-id>@100% --yes`. Allow ~15-20s for edge
  propagation before testing.
- **Never paste a secret with Ctrl+V at wrangler's masked prompt.** On Windows it can
  insert a literal `0x16` SYN control character instead of pasting, storing that one byte
  as the key. The resulting HTTP header is malformed, so upstreams reject it at the
  protocol layer — AssemblyAI answers with a bare `400` and an empty body, which never
  mentions credentials and reads like a server-side outage. Use the Cloudflare dashboard,
  or a file: `wrangler secret put KEY < key.txt`.

Also note: `CHAT_PROVIDER_API_KEY` must be a **workspace-scoped** Anthropic key. An
org-level key is rejected with a request for an `anthropic-workspace-id` header.

Verify each route directly with `curl` rather than through the client — the client
collapses every backend failure into one generic spoken message by design (FR-031), so it
cannot tell you which provider broke.

## Key decisions worth remembering

- **Default activation key**: Windows key + Left Alt (held together) — chosen in
  clarification, not an industry default; configurable in settings.
- **Max hold duration**: 30 seconds, then auto-cutoff (FR-004).
- **Second instance**: shows an "already running" notification and exits immediately; does
  not hand off to or communicate with the running instance.
- **Screen capture minimum OS build**: `CreateForMonitor` needs Windows 10 build 19041
  (2004), not 18362 (1903). 18362 is the floor for the app, 19041 for per-monitor capture.
- **Companion visuals**: demo phase only — square body, two black `Ellipse` eyes, XAML
  only, no external assets. Final visual identity is a separate future spec.
- **Session memory**: conversation history is in-memory only, discarded on restart — never
  written to disk (FR-014, FR-029). Timers are the same.
- **Actions ride the designation channel, not tool use.** Re-litigate only if an action
  ever needs its result fed back to the model.
- **Music goes through the user's account, not through Windows.** The Windows mixer can
  only attenuate what an app already sends; it cannot start a track or move the app's own
  slider. Anything that needs an app to *do* something needs that app's API.
- **Playback control goes through the system media transport, never synthetic media keys.**
- **Expensive attachments are opt-in per request, with the model able to ask.**
- **The install id is a bearer token.** Anyone with the Worker URL and an install id can
  control that Spotify account.

## Bugs that cost time, and would again

Every one of these looked like a dead feature and was actually something narrower. The
shared lesson: **the log said what happened; assuming was always slower than reading it.**

- **Synthetic media keys silently do nothing** unless the target app happens to hold the
  global media hotkey. `keybd_event` reports success either way. Use
  `GlobalSystemMediaTransportControlsSession`, which says whether the command was accepted.
- **A process's name is rarely what a person calls it.** Edge is `msedge`, VS Code is
  `Code`, Zen Browser is `zen`. Matching on process name alone makes every window verb look
  broken. `AppMatcher` scores process name, the file description (the vendor's own name for
  the app) and the window title.
- **The Windows mixer only has an entry for an app that is actively producing sound.** A
  paused Spotify has no audio session, so per-app volume reports "isn't playing anything"
  — which is true and useless. Say what would fix it.
- **"Not configured" and "the backend is down" look identical in a log that says neither.**
  The app now logs its resolved backend at startup for exactly this reason.
- **A process started from a stale environment does not see a newly-set user variable.**
  `ProxyEndpointOptions` falls back to reading the user-scope value directly, because
  "launch from an already-open terminal" is the normal case, not an edge case.
- **Editing scripts corrupt escape sequences.** A Python patch script turned
  `@"\\.\root\WMI"` into a string containing a literal carriage return, and brightness
  failed with a message blaming the monitor. When a fix does not take, read the bytes
  (`cat -A`) before believing the code.

## Known issues and open risks

- **Two Worker secrets have live API keys as their *names*** (`sk_109c27b9…` ElevenLabs,
  `sk-ant-api03-cFwY40rf…` Anthropic) — someone pasted a key into wrangler's name prompt.
  Secret names are printed in full by `wrangler secret list` and visible in the dashboard.
  Both should be rotated and the junk secrets deleted.
- **The Worker is unauthenticated.** Anyone who learns the URL can spend the provider
  credits, and anyone who also learns an install id can control that Spotify account. Both
  want fixing together before any public release.
- **Spec drift**: everything in "What Winly can do" and "How the expensive parts are kept
  cheap" is undocumented in spec.md and tasks.md. See Current status.
- The six findings from `/speckit-analyze` (2026-09-12) were all remediated the same day;
  no open findings remain from that pass, but it predates all of the above.

## Where to look before making changes

- **Changing scope or requirements** → spec.md first, then propagate to plan/tasks.
- **Changing architecture or tech choices** → plan.md + research.md (research.md has the
  "why", including alternatives already rejected — check it before re-litigating a
  technology choice).
- **Adding/reordering implementation work** → tasks.md; it is organised by milestone
  (M0-M7) per Constitution Principle IV, not by flat requirement order.
- **"Why does X work this way"** → research.md's Decision/Rationale/Alternatives entries,
  then "Key decisions" and "Bugs that cost time" above.
- **An action misbehaving** → `%LOCALAPPDATA%\Winly\logs\` first. Every action logs what it
  did or why it could not, and the answer is almost never "the build is stale".
- **Constitution conflicts** → the constitution wins; fix the spec/plan/tasks, not the
  principle (amending the constitution itself is a separate, explicit action).

## Workflow reminder

This project uses Spec Kit (`.specify/`, `.claude/skills/speckit-*`). The pipeline run so
far: `/speckit-constitution` → `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
`/speckit-tasks` → `/speckit-analyze` → `/speckit-implement` (2026-09-12). Nothing is
pending for 001 beyond the manual hardware pass. The natural next step is a fresh
`/speckit-specify` for the desktop-action system, which currently exists only in code.
