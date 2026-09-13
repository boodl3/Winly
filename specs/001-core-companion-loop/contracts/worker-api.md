# Contract: Worker API (Proxy)

Three routes, all served by the Cloudflare Worker. **The client sends no
credential to any route, and receives no long-lived credential in any
response** (Constitution Principle I, FR-034). Provider API keys exist only as
Worker secrets and never appear in a request or response body observed by the
client.

All requests and responses are JSON except where noted (SSE, audio bytes).

## `POST /chat`

Submits the transcript and captured displays; returns a streamed natural-language
answer that may designate one on-screen position.

**Request body**:

```json
{
  "transcript": "string, non-empty",
  "displays": [
    {
      "monitorId": "string",
      "imageBase64": "string, JPEG bytes",
      "widthPx": 1568,
      "heightPx": 882,
      "isPrimary": true
    }
  ],
  "history": [
    { "role": "user", "content": "string" },
    { "role": "assistant", "content": "string" }
  ]
}
```

- `displays`: 1 or more entries; exactly one has `isPrimary: true` (FR-008).
- `history`: the current session's prior exchanges (FR-013); empty on the first
  exchange of a session.

**Response**: `Content-Type: text/event-stream`. Each SSE `data:` event is a JSON
fragment:

```json
{ "delta": "string, next chunk of answer text" }
```

The final event in the stream is:

```json
{
  "done": true,
  "pointingTarget": { "monitorId": "string", "x": 0, "y": 0, "label": "string" },
  "action": {
    "action": "open" | "play" | "queue" | "media" | "volume" | "window"
            | "system" | "type" | "clipboard" | "openpath" | "timer",
    "target": "string",
    "argument": "string",
    "amount": 0,
    "relative": false
  },
  "needsScreen": false,
  "needsWebSearch": false
}
```

Note: `pointingTarget.x`/`.y` here map to `PointingTarget.XInCapturePx`/
`.YInCapturePx` in data-model.md — shortened wire-format keys, not a different
coordinate space.

`pointingTarget` is `null` when the answer designates no position (FR-016).
`action` is `null` unless the user asked for something to happen on their desktop.
`target` is an app name, an http(s) URL, a spoken song name, or a playback command
depending on `action`. `argument` is used only by `window` (what to do to it). `amount` and
`relative` apply to `volume`, to `system` with target `brightness`, and to `timer`
(seconds, always absolute). `relative: true` makes `amount` a signed step to add to the
value as it is now — the only way to express "turn it down", since the model cannot read
the current level — and `relative: false` makes it an absolute 0-100.

`needsScreen` and `needsWebSearch` are the model declining to answer without something it
was not given. The client asks once more with whatever was requested; a request that had
already been given both never sets them. When either is set, no `delta` events are sent,
so nothing is spoken before the second attempt. The in-band designations
(`@@POINT …@@`, `@@NOPOINT@@`, `@@DO …@@`) are never present in `delta` text the
client displays or speaks — the Worker separates narration from the structured
fields, and the client additionally strips any residual designation markup
defensively (FR-017).

The client never executes anything the Worker sends verbatim: `action.target`
names an app, an http(s) URL or a song, and the client resolves it against what
is actually installed (or against `/resolve-track`) before launching it with no
arguments. Player URIs are built client-side from the spoken name, never taken
from the model.

**Error responses** (non-2xx, JSON body, connection closed — not SSE):

```json
{ "error": "provider_unavailable" | "provider_timeout" | "invalid_request" }
```

The client maps every value to a short plain-language, non-technical message
(FR-031, SC-010) and returns to `Idle`.

The request body also carries `screenAvailable` (screenshots exist but were withheld to
save tokens, so the model may ask for them) and `webSearchAllowed` (attach the web search
tool — off by default because its definition alone is ~2,700 input tokens, more than the
system prompt, billed on every turn it is attached to). It also carries an optional
`nowPlaying` string — one line describing
what the system media transport says is playing, e.g.
`Playing in Spotify: "3 Nights" by Dominic Fike.` — so a playback command can be
aimed without the model having to find a player in the screenshots.

## `POST /music/*`

Control of the user's own Spotify account. Every route takes `installId` — an opaque GUID
the client generates once and keeps in its settings. The account's refresh token lives in
the Worker's KV store and never reaches the client (FR-034).

| Route | Body | Response |
|---|---|---|
| `/music/status` | `{ installId }` | `{ configured, linked, linkUrl }` |
| `/music/state` | `{ installId }` | `{ playing: { title, artist, isPlaying, deviceName, volumePercent } \| null }` |
| `/music/play` | `{ installId, query, queueOnly }` | `{ title, artist }` |
| `/music/transport` | `{ installId, command }` | `{ ok: true }` |
| `/music/volume` | `{ installId, amount, relative }` | `{ volumePercent }` |

`installId` must match the GUID shape exactly; anything else is `invalid_request` before it
reaches a store key.

**Error responses** add to the `/chat` set: `music_not_configured` (no credentials on the
backend), `music_not_linked` (nobody has signed in for this install, or access was revoked),
`music_no_device` (no Spotify device is awake), `music_needs_premium` (Spotify restricts
playback control to Premium), `music_no_match`, `music_unavailable`. The client turns each
into one plain sentence (FR-031); none of them fails the spoken answer that accompanied the
action.

## `GET /spotify/login` and `GET /spotify/callback`

The only two routes that are not client POSTs. `login?install=<id>` redirects the browser to
Spotify's consent page with a random `state` held in the store for ten minutes; `callback`
exchanges the code, writes the refresh token under that install id, and returns a small page
telling the user to close the tab. Scopes: `user-read-playback-state
user-modify-playback-state`.

## `POST /tts`

Submits final answer text (designation already stripped); returns audio.

**Request body**:

```json
{ "text": "string, non-empty, no markup or list syntax (FR-012)" }
```

**Response**: `200 OK`, streamed as it is synthesized so the client can begin
playback on the first bytes. The format follows the Worker's
`TEXT_TO_SPEECH_OUTPUT_FORMAT` var:

| Content-Type | Body |
|---|---|
| `audio/L16; rate=<hz>` | headerless mono PCM16 at that sample rate (default, `pcm_24000`) |
| `audio/mpeg` | MP3, which the client must buffer whole before it can play it |

**Error response**: same `{ "error": "..." }` shape as `/chat`.

## `POST /transcribe-token`

Mints a short-lived, narrowly scoped token for a single speech-to-text
streaming session (Constitution Principle I's direct-client-connection
exception).

**Request body**: empty (`{}`).

**Response**:

```json
{ "token": "string", "expiresInSeconds": 60, "endpoint": "wss://provider-host/..." }
```

The client uses `token` and `endpoint` to open its own `ClientWebSocket`
directly to the transcription provider (research.md §7). The token is scoped to
exactly one streaming session and expires; the Worker never re-issues a token
for reuse across sessions.

**Error response**: same `{ "error": "..." }` shape as `/chat`.
