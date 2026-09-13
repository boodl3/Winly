# Winly — Project Context for Claude

This file exists so a new Claude session can get oriented without re-reading every
spec/plan/tasks file from scratch. Details still live in their source files — this is
a map and a summary of decisions, not a duplicate of the content.

## What Winly is

A Windows desktop AI companion with no main window. The user holds a key, speaks a
request, and Winly answers aloud using the current screen as context — pointing at
on-screen elements, and carrying out desktop actions, when the request calls for it. See
[.specify/memory/constitution.md](.specify/memory/constitution.md) (**v1.2.0**) for the
eight non-negotiable principles (secrets never in the client, consent before capture,
clean-room originality, vertical slices, interop quarantined, clarity over cleverness,
fail loud/soft, and actions opt-in/ordered/bounded) plus the actuation capability ladder.

## Current status (2026-09-13)

`Winly.sln` builds with zero warnings, **280 unit tests pass** (260 Core + 20 Providers), the
Worker has **10 of its own** (`cd worker && npm test`, vitest, over `DesignationSplitter` and the auth gate),
and **the Worker is deployed and live** at
`https://winly-backend.boodle-doobee.workers.dev` (version `69917728`, at 100%; `cacheRead`
confirmed at 2,525 on the deployed prompt when 002 went out as `a1ff0299`). Multi-action chaining
is on the wire, and no longer only two deep: one hold saying "open spotify, put on some jazz,
throw chrome on the left, turn dark mode on and dim the screen a bit" came back from the live
Worker as **six** actions in order — it inserted `open Chrome` itself before the snap, which is the
prompt's "an app has to be opened before it can be asked to do anything" rule doing its job.

**Three things changed on 2026-09-13 after the first real multi-job session.** Both halves are
live: the Worker is deployed at `69917728` and the client was rebuilt and relaunched at 22:29
(`Backend … token present`, zero warnings). The per-request action cap went from five to ten with
the prompt told to write one designation per job rather than merging or dropping them; a `click`
verb was added so a video or a link on screen can actually be pressed, through the accessibility
tree rather than vision; and `open` on an already-running, minimised app now genuinely takes the
foreground. The trigger for the second was "play the first Procreate video" being answered with
"that should be up now on Edge" while nothing at all had been asked to happen — see "Bugs that
cost time" below, because it needed fixes in two separate files and either alone leaves it broken.

**None of the three is verified on hardware yet.** What is verified is the wire: the six-action
sequence above, and "play the first Procreate video" now answering `needsScreen: true` instead of
inventing one. The client-side halves — the click actually landing on the right row, the
foreground fallback, ten actions surviving a real sequence — have rows in
`tests/manual-verification.md` and nothing behind them.

**The companion now follows the cursor, and is visible by default.** Asked for on 2026-09-13,
after the three fixes above: the mascot no longer parks in the bottom-right corner and no longer
hides between requests. It tracks the pointer every rendered frame, on whichever monitor the
pointer is on, and is shown while idle. It also shrank — 72x80 DIP to **28.8x32**, just under
cursor size — and trails 46x40 DIP behind the hotspot so it does not sit on what is being pointed
at. (Both were taken down a further step on 2026-09-13: smaller and further back, on request.)
Built clean and relaunched; what it costs is measured, what
it looks like in motion is not — the `Follow —` rows in `tests/manual-verification.md`.

**Speech recognition was retuned on 2026-09-13** after a real miss — "pin Claude to the left"
came back as "pinch Claude. To the left." and got answered as a question about the screen. Three
separate things had to be true for that one sentence to fail, and all three are fixed; see
"Speech recognition" below. Both halves are live as of the 22:29 rebuild and the `69917728`
deploy, so the manual STT rows at the bottom of `tests/manual-verification.md` are now runnable.

**Four fixes went in later on 2026-09-13, from a second real session.** All four are client-side;
the Worker was not touched and did not need to be (`click` and `volume` were already in the live
prompt — what was wrong was how the client carried them out). Built clean, 280 tests green, and the
running process was confirmed to have loaded the new assemblies rather than assumed to:

- **The spoken confirmation had never worked once.** "Close Chrome — are you sure?" answered "yes"
  left Chrome open, every time, because the 5 s window cancelled the *transcription* instead of
  closing the microphone. See "Bugs that cost time"; it is the most disguised bug in this project
  so far, because a cancelled transcript is indistinguishable from the silence that FR-012b
  correctly treats as a refusal.
- **Action failures are now spoken, not only posted.** This is the whole of "it says it's playing
  the video but it doesn't" — the answer claims the action before it runs, deliberately, and the
  contradiction was going to a panel nobody has open.
- **"Turn Spotify down" moves Spotify's own slider**, through UI Automation, with the Windows mixer
  demoted to last resort. The mixer was never the thing being asked for.
- **`click` falls back to the window the user was looking at** when the app the model named has no
  window, which is what happens whenever it reads "Edge" off a screenshot of some other browser.

Also on request: the companion shrank again (36x40 DIP to **28.8x32**) and now trails 46x40 behind
the pointer instead of 32x28.

**The Worker is no longer open to anyone who learns the URL.** Every POST needs
`Authorization: Bearer <WINLY_CLIENT_TOKEN>`, checked once above the routing table in `fetch`.
Verified against the live deployment on 2026-09-13: no header and a wrong token both answer
`401 unauthorized`, a correct one reaches the route. The client reads its copy from
`WINLY_BACKEND_TOKEN` and the running app logs `token present` at startup.

**`002-desktop-actions` is specified, planned and implemented in code.** It lifts the
one-action-per-request limit and brings the action system under governance: sequences of
up to five actions, stop-on-first-failure, readiness-waiting, spoken confirmation for
irreversible actions, and a durable action record. Everything that can be verified in CI
is; the hardware pass (quickstart scenarios 1-17) is still open in
`tests/manual-verification.md`.

**`001-core-companion-loop`** is specified and implemented (`/speckit-implement` ran
2026-09-12): the voice-in → screen-context → spoken-answer → pointing loop, demo-phase
companion (square body, two black eyes, no external art). Four manual-verification tasks
remain open (T062, T076, T080, T082) with rows in `tests/manual-verification.md`.

The spec drift that used to sit here is resolved: the action system now has its own
feature (`specs/002-desktop-actions/`) with spec, plan, research, data model, contract
delta, quickstart and 60 tasks.

**001 — core companion loop** (`specs/001-core-companion-loop/`)

| Artifact | Status |
|---|---|
| spec.md / plan.md / data-model.md / quickstart.md | Complete for 001 |
| research.md | Complete (9 unknowns resolved) |
| contracts/worker-api.md | The base contract; 002 layers a delta on top of it |
| tasks.md | 82 tasks; 78 done, 4 manual-verification open |

**002 — desktop actions** (`specs/002-desktop-actions/`)

| Artifact | Status |
|---|---|
| spec.md | Complete; clarified 2026-09-13 (7 questions) |
| plan.md | Complete; Constitution Check passes with no outstanding deviations |
| research.md | Complete (R1–R5) |
| data-model.md | Complete |
| contracts/worker-api.md | **Delta to 001's contract** — `/chat` terminal event only |
| quickstart.md | Complete — 17 validation scenarios |
| tasks.md | 60 tasks; **57 done**, 3 open (T053, T057, T058 — all need real hardware) |

`/speckit-analyze` ran on 002 (2026-09-13) and its one CRITICAL finding is resolved: the
per-action time bound conflicted with Principle VIII's wall-clock requirement, so the
constitution was amended to v1.2.0 rather than the deviation being carried.

Toolchain note: the .NET 10 SDK on this machine is a user-local install at
`%LOCALAPPDATA%\Microsoft\dotnet` (not on PATH); prefix commands with that directory and
set `DOTNET_ROOT` to it before launching `Winly.exe`. Wrangler needs
`CLOUDFLARE_ACCOUNT_ID=472f7c7eec4af8d90f722f71c6343694` for anything beyond `deploy`
(creating a KV namespace fails with a bare "Authentication error" without it).

### What works, and what is waiting on setup

Verified live against the deployed Worker: all twelve action verbs are emitted correctly,
both escape hatches work in both directions, the music routes degrade correctly without
credentials, token counts are as documented below, a five-job utterance comes back as six
ordered actions, and a request naming something on screen asks for the screen rather than
answering without it. Verified on this machine: the brightness WMI call, and app-name matching
against the real process list.

Verified in CI: sequence ordering, the action cap, stop-on-failure, per-action
bounds, cancellation at action boundaries, consequential classification (exhaustive over
every verb, including which click labels need confirming), which on-screen element a spoken
label means, spoken-answer parsing, the settings gate, and the action record's cap,
redaction, clearing and crash tolerance.

**Playing a song no longer needs the Spotify account at all.** The user declined to link one
("Winly should be able to do tasks for me as if it was an intern I hired"), and the desktop app is
already signed in, so `play` now drives Spotify's own window through UI Automation — see "Driving
another app's window" below. `SPOTIFY_CLIENT_ID` / `SPOTIFY_CLIENT_SECRET` remain unset. That still leaves
**`queue`** with no path at all — it degrades with a plain spoken message rather than failing —
but **no longer the volume**: since 2026-09-13 `volume` drives the slider in Spotify's own window
through the same UI Automation path, so the only thing the account would still buy is `queue`.

**Deployed 2026-09-13.** 002's Worker changes are live, so the client now receives the `actions`
array rather than one action per request.

**Verified on hardware 2026-09-13:** `play` end to end against the real Spotify window — five
spoken-style queries, right artist every time, exact track on three (see "Driving another app's
window"). Also the cold-connect fix, measured at 43,441 ms before and 1,492 ms after.

**Never verified on hardware:** most other client-side action execution — window snapping at
non-100% scaling, typing, clipboard, the radio toggles, timers firing — plus everything
002 added: readiness-waiting against a real Electron app, the spoken confirmation round
trip, focus verification, and the record surviving a restart. Everything added on 2026-09-13 is
in the same state: `click` landing on the right element in a real browser, the `AttachThreadInput`
foreground fallback, and a ten-action sequence actually running to the end. So is everything from
the second session that day: a spoken "yes" actually closing the app, a failure being *heard*
rather than posted, Spotify's own slider visibly moving (and not the progress bar), and the
click fallback to the foreground window. Rows are prepared in
`tests/manual-verification.md`, and the 20-request benchmark the success criteria are
measured against is written out in `tests/action-benchmark.md`.

**The benchmark itself was stale until 2026-09-13 and would have scored a false failure.** It
still encoded the old five-action cap — SC-008 read "no sequence exceeds 5 actions" and run #20
was a six-action request expecting a stop at five — so against the current cap of 10 it would have
completed all six and been recorded as blowing a criterion that no longer exists. SC-008 now says
10 and #20 is an eleven-job request that actually reaches the cap. **Whatever moves
`MaxActionsPerRequest` has to move this file too**; it is the third place the cap is written down,
after the runner and the Worker's prompt.

## What Winly can do

The companion loop from 001, plus an action system layered on the same channel.

**Twelve verbs**: `open`, `play`, `queue`, `media`, `volume`, `window`, `system`, `type`,
`clipboard`, `openpath`, `click`, `timer`. Shapes and examples are in the Worker's system prompt
(`worker/src/index.ts`) and the contract (`contracts/worker-api.md`); the authoritative
validation is `Winly.Core.Actions.DesktopActionParser`.

**Actions ride the designation channel, not tool use.** The model emits `@@DO {…}@@`
alongside `@@POINT …@@`; the Worker lifts them into the `done` SSE event; the client carries
them out *while the answer is still being spoken*. A tool-use round trip would put a second
model call between the question and the answer, which is the whole latency budget.

**One request may carry up to ten actions** (002; raised from five on 2026-09-13 because one
hold is one utterance and people list several jobs in it). They run sequentially in declared
order and stop at the first failure; `DesktopActionSequenceRunner` owns ordering, the
count cap, per-action time bounds, readiness-waiting and outcome collection, so it is
unit-tested headless. There is **no aggregate time bound** — each action is bounded
individually (60 s for a readiness wait, 10 s otherwise), which is Principle VIII as
amended in constitution v1.2.0.

**Irreversible actions are confirmed aloud.** Closing an app, locking or sleeping, and any
`type` are stated out loud; Winly then waits for the prompt to finish playing, reopens the
microphone for 5 seconds, and treats anything that is not a clear yes — silence included —
as a refusal. Waiting for playback to finish is load-bearing: capturing earlier transcribes
Winly asking the question.

**That loop answered "no" to everything until 2026-09-13**, including a clear spoken yes, and had
since it was written. The 5 s window was a `CancelAfter` on the token handed to `Transcribe`, and
the transcriber does not finalize a turn until the **audio stream ends** — so the window arrived
and killed the request with the answer still unread. The window now ends the *capture*
(`StopCapture`, exactly what releasing the key does on a normal turn) and the transcript is awaited
under a separate 8 s grace. `SpokenConfirmationLoopTests` pins both halves.

**The companion follows the cursor.** Not a corner mascot: `CompanionOverlayHost` subscribes to
`CompositionTarget.Rendering` and eases the buddy toward the pointer every frame, idle included.
The easing is an exponential approach against the **real frame delta**
(`1 - exp(-FollowRate * dt)`), so the motion is identical at 60 Hz and 165 Hz and a dropped frame
does not make it lurch. Crossing a monitor boundary hands it to that monitor's overlay window at
the cursor rather than flying it across the gap. Pointing still wins: `PointTo` suspends the
follow, the Bézier arc runs, and `ReturnToRest` releases the hold so the buddy glides back —
there is no rest position any more, "rest" is wherever the pointer is.

Measured on this machine: **0.30 CPU-seconds per 10 s** idle — about 3% of one core — for a
companion that is now animating continuously instead of sitting collapsed. The idle storyboard
(body bob, blink) was already keeping WPF rendering, so the follow adds arithmetic per frame
rather than the wakeups. Worth re-measuring if the per-frame work ever grows past arithmetic.

**It says when it is thinking.** The companion enters `Working` the moment the microphone
closes — before the transcript comes back, not after — because waiting on the transcript is the
longest silent step of a turn, and leaving `Listening` up shows a capture badge for something no
longer being captured (FR-027). The `Working` animation is deliberately the loudest of the four
states: it is the only one with nothing to hear, so a subtle one is indistinguishable from a hang.

**Every attempt is recorded** to `%LOCALAPPDATA%\Winly\actions.jsonl`, capped at 500
entries, reviewable and clearable from the panel. Metadata only — never the text of a
`type` action, never clipboard contents, never anything captured.

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

**Playing a song drives Spotify's window, not its API.** That is the live path and it needs no
account linking at all — see "Driving another app's window" below.

**`click` presses anything the model can read off the screen** — a video on a results page, a link,
a button, a row — through the same accessibility tree `play` uses, not through vision or synthetic
mouse input. The model copies the label off the screenshot; `ScreenElementMatcher` (Core, tested)
decides which element that is and `ScreenClickControl` (Platform) invokes it. It never falls back
to "the first plausible thing": no match means no click and the user is told, because pressing the
wrong control on someone's screen is not recoverable the way playing the wrong song is. A label
that reads destructively (delete, buy, send, unsubscribe, …) is confirmed aloud first.

**The account path still exists behind it, unlinked.** `GET /spotify/login` and
`/spotify/callback` do the authorization-code round trip in the browser; the refresh token
lands in the `MUSIC_TOKENS` KV namespace keyed by `UserSettings.InstallId`, and the client
holds nothing but that opaque GUID (Constitution Principle I). Playback control is
Premium-only on Spotify's side. If it is ever linked, `Play` prefers it — it is exact where
the UI path is approximate — and `queue` plus Spotify's own volume slider start working.

## Driving another app's window (the `play` and `volume` verbs)

Added 2026-09-13 because the user rejected account linking outright. `WindowsDesktopActions.Play`
opens `spotify:search:<query>` and then presses the matching row's own play control through **UI
Automation** — the accessibility tree, the same interface a screen reader drives. Not screenshots:
no model round trip, no pixels, nothing to re-derive when a row moves. `System.Windows.Automation`
comes from a `FrameworkReference` to `Microsoft.WindowsDesktop.App.WPF` on `Winly.Platform` — *not*
`<UseWPF>`, which swaps in WPF's implicit-usings set and drops `System.IO` out from under the audio
code.

The split follows Principle V: `SpotifyUiControl` (Platform) walks the tree, and
`SearchResultMatcher` (Core) decides which row was meant, so the only part that is decidable
without Spotify open is the only part unit-tested.

Everything below was learned by probing the live window, and each one silently picked the wrong
song or nothing at all:

- **Match the artist, not just the title — and weight it higher.** Covers advertise the original in
  their own title ("3 Nights (8-Bit Dominic Fike Emulation)", "Bohemian Rhapsody (Originally
  Performed By Queen) [Karaoke Version]"), so on title words alone they beat the real recording,
  which spends those words on the artist field. Artist words count triple and unmatched title words
  are subtracted.
- **Drop spoken connectors.** "by" is load-bearing: it let the karaoke row match "bohemian rhapsody
  by queen" word for word.
- **There are two grids named "Search results".** The first is the music-video and artist strip and
  holds no plain songs at all, so `FindFirst` finds nothing playable and gives up while the real
  list sits below it. Enumerate them all.
- **"Music video" rows are playable and often the only thing offered.** Spotify surfaces "3 Nights"
  *only* as a music video; excluding those rows is what made "play 3 Nights" play a different
  Dominic Fike song. Audio rows win a tie, video rows still beat the wrong song.
- **The search field updates before the rows do.** Checking the box alone is not enough: acting on a
  page that has not caught up plays the *previous* request's answer, which looks exactly like the
  matcher choosing wrongly. The top-ranked-song fallback is refused until the poll window is spent.
- **A row's play control is named "Play" on some rows and "Play <title>" on others.** An exact-name
  condition silently finds nothing on half of them.
- **The right row is usually below the fold.** Filtering on `IsOffscreen` throws away the answer;
  `ScrollItemPattern.ScrollIntoView()` first, then `InvokePattern.Invoke()`.

Measured on this machine: "3 nights by dominic fike", "bohemian rhapsody by queen" and "some
radiohead" land exactly; "karma police by radiohead" gets the live cut, because the studio one is
the unlabelled "Top result" card that carries no `Song •` subtitle. That ceiling is recorded as a
`ponytail:` comment on `Describe`.

**`volume` reaches into the same window, for the same reason.** `SpotifyUiControl.TrySetVolume`
moves the slider in Spotify's own UI; the Windows mixer is the fallback behind it, not the default.
That ordering is the point — the mixer only attenuates what Spotify already sends, so it left the
slider the user was looking at exactly where it was, and it has no entry at all while Spotify is
paused, which is precisely when someone reaches for the volume. Three things learned writing it:

- **Match the slider by name, never by position.** The playback progress bar is a `Slider` too, and
  "the first slider" seeks the track instead of changing the volume — wrong in a way the user
  notices instantly. No slider named "volume" means fall back to the mixer, and the log names every
  slider it did see so the next session can fix the name rather than re-derive this.
- **Read the range off the control.** Spotify has shipped this slider as 0-1 and as 0-100. Treating
  a 0-1 one as a percentage mutes the app on every request, so percentages are mapped through
  whatever `RangeValuePattern` reports.
- **This is unverified on hardware**, unlike `play` above. The rows are in
  `tests/manual-verification.md`; the log line is `Spotify's own volume slider moved to N%`.

## The look

One theme file, `src/Winly.App/Theme.xaml`, merged in `App.xaml` and reaching every window
implicitly. Its palette is lifted from the user's own portfolio (deyab.dev) — read off the
live site's computed styles rather than eyeballed: ink `#1B1A16`, paper `#F2EFE8`, `#14141A`
card surfaces, hairline borders at 18% paper, `#3446C9` accent, 16px radius, Outfit for
display text with Segoe UI as its fallback (which is the site's own fallback, so nothing
ships).

The companion and the tray icon were already drawn in exactly those colours; only the chrome
was still Windows default. Three things are worth knowing before touching it:

- **The implicit `TextBlock` style paints every window paper-on-dark, including ones you did
  not restyle.** That is why `FirstRunDisclosureWindow` and the `ToolTip` template are in
  here too — leave a window light and its text is invisible rather than merely ugly.
- **WPF's stock chrome ignores a dark `Background`.** `Button`, `CheckBox`, `ComboBox`,
  `ListBox` and `ScrollBar` each needed a `ControlTemplate` to stop rendering a pale slab;
  setting brushes alone is not enough. That is the whole reason the file is long.
- **The panel and the disclosure are `AllowsTransparency` windows with a 12px margin** so a
  `DropShadowEffect` has room and the corners can round. Their `Width`/`Height` carry that
  24px, so the visible card is 24 smaller than the window — keep both in step if you resize.

The panel header draws the companion's face inline (paper body, ink eyes) rather than reusing
`SquareBuddyControl`, which is animated, ink-bodied and would vanish against the surface.

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
path.

Also: only the last six exchanges travel with a question (`ConversationSession.Recent`);
the session still keeps all of them in memory, so FR-013 is unchanged.

## How fast it actually is, and which third to attack next

Key release to first spoken word was 1.9–3.2 s against SC-001's 4 s median, and the log only
ever named that total — which made "can this be faster" a guess about which of three providers
to go at. `activation.latency` now carries its three stages, so it is a measurement:

    activation.latency 1644.9 ms ... (transcript 185.0 ms, first token 890.2 ms, speech 569.7 ms)

`tests/score-benchmark.ps1 -Since "<time>"` prints the medians for a run, alongside the action
record grouped into requests. **Four turns on 2026-09-13 after the change below**: total median
1,991 ms, transcript 134, first token 970, speech 960. Four turns is a smoke test, not a result —
the 20-request benchmark is the result — but the shape is already clear and worth carrying:

- **The chat round trip is the floor** and the largest stage every time. Nothing cheap is left
  there; shortening it means a client-side fast path for transport commands, which is a second
  NLU that will drift from the Worker's prompt. Deliberately not built.
- **TTS is second**, and its stage cannot start until the first sentence is complete — which is
  why the system prompt asks for a short first one. That instruction is load-bearing latency, not
  style.
- **The transcript is now the smallest.** It used to wait for AssemblyAI's `Termination` message,
  which trails the finished transcript by a round trip carrying no text. `ReceiveTurns` returns as
  soon as `Terminate` has gone out and the hold's single turn comes back formatted, and closes
  with `CloseOutputAsync` rather than `CloseAsync` — the latter waits for the peer's close frame,
  which puts the saved round trip straight back. **The shortcut is refused when there is more than
  one turn**: a pause longer than `min_turn_silence` can leave a second turn still formatting, and
  truncating someone's request is not a trade worth 200 ms.

Everything else on the path was already tuned and should be left alone: HTTP/2 with the key-down
token mint warming the connection, `HappyEyeballsConnect`, capture parallel with the hold,
per-sentence TTS overlapping playback, streaming PCM at a 150 ms buffer.

## Architecture, at a glance

Four .NET 10 client projects (Constitution Principle V enforced via project reference
direction, not just convention):

- `Winly.Core` (`net10.0`, no Windows types) — state machine, coordinate math, designation
  parsing, the two context heuristics, the reminder scheduler, all interfaces. Fully
  unit-testable and where nearly all 243 Core tests live. `Actions/` holds `DesktopAction`
  + `IDesktopActions`, `DesktopActionParser`, and from 002:
  `DesktopActionSequenceRunner` (ordering, cap, bounds, readiness, confirmation gate,
  outcomes), `ActionOutcome`, `ConsequentialActionClassifier`, `SpokenConfirmation`,
  `ActionRecordEntry` + `IActionRecord`. Plus the two matchers, which are the
  decidable-while-headless halves of driving someone else's window and so the only halves that
  are tested: `SearchResultMatcher` (which row of a music app's search results was meant) and
  `ScreenElementMatcher` (which labelled control on screen a spoken phrase means). Their scoring
  is deliberately opposite on one point — see the note in `ScreenElementMatcher` before copying
  either onto the other.
- `Winly.Platform` (`net10.0-windows10.0.22621.0`, `SupportedOSPlatformVersion` 19041) —
  all P/Invoke, WinRT, D3D, WASAPI, WMI. Not unit-testable except the settings store and
  the action record (both touch only the disk); everything else is verified manually per
  `tests/manual-verification.md`. `Actions/` holds `AppMatcher`, `WindowControl`,
  `SystemControl`, `UserInputControl`, `NativeDesktopMethods`, `WindowsDesktopActions`,
  `SpotifyUiControl` and `ScreenClickControl` (both UI Automation); `Settings/` holds
  `JsonFileSettingsStore` and
  `JsonLinesActionRecord`. Carries a `FrameworkReference` to `Microsoft.WindowsDesktop.App.WPF`
  purely for `System.Windows.Automation` — deliberately not `<UseWPF>`, which would also swap in
  WPF's implicit-usings set and drop `System.IO` out from under the audio code.
- `Winly.Providers` (`net10.0`) — HTTP/WebSocket clients to the backend proxy, including
  `Music/ProxyMusicService`, `SpeechToText/StreamingSpeechToTextProvider` (which owns the
  transcriber's key-term list, the one piece of provider tuning that is not server-side) and
  `HappyEyeballsConnect`, the `ConnectCallback` that races IPv6
  against IPv4 (without it a cold connect costs 40 s on a network with no IPv6 path).
- `Winly.App` (`net10.0-windows10.0.22621.0`) — WPF composition root, tray, overlay,
  settings panel, and `Theme.xaml` (the app-wide visual language — see "The look").
  Composition is hand-wired in `App.xaml.cs` (no DI container); the
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

Every POST route above is gated on `Authorization: Bearer <WINLY_CLIENT_TOKEN>`; the two browser
GETs cannot carry a header and stay open. The client reads its copy from `WINLY_BACKEND_TOKEN`
(user-scope env var, same fallback as `WINLY_PROXY_BASE_URL`) and sets it once on the shared
`HttpClient`.

Bindings: `MUSIC_TOKENS` KV (`ad0f2161eabb4ca3a0f26bfc4aa4f288`). Non-secret vars live in
`wrangler.toml`. Full file-by-file client layout: plan.md's "Project Structure" section.

## Deploying the backend (hard-won lessons)

Deploy with `cd worker && npx wrangler deploy`. Provider keys are Worker secrets, never in
the client. **Set a secret first and deploy second**: `deploy` promotes its version to 100% and
carries the secret with it, which is how the 2026-09-13 auth rollout sidestepped the promotion
trap below rather than having to work around it afterwards.

Three traps cost hours — check them before concluding a key is wrong:

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
- **A secret file written by a shell usually ends in a newline, and the newline is stored.**
  `echo`, `>` from most tools and `console.log` all append one, so the secret is silently one
  byte longer than the value anyone will present. Anything comparing it by equality then fails
  for a reason no log will name. Write the file without the newline
  (`node -e "process.stdout.write(...)"`), check it with `wc -c`, and compare the stored value
  against the file before deleting the file — which is what the token rollout did.

Also note: `CHAT_PROVIDER_API_KEY` must be a **workspace-scoped** Anthropic key. An
org-level key is rejected with a request for an `anthropic-workspace-id` header.

Verify each route directly with `curl` rather than through the client — the client
collapses every backend failure into one generic spoken message by design (FR-031), so it
cannot tell you which provider broke. Every POST now needs
`-H "Authorization: Bearer $(cat token.txt)"`; without it every route answers `401 unauthorized`
whatever else is wrong with the request, so a curl that suddenly 401s everywhere is a missing
header, not a dead backend. Prove the gate in both directions after any auth change: no header
and a wrong token must both be refused, and a correct one must reach the route.

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
- **The companion is visible by default** (`UserSettings.CompanionVisibilityMode`), changed from
  `InteractionOnly` when it started following the cursor: withdrawing it between requests hides
  the thing it now spends most of its time doing. Both modes still work and the panel still
  switches them; only the default moved.
- **The buddy is authored at 72x80 and scaled by one `LayoutTransform`.** Resizing it means
  changing that one factor in `SquareBuddyControl.xaml`, not twenty dimensions. WPF is vector all
  the way down, so the scale costs nothing in crispness — do not re-draw at a new size.
- **The chrome follows deyab.dev, the user's own portfolio.** Palette and radius were read
  off the live site rather than invented, and the companion already matched it. When a new
  window or control appears, take its colours from `Theme.xaml`; do not start a second
  palette.
- **Session memory**: conversation history is in-memory only, discarded on restart — never
  written to disk (FR-014, FR-029). Timers are the same.
- **Actions ride the designation channel, not tool use.** Re-litigate only if an action
  ever needs its result fed back to the model.
- **Anything that needs an app to *do* something needs that app, not Windows.** The Windows
  mixer can only attenuate what an app already sends; it cannot start a track or move the app's
  own slider. So "turn Spotify down" now goes through Spotify's **own** volume slider via UI
  Automation (`SpotifyUiControl.TrySetVolume`), with the mixer demoted to last resort — the mixer
  left the slider the user was looking at exactly where it was, and had no entry at all while
  Spotify was paused. The slider is matched **by name**: the playback progress bar is a slider too,
  and "the first slider" seeks the track instead.
- **Reach into an app through its accessibility tree before its API, and long before vision.**
  The user declined account linking outright — "Winly should be able to do tasks for me as if it
  was an intern I hired" — and an intern does not need a second copy of a credential to use a
  machine that is already signed in. UI Automation gets there with no model round trip, no pixels
  and no OAuth. Screenshots stay the fallback for apps that expose no tree, not the default.
- **The transcriber's vocabulary lives in the client, everything else about the session in the
  Worker.** Half of it is the names of the apps that happen to be open, which the Worker cannot
  know. Adding a verb to the action system means adding it to `CommandKeyTerms` too — a verb the
  transcriber has never been told about is a verb the model never gets a chance to see.
- **Playback control goes through the system media transport, never synthetic media keys.**
- **Expensive attachments are opt-in per request, with the model able to ask.**
- **The install id is a bearer token, and it is now behind a second one.** Controlling a linked
  Spotify account needs an install id *and* the shared client token, so a leaked install id
  alone is no longer enough.
- **The auth gate fails closed when its secret is unset.** Tempting to let a missing
  `WINLY_CLIENT_TOKEN` mean "auth not configured, allow through" — that is precisely the failure
  the gate exists to prevent, and the secret-promotion trap is the realistic way it would happen.
  Unset means 401, and a test pins it.
- **Never wait on the `Process` handle `Process.Start` returned.** Spotify, Discord, Teams
  and every Electron app launch a stub that spawns the real process and exits, so that
  handle is dead within a second and never owned a window. Readiness polls `AppMatcher`
  against the live process list and probes the window with `WM_NULL` — which is what
  separates a window that exists from one that is answering.
- **The Worker sends `actions` only, and does not cap it.** The singular `action` compat
  field was removed once it was clear the only client ships from this tree. The five-action
  cap lives in `DesktopActionSequenceRunner` alone: capping in the Worker too would hand the
  client exactly five and hide the fact that the request was truncated, so the user would
  never be told the rest did not happen (FR-003).

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
  failed with a message blaming the monitor. It happened again writing this file:
  `\Winly\actions.jsonl` in a non-raw Python string became `\Winly` + a `0x07` BEL byte,
  and the path silently read as `Winlyctions.jsonl`. **When a path or pattern contains a
  backslash, write it with an editor rather than a shell heredoc or a Python string**, and
  read the bytes (`cat -A`) before believing a fix took.
- **Winly's own voice answers its own confirmation prompt.** Opening the microphone before
  the spoken question has finished playing transcribes the question itself, and the likely
  tail of "Should I go ahead?" reads as agreement. `ConfirmAloud` awaits `Announce` — which
  awaits playback — before it captures. That ordering is load-bearing, not politeness.
- **A window that exists is not a window that is listening.** A splash screen, or an app
  sitting on a modal update prompt, satisfies `MainWindowHandle != 0` forever. Readiness
  probes with `SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG)`; only the reply matters.
- **.NET has no happy eyeballs, and that is a forty-second bug.** Every cold connect to the
  backend took 24-64 s while a warm one took 2 s, which read as "Winly buffers the first time I
  use it". `SocketsHttpHandler` walks the resolved addresses **in order**, and the backend
  publishes two AAAA records; on a network whose IPv6 path is dead that is a full Windows SYN
  timeout — about 21 s — *each*, before IPv4 is ever tried. 21, 42 and 63 are all over that log.
  curl and browsers never showed it because they race the families (RFC 8305). Measured against
  the real backend: **43,441 ms before, 1,492 ms after.** `HappyEyeballsConnect` is the
  `ConnectCallback`. If a future host feels slow only on its first request, suspect this before
  suspecting the provider — and note the same trap waits in any other raw socket or
  `ClientWebSocket` that does not go through this handler.
- **One misheard phoneme reads as a broken feature, and the log says which one.** "Pin Claude to
  the left" came back as "Pinch Claude. To the left.", which is not a verb, so Winly answered it as
  a question about the screen and looked like it had forgotten how to snap windows. Nothing in the
  action system was involved at all. The whole chain is in "Speech recognition" above; the habit
  worth keeping is checking the transcript in the log before suspecting anything downstream of it.
- **`SetForegroundWindow` is refused for a tray app, and the refusal looks like nothing.** Windows
  grants the foreground only to the process owning the current foreground window or one that has
  just received input. Winly's activation key comes through a global hook, so the keystroke goes to
  whatever the user was *in* — never to Winly. The call comes back having restored the window and
  flashed its taskbar button, which reads as "open did nothing" for an app that was merely
  minimised. `WindowControl.Focus` now checks `GetForegroundWindow()` afterwards rather than
  trusting the return value, and on a refusal joins the two input queues with `AttachThreadInput`
  for the length of one call — and detaches in a `finally`, because two processes sharing an input
  queue is not a state to leave behind.
- **A command word in the sentence is not proof the request is not about the screen.** "Play the
  first Procreate video" matched `play` in `ScreenContextHeuristic`, so it was classified as a music
  command and went out with **no screenshots at all**; the model, with nothing to look at, answered
  that the video "should be up now on Edge". Nothing had been asked to happen — there was no
  designation behind that sentence. Two fixes, because either alone leaves it broken: the heuristic
  now lets "video", "click" and the ordinals override a command word, and the prompt now forbids
  claiming something is done without a designation for it. **A confident sentence with no `@@DO@@`
  behind it is the failure mode to look for** — the action record and the log are both empty for
  it, which is itself the tell.
- **`CompositionTarget.Rendering` fires more than once per frame, and its handler is the UI
  thread.** Two traps in one event: pace anything in it off `RenderingEventArgs.RenderingTime`
  rather than a wall clock, and skip the tick when that time has not advanced, or a per-frame
  animation gets a zero delta and stutters. Everything in the handler runs before the frame is
  drawn, so it must stay arithmetic — `MonitorEnumerator.Enumerate()` is a dozen syscalls and
  belongs nowhere near it; `TryGetCursorPositionPx` is the one that is cheap enough.
- **A running `Winly.exe` locks `Winly.Core.dll` and the build fails with MSB3027, not a
  compile error.** Ten retries, then "the process cannot access the file" naming the PID. Stop
  the app before rebuilding; the message is about a file copy and reads nothing like stale code.
- **The confirmation loop could never have worked, and nothing said so.** "Close Chrome, are you
  sure?" answered "yes" left Chrome open. The 5 s window was a `CancelAfter` on the token passed to
  `Transcribe`, and the transcriber only finalizes a turn once the **audio stream ends** — so at the
  5 s mark the request was cancelled with the answer still unread, every time. Silence is a refusal
  by design (FR-012b), which is exactly what a cancelled transcription looks like, so the bug wore
  the correct behaviour as a disguise. The fix is to mirror the main loop: the window ends the
  *capture* (`StopCapture`, the same thing key release does), and the transcript is then awaited
  under a longer grace. **Anything that bounds a transcription must bound the capture, never the
  transcript.**
- **A fake that answers instantly cannot catch the bug above.** The first test written for it passed
  against the broken code, because `Transcribe` was stubbed to return immediately and nothing ever
  waited on the stream. A stub for this provider has to honour its real contract — no transcript
  until the audio ends — or it is asserting on nothing. Checked by reverting the fix and watching
  the tests go red; a test that has never failed is a test that has never been run.
- **A failure written to the panel is a failure nobody hears.** The spoken answer says "playing it"
  before the action runs, deliberately (it overlaps the latency), so an action that then fails left
  the user with a confident claim and a contradiction posted to a window that is not open. That is
  the whole of "it says it's playing the video but it doesn't" — usually not a lying model, just an
  unheard failure. `FailureMessages.ForSequence` is now spoken through `Announce`, not `Post`.
  **Anything that contradicts something already said out loud has to be said out loud.**
- **`SendInput` reports success when UIPI drops the keystrokes.** Typing into an elevated
  window returns a non-zero count having delivered nothing, so without a separate check the
  user is told it worked. `UserInputControl` probes the target with `OpenProcess` first —
  a process it cannot open for query is elevated, and the action is refused plainly.

## Speech recognition

One real miss drove all of this: "pin Claude to the left" arrived as **"Pinch Claude. To the
left."** and was answered as a question about the screen, with an answer assembled out of a usage
dashboard and the taskbar weather widget. Three things had to go wrong for that, in three
different files, and only the first is about speech at all:

1. The utterance was **split into two turns** at the pause, so "pin" was decoded with nothing
   after it to disambiguate — and the two finals overlapped, so joining them duplicates words.
2. `ScreenContextHeuristic` matched `on the (left|right)` but not **"to the left"**, so a window
   command was classified as a screen question and paid for screenshots.
3. The system prompt never said the transcript comes from speech recognition, so a non-verb plus
   attached screenshots left the model no option but to answer about the screen.

Fixing only the first would have left the other two waiting for the next misheard word.

### The socket

`universal-3-5-pro` (AssemblyAI's default). Five query parameters carry the tuning, and every one
is free — they are parameters, not extra requests:

| Parameter | Why |
|---|---|
| `mode=max_accuracy` | Nothing here is latency-bound at the transcriber; the audio is already captured. |
| `prompt` | Free text, Pro-only. Says these are short spoken PC commands — what you would tell a human transcriptionist before handing them the tape. |
| `voice_focus=near-field` | A desk microphone hears the room. Near-field is the profile for someone sitting at the machine, which is the only way Winly is used. |
| `min_turn_silence=10000` | Push-to-talk means **the hold is the utterance**. Measured on a clip with a 2.5 s pause: the default returns two finals whose text overlaps ("Pin Claude to the left." plus "The left."); at 10 s it stays one turn and `Terminate` still finalizes it on key release. |
| `keyterms_prompt` | Up to 100 boosted terms — **appended by the client, not the Worker**. |

`keyterms_prompt` is the only one built client-side, and for one reason: half of it is the names of
the apps that are actually open. `StreamingSpeechToTextProvider.KeyTerms` puts
`AppMatcher.WindowedAppNames` first (vendor file description before process name — nobody says
"msedge"), then the fixed command vocabulary. App names lead because the cap cuts from the end and
they are the half a general transcriber has never heard of; 40 of them plus 57 verbs stays under
100, which the test asserts rather than a `Take` silently dropping verbs off the end. The headroom
is thin — adding a verb means adding its spoken form here too, and the assert is what stops that
pushing the list over the provider's limit unnoticed.

### Traps

- **AssemblyAI accepts unknown query parameters silently.** A misspelled name does not fail the
  handshake, it just does nothing — verified by sending `nonsense_param=1` and getting a normal
  `Begin`. Only `voice_focus` and `mode` come back in the `Begin` message's `configuration` echo;
  `prompt` and `keyterms_prompt` do not, so **nothing on the wire can confirm them**. Speaking a
  command is the only proof, which is why they have rows in `tests/manual-verification.md`.
- **A short command is the worst case for any transcriber.** There is no sentence around a word to
  disambiguate a phoneme. The prompt now tells the model the transcript is speech recognition
  output: act on the nearest plain command when a request is one sound away from one, and say you
  did not catch it rather than inventing an answer. That is the safety net, not the fix — it turns
  a confident wrong answer into "say that again".
- **The client keeps partial turns.** `StreamingSpeechToTextProvider` used to record only turns
  carrying `end_of_turn`. Now that one hold is one turn, a turn the provider never finalizes is
  the whole request rather than a fragment of it, so the last partial is kept — later messages for
  the same `turn_order` overwrite earlier ones, so a final still wins when it arrives.
- **The capture itself is not a suspect.** WASAPI is asked for 16 kHz mono PCM16 directly
  (`AutoConvertPcm`), so there is no hand-rolled resampler to alias consonants. Checked before
  touching anything upstream of it.

## Known issues and open risks

- **Two Worker secrets have live API keys as their *names*** (`sk_109c27b9…` ElevenLabs,
  `sk-ant-api03-cFwY40rf…` Anthropic) — someone pasted a key into wrangler's name prompt.
  Secret names are printed in full by `wrangler secret list` and visible in the dashboard.
  Both should be rotated and the junk secrets deleted.
- **Version-picking within an artist is approximate.** `play` reliably lands on the right artist
  and usually the right song, but can pick a live or remastered cut when the studio one is only
  offered as the unlabelled "Top result" card. `queue` still has no UI path at all.
- **The Worker's auth is one shared token, and that is a known ceiling.** Every POST now needs
  `Authorization: Bearer <WINLY_CLIENT_TOKEN>`; the gate sits above the routing table in
  `fetch`, so a route added later cannot be added unprotected, and it **fails closed when the
  secret is unset** — an auth check that vanishes with its secret is the failure it exists to
  prevent, and the secret-promotion trap below is how that would happen here. The token is
  extractable from any copy of the client binary, so it closes the real exposure (a leaked URL
  letting a stranger spend the provider credits) and raises Spotify control from "learn an
  install id" to "learn an install id *and* hold the binary". It does not survive public
  distribution: that wants per-install tokens behind a real sign-in, and is the thing to build
  before the first Gmail/Calendar scope ever reaches KV.
- **Spotify readiness stops at the window.** `WaitForApplicationReady` waits until the
  window answers `WM_NULL` and goes no further. The account-side device wait that used to
  follow it was removed: it polled `IMusicService.Playing()`, which throws on the first call
  while the account is unlinked, and `play` drives the window through UI Automation anyway —
  which does its own polling for search results. Re-add a device wait only alongside a
  `/music/devices` route, since `Playing()` returns null both for "no device" and for "idle".
- The six findings from `/speckit-analyze` on 001 (2026-09-12) were all remediated the same
  day. The 002 pass (2026-09-13) found ten; all are resolved, including the CRITICAL
  Principle VIII conflict, which was closed by amending the constitution to v1.2.0.

## Where to look before making changes

- **Changing scope or requirements** → spec.md first, then propagate to plan/tasks.
- **Changing architecture or tech choices** → plan.md + research.md (research.md has the
  "why", including alternatives already rejected — check it before re-litigating a
  technology choice).
- **Adding/reordering implementation work** → tasks.md; 001 is organised by milestone
  (M0-M7) and 002 by user story (each a vertical slice), per Constitution Principle IV,
  not by flat requirement order.
- **"Why does X work this way"** → research.md's Decision/Rationale/Alternatives entries,
  then "Key decisions" and "Bugs that cost time" above.
- **Winly mishearing a command** → "Speech recognition" above, then the transcript in
  `%LOCALAPPDATA%\Winly\logs\`. The log records what the transcriber actually returned, which is
  the only thing that separates a speech miss from the model misreading a good transcript — they
  look identical from the outside, and the fix is in a different file for each.
- **Winly feeling slow** → the `activation.latency` line in `%LOCALAPPDATA%\Winly\logs\`, which
  names which of the three stages ate the turn, then "How fast it actually is" above for what is
  already tuned and what was deliberately left alone. `tests/score-benchmark.ps1` prints the
  medians over a run. **Do not optimise a stage before the log says it is the slow one** — the
  first two candidates that looked obvious were both aimed at the wrong third.
- **An action misbehaving** → `%LOCALAPPDATA%\Winly\logs\` first. Every action logs what it
  did or why it could not, and the answer is almost never "the build is stale". Then
  `%LOCALAPPDATA%\Winly\actions.jsonl`, which says what was attempted and what became of
  it; the log says why. Use both.
- **`play` picking the wrong song** → `SearchResultMatcher` for the choice (it is unit-tested,
  so reproduce it there first) and `SpotifyUiControl` for what the tree actually offered. The log
  line names the title and artist it pressed.
- **"Turn Spotify down" changing the wrong thing** → the log says which of the three paths ran:
  `Spotify's own volume now N%` is the account API, `Spotify's own volume slider moved to N%` is the
  app's own slider, and `Spotify showed no volume slider; sliders on offer were [...]` means it fell
  through to the Windows mixer and names what the tree offered instead. The mixer is the one that
  looks like nothing happened, because it leaves the app's slider where it was.
- **`click` pressing the wrong thing, or nothing** → same split: `ScreenElementMatcher` for the
  choice, `ScreenClickControl` for what the tree offered. The log says either `Pressed <element>`
  or `Nothing on screen matched <label>`, which is the whole diagnosis — the first means the
  matcher is wrong and is reproducible headless, the second means the label never reached the
  tree (wrong window, Chromium's tree still lazy, or the model paraphrased instead of copying
  the label off the screenshot).
- **Winly saying it did something it did not do** → the action record and the log first, then ask
  whether the failure was *heard*. Since 2026-09-13 a sequence failure is spoken, so a silent turn
  that did nothing means no action was ever emitted — the model claimed without a designation — and
  the fix is in the Worker's prompt, not the client.
- **A confirmation ("are you sure?") not being acted on** → `ConfirmAloud` in
  `CompanionOrchestrator`, and specifically whether anything cancels the transcription rather than
  closing the microphone. `SpokenConfirmationLoopTests` pins it; its fake waits for the capture to
  close on purpose.
- **The companion's size, position, or how the follow feels** → three tuning values, each in one
  place: the `0.4` `ScaleTransform` in `SquareBuddyControl.xaml` (size — the UserControl's own
  `Width`/`Height` are that product and must move with it),
  `CompanionOverlayWindow.CursorOffset` (how far behind the pointer it sits) and
  `CompanionOverlayHost.FollowRate` (how tightly it closes — lower is laggier). They are
  independent; "further away" and "trails more" are different knobs.
- **Anything visual — colours, spacing, a new control** → `src/Winly.App/Theme.xaml` and
  "The look" above. Windows carry layout only; the brushes, fonts and control templates all
  live in the one file.
- **Changing a time bound or the action cap** → `ActionBounds` and
  `DesktopActionSequenceRunner.MaxActionsPerRequest`. These are tuning values by design
  (FR-005b); changing them must not require changing how a sequence runs. The cap is stated in
  the Worker's prompt too, and the two have to move together: the client's is the one that
  enforces, the Worker's is the one that stops the model self-censoring a long request. And
  `tests/action-benchmark.md` is the third: SC-008 and run #20 are written against a specific cap,
  and a stale one there scores a false failure rather than failing loudly.
- **Adding a verb** → the parser, the classifier (its test is exhaustive over the enum and will
  fail until you decide), the Worker's `ACTION_KINDS` *and* its prompt, and
  `StreamingSpeechToTextProvider.CommandKeyTerms` — a verb the transcriber has never been told
  about is a verb the model never gets a chance to see.
- **Constitution conflicts** → the constitution wins; fix the spec/plan/tasks, not the
  principle (amending the constitution itself is a separate, explicit action).

## Workflow reminder

This project uses Spec Kit (`.specify/`, `.claude/skills/speckit-*`).

- **001** ran the full pipeline on 2026-09-12. Nothing pending beyond the manual pass.
- **002** ran `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks`
  → `/speckit-analyze` → `/speckit-implement` on 2026-09-13, with a `/speckit-constitution`
  amendment to v1.2.0 in the middle to resolve the analyze pass's CRITICAL finding.

**The next step is not another spec.** The Worker is deployed and gated, and the Spotify secrets
are deliberately not being set, so what is left is the hardware pass — seven tasks across both
features, all of that shape: 001's T062, T076, T080, T082 and 002's T053, T057, T058, plus the
`tests/manual-verification.md` rows the 2026-09-13 changes added (**76 rows are pending in total**;
the ones dated that day are tagged by prefix — `STT`, `Click`, `Open`, `Sequence`, `Latency`,
`Follow`, `Confirm`, `Fail loud`, `Volume`). Two of the latency rows are the only thing that will
prove the `Termination` shortcut never truncates a transcript.

Then the 20-request benchmark itself, which nobody has run: it needs a person at the microphone for
20 holds, including answering #17 "yes", #18 "no", and staying silent through #19, so it cannot be
automated and has been sitting unrun because of it. **Note what that means about #17** — until the
confirmation fix later on 2026-09-13, answering "yes" could not possibly have worked, so the
benchmark would have scored a failure there no matter how well the rest went. The one run nobody
did was the one that would have found the bug. Writing 003 before this happens would stack an
unverified feature on an unverified one. Run it against the gated backend, which is what is live.

The 2026-09-13 work went in **outside** the Spec Kit pipeline, deliberately: it was bug reports
from two real sessions — three from the first, four from the second — not a feature. The `click` verb is the part that arguably crossed
that line, and it is recorded as a down payment on 003 rather than as 003 — if it grows a second
step (scroll to find, then act on what appeared), stop and spec it.

**On connectors (Spotify, Gmail, Outlook, Calendar), asked 2026-09-13.** Spotify is settled — the
UI Automation path already does the job with no account, so adding the connector back would
re-introduce exactly the linking that was rejected, to buy `queue` and a volume slider. Calendar
and mail are worth having, but a "connector" in the usual sense means tool use, and tool use means
a second model round trip between the question and the first spoken word — the thing 001 and 002
were both designed around. **Pre-fetch and inject before reaching for tool use**: a Worker route
returning a few hundred characters of calendar or inbox context, attached the way a screenshot is
and gated by a heuristic in the same shape as `ScreenContextHeuristic`, with a `@@NEEDMAIL@@`
escape hatch. One round trip, no new channel. Tool use earns its round trip only when the user
needs arbitrary queries ("find the thread where we agreed the price"). Either way the shared
client token must become per-install tokens behind a real sign-in **before** the first mail scope
puts a refresh token in KV — one extractable token is adequate for provider credits and is not
adequate for someone's mailbox.

When 002 is verified, the natural 003 is acting on arbitrary on-screen elements rather than the
fixed verbs. It is **explicitly out of scope** for 002 and the reasoning is recorded in
002's spec.md, so that boundary does not need re-deriving. The `click` verb added on 2026-09-13 is
a deliberate down payment on it — one label, one element, one invoke — and not the feature: it
cannot scroll to find something, cannot type into what it pressed, and cannot chain a step onto
what appeared afterwards. Those are what 003 is still for.

One thing has changed since that was written, and it is worth carrying into 003: the `play` verb
shows the general capability does **not** have to mean vision. The accessibility tree handed over a
1,073-element, fully-labelled, invokable model of another app's window with no model round trip at
all — a different cost and risk profile from the screenshot-per-step loop 003 was scoped around.
Re-cost that spec against UI Automation first, and reserve vision for apps that expose no tree.
