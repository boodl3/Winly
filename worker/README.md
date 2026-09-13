# Winly backend (Cloudflare Worker)

Holds every third-party credential server-side (Constitution Principle I). The desktop
client sends no credential to any route and receives no long-lived credential back.
Route contracts: `specs/001-core-companion-loop/contracts/worker-api.md`.

## Providers behind each route

| Route | Provider | Why |
|---|---|---|
| `POST /chat` | Anthropic Messages API (`CHAT_MODEL`, default `claude-sonnet-5`) | Vision + streaming; the Worker separates the spoken answer from the trailing `@@POINT …@@` designation |
| `POST /tts` | ElevenLabs streaming text-to-speech (`TEXT_TO_SPEECH_MODEL` / `TEXT_TO_SPEECH_VOICE` / `TEXT_TO_SPEECH_OUTPUT_FORMAT`) | Low-latency streaming synthesis; the Worker forwards the body straight through, defaulting to headerless PCM so the client plays it as it arrives instead of buffering an MP3 |
| `POST /transcribe-token` | AssemblyAI Streaming v3 temporary token | Token goes in the websocket URL, exactly the direct-connection case research.md §7 describes |
| `POST /music/*` | Spotify Web API, user OAuth | `status`, `state`, `play`, `transport`, `volume`. Optional: with no credentials every route answers `music_not_configured` and the client keeps to the Windows mixer and a search fallback |
| `GET /spotify/login`, `GET /spotify/callback` | Spotify authorization code flow | The only two browser-facing routes. The refresh token is written to KV and never reaches the client |

## Required secrets

```
npx wrangler secret put CHAT_PROVIDER_API_KEY
npx wrangler secret put SPEECH_TO_TEXT_PROVIDER_API_KEY
npx wrangler secret put TEXT_TO_SPEECH_PROVIDER_API_KEY
npx wrangler secret put WINLY_CLIENT_TOKEN
```

`WINLY_CLIENT_TOKEN` is the shared token every desktop client presents as
`Authorization: Bearer <token>`. Every POST is refused with `401 unauthorized` without it —
including when the secret itself is unset, deliberately: an auth check that disappears along with
its secret is the failure it exists to prevent, and a secret written but never promoted to the live
deployment is the common way that happens here (see Commands).

It is not a provider credential, so Principle I is untouched — it gates this Worker only. Generate
one and set it from a file rather than pasting at the masked prompt:

```
node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))" > token.txt
npx wrangler secret put WINLY_CLIENT_TOKEN < token.txt
```

Then set the same value on the client machine and restart Winly:

```
setx WINLY_BACKEND_TOKEN "<token>"
```

Ceiling worth knowing: one shared token, extractable from any copy of the client binary. It closes
the real exposure — a leaked URL letting a stranger spend the provider credits — and raises control
of a linked Spotify account from "learn an install id" to "learn an install id *and* hold the
binary". It does not survive public distribution; that wants per-install tokens behind a real
sign-in.

## Optional secrets

```
npx wrangler secret put SPOTIFY_CLIENT_ID
npx wrangler secret put SPOTIFY_CLIENT_SECRET
```

Create an app at <https://developer.spotify.com/dashboard> and add exactly this redirect URI:

```
https://winly-backend.boodle-doobee.workers.dev/spotify/callback
```

Then press **Connect Spotify** in the Winly panel once; the browser round trip stores a refresh
token in the `MUSIC_TOKENS` KV namespace under the install's own id. Scopes requested are
`user-read-playback-state user-modify-playback-state`, and Spotify restricts playback control
to Premium accounts. Without any of this the music routes answer `music_not_configured` and
Winly falls back to the Windows mixer and a `spotify:search:` link. Remember the promotion
check in CLAUDE.md after setting a secret.

The install id is effectively a bearer token for that account's playback: anyone who learns
both the Worker URL and an install id can control it. That is the same unauthenticated-Worker
caveat as the note at the bottom of this file, and it wants fixing before any public release.

## Cost notes

`/chat` logs a `chat.usage` line per request (`npx wrangler tail`) with input, output and cache
token counts — measure there rather than guessing. Two things keep it down and are easy to
break by accident:

- The system prompt is cached with a **1 hour** TTL. Caching is ignored entirely below 1024
  tokens, and the prompt sat just under that for a while doing nothing; if you shorten it,
  check `cacheRead` is still non-zero on the second request in a row.
- Screenshots are capped by pixel area, not just edge length — see `CapturedFrameEncoder` —
  and are only attached when the question looks like it needs them.
- **The web search tool is attached per request, never by default.** Its definition alone is
  about 2,700 input tokens, more than this entire system prompt, and it would be billed on
  every turn whether or not a search happened. Measured: 2,446 cached tokens without it,
  5,084 with. The model asks for it with `@@NEEDWEB@@` when the heuristic guessed wrong.

Measured steady-state, cache warm, one 1356x848 display:

| Turn | Fresh input | Cached | Output |
|---|---|---|---|
| Command ("turn it down a bit") | 121 | 2,446 | 32 |
| Screen question | 1,610 | 2,446 | 90 |

The first turn of each hour writes the cache instead of reading it, which costs about twice a
pre-caching turn; the break-even is the third activation in that hour.

Non-secret vars (`CHAT_MODEL`, `TEXT_TO_SPEECH_MODEL`, `TEXT_TO_SPEECH_VOICE`,
`TEXT_TO_SPEECH_OUTPUT_FORMAT`) live in `wrangler.toml`. If your ElevenLabs plan rejects
`pcm_24000`, set `TEXT_TO_SPEECH_OUTPUT_FORMAT = "mp3_44100_128"` — the client reads the
format off the response content type and falls back to buffered MP3 playback.

## Commands

```
npm install
npm run typecheck
npm run dev        # local: keys come from .dev.vars (copy .dev.vars.example)
npm run deploy
```

`.dev.vars` is the one documented credential-at-rest exception (plan.md Complexity
Tracking) and is git-ignored. No client project reads it under any configuration.

## Note

The routes are unauthenticated: anyone who learns the Worker URL can spend your provider
credits. Before a public release, add a per-install shared secret or Cloudflare Access in
front of the Worker — out of scope for this feature.
