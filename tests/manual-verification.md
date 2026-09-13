# Manual Verification Matrix — Winly.Platform

`Winly.Platform` cannot be exercised in CI (Constitution Principle V). Every platform-layer
change records the manual verification performed here, including the Windows version and
monitor configuration it was verified on. Fill a row per run; add rows rather than
overwriting history.

| Scenario | Windows version | Monitor configuration | Scaling factor(s) | Verified by | Date | Result / notes |
|---|---|---|---|---|---|---|
| Automated smoke test: launch, tray + overlay creation, simulated Win+Alt hold via `keybd_event` (1.5 s), WASAPI mic open/close, no backend configured → plain-language failure → Idle; idle CPU sampled over 10 s | Windows 11 Home 10.0.26200 | single monitor `\\.\DISPLAY1`, 1920×1200 physical | 125 % (120 DPI) | Claude (implementation session) | 2026-09-12 | PASS — log shows `Winly started`, activation ran to the expected `ProxyNotConfiguredException` and posted the FR-031 message, process stayed alive; idle CPU 0.39 % with the buddy animating (AlwaysVisible). Not a substitute for the human rows below. |
| M1 — hold activation key anywhere, buddy reacts within 150 ms (SC-002) | | | | | | pending |
| Click-through reaches the underlying app while pointing; companion never takes keyboard focus (FR-024, T062) — requires a real mixed-DPI multi-monitor rig | | | | | | pending |
| Idle: no open microphone or display-capture handle, CPU < 1 % averaged over 5 min (SC-005, FR-032, T075) | Windows 11 Home 10.0.26200.0 | single monitor `\\.\DISPLAY1`, 1920x1200 physical / 1536x960 logical | 125 % (120 DPI) | Claude (automated, session 53de2cfe) | 2026-09-13 | PASS - 300.1 s idle: 0.141 s CPU = 0.003 % of 16 logical cores (0.047 % of one core), well under the 1 % target. Microphone handle closed, confirmed via Windows CapabilityAccessManager consent store (LastUsedTimeStop > LastUsedTimeStart for Winly.exe). No display-capture handle while idle: D3D device, frame pool and capture session are all `using`-scoped inside a single `CaptureAll` call (`GraphicsCaptureDisplayCapture.cs:25-70`), so none survives an activation. Handles fell 1555 to 1531, threads 21 to 16, working set 152.5 to 140.8 MB across the window - no leak or growth; process alive throughout. CompanionVisibilityMode=InteractionOnly. |
| quickstart.md sections 1–7 end to end on a mixed-DPI multi-monitor machine (T076) | | | | | | pending |
| Latency: key release → first audible word, ≥ 20 activations on broadband; record median (≤ 4 s) and p95 (≤ 7 s) (SC-001, T079) | | | | | | pending — harness: `Winly.App` logs `activation.latency` events; compute percentiles from `%LOCALAPPDATA%\Winly\logs\` |
| Pointing accuracy across a 10-application set, ≥ 80 % inside the intended element (SC-003, T080) | | | | | | pending |
| 20 consecutive activations, none leaves the app requiring restart (SC-009, T081) | | | | | | pending |
| No capture-related file writes during a multi-activation session (Process Monitor filtered to `Winly.exe`, `WriteFile`) (FR-029, T082) | | | | | | pending |
| Backend address resolves when Winly is launched from a shell/launcher opened before WINLY_PROXY_BASE_URL was set (user-scope fallback) | Windows 11 Home 10.0.26200 | single monitor | 125 % | Claude (session f6b540b7) | 2026-09-13 | PASS - launched from a process whose `WINLY_PROXY_BASE_URL` was empty; startup log reads `Backend https://winly-backend.boodle-doobee.workers.dev/` instead of the not-configured message |
| Streamed PCM playback: answers play without gaps or clipping, and a new activation cuts the previous one off cleanly (post-2026-09-13 latency work) | | | | | | pending |
| Action — "open Notepad" / "open Spotify" launches the app; an app that is not installed gets the plain "I couldn't find an app called X" message rather than silence | | | | | | pending |
| Spotify — Connect Spotify in the panel completes the browser round trip and reports connected | | | | | | pending |
| Action — "play <song> on Spotify" actually starts that track (not a search screen) once linked | | | | | | pending |
| Action — "lower the volume from Spotify" moves **Spotify's own slider**, visible in the Spotify UI, not the Windows mixer entry | | | | | | pending |
| Action — repeating "turn it down" keeps lowering rather than jumping to a fixed level | | | | | | pending |
| Action — "set the volume to 40 percent" lands on 40, and "mute it" on 0 | | | | | | pending |
| Action — "pause the song" / "next track" reach Spotify while it is unfocused and minimised | | | | | | pending |
| Action — with nothing playing at all, a playback command says so rather than failing silently | | | | | | pending |
| Action — "add <song> to the queue" queues without interrupting what is playing | | | | | | pending |
| Action — window verbs: focus, minimise, maximise, close, and snap left/right on a scaled display | | | | | | pending |
| Action — system verbs: lock, dark/light mode (apps repaint without sign-out), mute, Wi-Fi and Bluetooth toggles | | | | | | pending |
| Action — brightness on an internal panel; on a desktop monitor it says it cannot rather than failing silently | | | | | | pending |
| Action — "type out ..." lands the text in the focused window, including an accented character and an emoji | | | | | | pending |
| Action — "what is on my clipboard" reads it aloud; with an image on the clipboard it says there is no text | | | | | | pending |
| Action — "open my downloads folder" opens it; a name that matches nothing says so | | | | | | pending |
| Action — "remind me in one minute to X" speaks the reminder aloud when it comes due | | | | | | pending |
| Cost — a command turn logs `displays:0` in `chat.usage` and a screen question logs `displays:1` (npx wrangler tail) | | | | | | pending |
| Cost — `cacheRead` is non-zero from the second activation onward within an hour | | | | | | pending |
| Escape hatch — a screen question phrased as a command still gets answered, via one extra round trip | | | | | | pending |
| Web — "what is the weather right now" answers from a live search; "what is the capital of France" does not search | | | | | | pending |
| Action — a screen question ("what is this button") triggers no desktop action | | | | | | pending |
| Action — unchecking "Let Winly open apps and change volume" stops actions and posts the settings message | | | | | | pending |
