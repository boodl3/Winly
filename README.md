# Winly

A Windows desktop AI companion with no main window. Hold a key, speak a request, and
Winly answers aloud using the current screen as context — pointing at on-screen elements
and carrying out desktop actions when the request calls for it.

See [CLAUDE.md](CLAUDE.md) for the full project map (status, architecture, key
decisions, known issues) and
[.specify/memory/constitution.md](.specify/memory/constitution.md) for the seven
non-negotiable principles this project is built on (secrets never in the client, consent
before capture, clean-room originality, vertical slices, interop quarantined, clarity
over cleverness, fail loud/soft).

## What it does

- **Hold-to-talk**: default activation key is Windows key + Left Alt (configurable),
  held for up to 30 seconds.
- **Screen-aware answers**: the model can ask for a screenshot when the question needs
  one, and points at on-screen elements in its reply.
- **Desktop actions**: eleven verbs — `open`, `play`, `queue`, `media`, `volume`,
  `window`, `system`, `type`, `clipboard`, `openpath`, `timer` — carried out on-device by
  `Winly.Platform`, never by the model directly. No arguments are ever passed to a
  launched app, no URL opens unless its scheme is http/https, and nothing irreversible
  (delete, recycle bin, security settings) is exposed.
- **Music via the user's own Spotify account** (optional; degrades gracefully to the
  Windows mixer and a search link if not connected).
- **No main window**: a small overlay companion (square body, two eyes — demo-phase
  visuals) plus a tray icon and settings panel.

## Repository layout

```
src/
  Winly.Core       — state machine, coordinate math, designation parsing, heuristics.
                      Pure C#, no Windows types, fully unit-tested.
  Winly.Platform   — all P/Invoke, WinRT, D3D, WASAPI, WMI. Desktop action execution
                      lives in Winly.Platform/Actions/. Verified manually (Windows-only).
  Winly.Providers  — HTTP/WebSocket clients to the backend Worker.
  Winly.App        — WPF composition root: tray, overlay, settings panel.
tests/             — unit tests for Core and Providers; manual-verification.md for
                      what can only be checked on real hardware.
worker/            — Cloudflare Worker (TypeScript) holding every third-party
                      credential server-side. See worker/README.md.
specs/             — Spec Kit feature specs, plans, tasks, contracts.
.specify/          — Spec Kit tooling (constitution, templates, scripts).
```

Full file-by-file layout: `specs/001-core-companion-loop/plan.md`'s "Project Structure"
section. Worker route contracts: `specs/001-core-companion-loop/contracts/worker-api.md`.

## Prerequisites

- Windows 10 build 19041 (2004) or later.
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Node.js](https://nodejs.org/) (for the Worker) and a
  [Cloudflare](https://dash.cloudflare.com/) account if you want to run your own backend.

## Building and testing the client

```powershell
dotnet build Winly.sln
dotnet test Winly.sln
```

`dotnet run --project src/Winly.App` launches the app (or run `Winly.exe` directly after
building). Winly needs a reachable backend Worker URL — see below — configured in the
settings panel or via environment variable; it logs its resolved backend at startup.

## Running the backend

The client never holds a provider API key — everything (Anthropic, ElevenLabs,
AssemblyAI, Spotify) is proxied through a Cloudflare Worker. To run your own:

```bash
cd worker
npm install
npm run dev       # local dev; keys come from .dev.vars (copy .dev.vars.example)
npm run typecheck
npm run deploy    # deploy to Cloudflare
```

See [worker/README.md](worker/README.md) for required/optional secrets, Spotify setup,
and cost-control notes (prompt caching, screenshot sizing, opt-in web search).

## Development workflow

This project uses [Spec Kit](.specify/) for spec-driven development. Skills live under
`.claude/skills/speckit-*`. See CLAUDE.md's "Where to look before making changes" section
for which artifact to update for a given kind of change.

## License

See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for third-party license
attributions.
