# Quickstart: Core Companion Loop

Validates the feature end to end: clone → backend deployed → client running →
ask a question about the screen → hear an answer.

## Prerequisites

- Windows 10 build 19041+ (research.md §1), x64
- .NET 10 SDK
- Node.js + `npm` (for Wrangler)
- A Cloudflare account with Workers enabled
- API keys for the chat, speech-to-text, and text-to-speech providers chosen for
  this deployment (never placed in any client project — Constitution Principle I)
- A working microphone and speakers/headphones

## 1. Deploy the backend

```
cd worker
npm install
npx wrangler secret put CHAT_PROVIDER_API_KEY
npx wrangler secret put SPEECH_TO_TEXT_PROVIDER_API_KEY
npx wrangler secret put TEXT_TO_SPEECH_PROVIDER_API_KEY
npx wrangler deploy
```

Note the deployed Worker URL (e.g., `https://winly-backend.<subdomain>.workers.dev`).

**Verify no credential reaches any client artifact**: `wrangler secret put` stores
the value server-side only; confirm none of the three keys appear anywhere under
`src/` by searching the client source tree for the key values before proceeding.

## 2. Point the client at the backend

Set the proxy base URL the client project reads at startup (exact configuration
key defined alongside `ProxyEndpointOptions` in `Winly.Providers`) to the
Worker URL from step 1.

## 3. Run the client

```
dotnet run --project src/Winly.App
```

Expect: no taskbar window; a tray icon appears (FR-001); on first launch, a
disclosure screen describing what is captured, when, and where it is sent
(FR-026) — accept it to proceed.

## 4. Validate the core loop (User Story 1)

1. Open any application with visible text (an error dialog, a settings panel).
2. Hold **Windows key + Left Alt** (the default activation combination —
   see Clarifications in `spec.md`) and ask a question aloud about what's
   visible, e.g. "what does this button do".
3. Release the key.
4. **Expected**: the companion shows a listening indicator while held, a
   working indicator after release, then speaks an answer referencing the
   actual on-screen content — within 4 seconds median (SC-001).

## 5. Validate pointing (User Story 2)

1. In an application with a specific labeled control, ask "where do I change
   `<setting>`".
2. **Expected**: the companion travels to that control and remains there while
   the answer is spoken; clicking elsewhere still reaches the underlying
   application (FR-024).

## 6. Validate capture controls (User Story 4)

1. Open settings and disable screen and microphone capture.
2. Restart the application.
3. Hold the activation key.
4. **Expected**: no capture occurs; the companion or tray indicates capture is
   off and explains why (FR-028).

## 7. Validate resilience

1. Disconnect network access, then repeat step 4 above.
2. **Expected**: a short, plain-language failure message with no error codes or
   provider names (FR-031, SC-010), and the next activation (after reconnecting)
   works normally (SC-007).

## Local development variant

For iterating on the Worker without deploying on every change:

```
cd worker
npx wrangler dev
```

Provider keys for local development go in `worker/.dev.vars` (git-ignored).
**This file must never be copied into, or referenced by, any client project** —
this is the one documented exception in the plan's Complexity Tracking table,
scoped strictly to local Worker development.
