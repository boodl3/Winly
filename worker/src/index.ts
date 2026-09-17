import Anthropic from "@anthropic-ai/sdk";

export interface Env {
  // Secrets — set with `wrangler secret put`, never present in any response body.
  CHAT_PROVIDER_API_KEY: string;
  SPEECH_TO_TEXT_PROVIDER_API_KEY: string;
  TEXT_TO_SPEECH_PROVIDER_API_KEY: string;
  // Optional secrets. Without them the music routes report "not configured" and the client keeps
  // to what it can do locally.
  SPOTIFY_CLIENT_ID?: string;
  SPOTIFY_CLIENT_SECRET?: string;
  // The shared token every desktop client presents. Not a third-party credential: it gates this
  // Worker itself, so Principle I is untouched. Unset means every POST is refused, deliberately —
  // an auth check that disappears when its secret is missing is the bug it was added to prevent.
  WINLY_CLIENT_TOKEN?: string;
  // Non-secret vars from wrangler.toml.
  CHAT_MODEL: string;
  TEXT_TO_SPEECH_MODEL: string;
  TEXT_TO_SPEECH_VOICE: string;
  TEXT_TO_SPEECH_OUTPUT_FORMAT: string;
  WEB_SEARCH_MAX_USES: string;
  // Holds each install's Spotify tokens. The user's tokens never reach the desktop client.
  MUSIC_TOKENS: KVNamespace;
}

type ErrorCode =
  | "unauthorized"
  | "provider_unavailable"
  | "provider_timeout"
  | "invalid_request"
  | "music_not_configured"
  | "music_not_linked"
  | "music_no_device"
  | "music_needs_premium"
  | "music_no_match"
  | "music_unavailable";

interface DisplayPayload {
  monitorId: string;
  imageBase64: string;
  widthPx: number;
  heightPx: number;
  isPrimary: boolean;
}

interface ChatRequestBody {
  transcript: string;
  displays: DisplayPayload[];
  history: { role: "user" | "assistant"; content: string }[];
  nowPlaying?: string | null;
  screenAvailable?: boolean;
  webSearchAllowed?: boolean;
}

type DesktopActionKind =
  | "open"
  | "play"
  | "queue"
  | "media"
  | "volume"
  | "window"
  | "system"
  | "type"
  | "clipboard"
  | "openpath"
  | "click"
  | "timer";

const ACTION_KINDS: readonly string[] = [
  "open",
  "play",
  "queue",
  "media",
  "volume",
  "window",
  "system",
  "type",
  "clipboard",
  "openpath",
  "click",
  "timer",
];

interface DesktopAction {
  action: DesktopActionKind;
  target: string;
  argument: string;
  amount: number;
  relative: boolean;
}

interface PointingTarget {
  monitorId: string;
  x: number;
  y: number;
  label: string;
}

const UPSTREAM_TIMEOUT_MILLISECONDS = 12_000;
const TRANSCRIBE_TOKEN_TTL_SECONDS = 60;
const TRANSCRIBE_TOKEN_URL = `https://streaming.assemblyai.com/v3/token?expires_in_seconds=${TRANSCRIBE_TOKEN_TTL_SECONDS}`;
// Short spoken commands are the hardest thing for a general transcriber: there is no sentence
// around a word to disambiguate it, so one missed phoneme ("pin Claude" -> "pinch Claude") turns a
// window command into nonsense that the chat model then answers as if it meant something. Every
// lever here is free — they are query parameters, not extra requests.
//   prompt        — free text, Universal-3.5 Pro only (the default model here). Says what kind of
//                   utterance this is, which is what a human transcriptionist would be told.
// The third lever, keyterms_prompt, is appended by the client rather than set here: half of it is
// the names of the apps that are actually open, which only the desktop side knows.
const TRANSCRIBE_PROMPT =
  "The speaker is talking to a voice assistant on their Windows PC. Utterances are spoken commands " +
  "about applications, windows, volume, music and the screen, or questions about what is on the " +
  "screen. The speaker often talks quickly and chains several instructions into one breath, " +
  "separated by commas or by 'and', with words running together and word endings clipped; " +
  "transcribe the whole utterance rather than stopping at the first instruction. Prefer command " +
  "words over similar-sounding ordinary words.";
const TRANSCRIBE_STREAM_ENDPOINT =
  "wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&encoding=pcm_s16le&format_turns=true" +
  "&mode=max_accuracy" +
  // Push-to-talk: the hold IS the utterance, so never let a thinking pause split it into two turns
  // decoded independently of each other. "Pin Claude / to the left" split exactly there, and the
  // first half alone is what "pin" was misheard in. 10 s is the parameter's ceiling; Terminate
  // still finalizes the open turn when the key is released (measured, not assumed).
  "&min_turn_silence=10000" +
  // min_turn_silence is only the floor before an end-of-turn check is *run*. Two other levers end a
  // turn on their own and both default to ending it early: max_turn_silence forces one after 1536 ms
  // of silence whatever the minimum says, and end_of_turn_confidence_threshold ends one at 0.4 when
  // the words merely *sound* finished. Someone rattling off "open Spotify, put some jazz on, and
  // throw Chrome on the left" trips both — a breath between jobs, and clauses that each parse as a
  // complete sentence. A split there is not just two decodes instead of one: the two finals overlap,
  // so the joined transcript duplicates words, and the client's single-turn fast path is refused, so
  // it costs latency too. Nothing here is lost by refusing to end turns: this is push-to-talk, and
  // releasing the key sends Terminate, which finalizes the turn regardless of either threshold.
  "&max_turn_silence=10000" +
  "&end_of_turn_confidence_threshold=0.9" +
  // A desktop microphone hears the room: a fan, a second person, whatever is playing. near-field
  // is the profile for someone sitting at the machine, which is the only way Winly is ever used.
  "&voice_focus=near-field" +
  `&prompt=${encodeURIComponent(TRANSCRIBE_PROMPT)}`;
const DEFAULT_TEXT_TO_SPEECH_OUTPUT_FORMAT = "pcm_24000";
const textToSpeechUrl = (voiceId: string, outputFormat: string) =>
  `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voiceId)}/stream` +
  `?output_format=${encodeURIComponent(outputFormat)}`;

// An answer is synthesized one sentence at a time so playback can start before the model has
// finished writing it, which leaves every chunk sounding like the opening of a fresh announcement:
// the voice resets its pitch and pace at each seam. `previous_text` is what it just said — free
// context, since only `text` is billed — so the delivery carries across the seam instead.
const TEXT_TO_SPEECH_CONTEXT_CHARACTERS = 500;
export function textToSpeechBody(text: string, previousText: string | undefined, model: string) {
  return {
    text,
    model_id: model,
    ...(previousText ? { previous_text: previousText.slice(-TEXT_TO_SPEECH_CONTEXT_CHARACTERS) } : {}),
    // A one-word chunk ("Done.") carries no evidence of what language it is in, and a mis-detection
    // is audible. Only the v2.5 models accept the hint — the others answer 400 for an unknown field.
    // ponytail: English-only app; this becomes a var the day Winly speaks anything else.
    ...(model.endsWith("_v2_5") ? { language_code: "en" } : {}),
  };
}

const SPOTIFY_SCOPES = "user-read-playback-state user-modify-playback-state";
const SPOTIFY_API = "https://api.spotify.com/v1";
const LINK_STATE_TTL_SECONDS = 600;
const INSTALL_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const SYSTEM_PROMPT = `You are Winly, a voice companion living on the user's Windows desktop. The user spoke a question out loud. When screenshots of their displays are attached, the primary display is the one containing their mouse cursor.

Answer in one to three short spoken sentences, and keep the first one short — it is spoken aloud while the rest is still being written. Plain speech only: no markdown, no lists, no headings, no code fences, no URLs, and no symbols that would sound odd read aloud. If the question is about the screen, refer to what is actually visible. If it is unrelated to the screen, just answer it without forcing screen content in. If part of a screenshot is blank or black, say you cannot see that part rather than guessing.

After your spoken answer, on a new final line, write exactly one pointing designation:
@@POINT {"monitorId":"<display id>","x":<integer>,"y":<integer>,"label":"<short name of the element>"}@@ — when the answer concerns one specific on-screen element. x and y are pixel coordinates inside that display's attached image (0,0 is the top-left corner; each image's size is stated next to it).
@@NOPOINT@@ — when no single element is relevant, and always when no screenshots are attached.

When the user asked you to DO something on their computer rather than only answer it, add an action designation on the line after that — one for each separate thing they asked for — and say in your spoken answer that you are doing it. Every action takes this shape, with only the fields that verb needs:
@@DO {"action":"<verb>","target":"<string>","argument":"<string>","amount":<number>,"relative":<true or false>}@@

The verbs:
"open" — target is an installed app's plain name (Spotify, Notepad, Chrome) or an http(s) URL. Launches it, or brings it to the front if it is already running, so use it even when the app is only minimised.
"play" — target is a song, artist or album in plain words: only what they named, no URL and no quotes. Argument is the service they named out loud — YouTube, YouTube Music, Apple Music, SoundCloud, Tidal, Deezer, Bandcamp, Amazon Music — and empty when they named none, which means Spotify. Put the service in the argument and leave it out of the target. Only Spotify actually starts playing; the rest open that service's own search, which Winly says out loud, so never claim a track is playing on one of them.
"queue" — same as play, but adds to the queue instead of interrupting.
"media" — target is playpause, play, pause, next, previous, stop, shuffle or repeat. Reaches whatever is playing.
"volume" — target is an app's name, or "spotify" for the music itself, or empty for the whole system. With "relative":true, amount is a step added to the current volume — the only way to express "turn it down", since you cannot read the current level. Use about -20 for a nudge down, +20 for up, -40 for "much quieter". With "relative":false, amount is an absolute 0-100.
"window" — target is an app's name, argument is focus, minimize, maximize, restore, close, left or right (left and right snap it to that half of the screen).
"system" — target is lock, sleep, darkmode, lightmode, brightness, mute, unmute, wifi, bluetooth or showdesktop. Only "brightness" carries an amount, absolute or relative like volume.
"type" — target is the literal text to type into whatever is focused. It is not only for dictation: when they ask you to write something down — a summary, a note, an email draft — you compose the text yourself and put it in the target. Open or click the destination first, in the same request, so the text has somewhere to land: a document goes in Notepad, an email goes in the compose window of whatever mail they use. Write the real text out in full: never a placeholder, an ellipsis, or a run of repeated characters standing in for words you have not written. Use line breaks where the text should have them, a blank line between paragraphs, so a letter or an email arrives laid out rather than as one block. Keep it under about 1500 characters; if that is not enough room, write a shorter piece that is complete rather than a long one that is not.
"clipboard" — target is read (say the clipboard aloud) or copy (copy the current selection).
"openpath" — target is a file or folder name to find under the user's own profile and open.
"click" — target is the visible label of something on their screen to press: a video on a results page, a link, a button, a row. Argument is the app it is in (Edge, Chrome, Spotify) or empty for whatever they are looking at. Copy the label off the screenshot as exactly as you can read it — that text is matched against the real control, so "Procreate Tutorial for Beginners" finds it and "the first video" does not. This is the only verb that reaches something that is merely on screen; it needs the screenshots, so ask for them with @@NEEDSCREEN@@ if they are not attached.
"timer" — amount is how many seconds from now, target is what the timer is for. "Remind me in 20 minutes to stretch" is amount 1200, target "stretch".

Worked examples, spoken request on the left and the designation it deserves on the right:
"turn spotify down a bit" -> @@DO {"action":"volume","target":"spotify","amount":-20,"relative":true}@@
"way too loud" -> @@DO {"action":"volume","target":"","amount":-40,"relative":true}@@
"set the volume to forty percent" -> @@DO {"action":"volume","target":"","amount":40,"relative":false}@@
"mute it" -> @@DO {"action":"system","target":"mute"}@@
"put on some Radiohead" -> @@DO {"action":"play","target":"Radiohead"}@@
"play Bohemian Rhapsody on Spotify" -> @@DO {"action":"play","target":"Bohemian Rhapsody"}@@
"play lofi beats on YouTube" -> @@DO {"action":"play","target":"lofi beats","argument":"YouTube"}@@ — the service goes in the argument, never in the target
"add this to the queue, Weightless by Marconi Union" -> @@DO {"action":"queue","target":"Weightless Marconi Union"}@@
"pause the song" -> @@DO {"action":"media","target":"pause"}@@
"next song" -> @@DO {"action":"media","target":"next"}@@
"open my email" -> @@DO {"action":"open","target":"Outlook"}@@, naming whichever mail app the screenshots actually show
"pull up the weather" -> @@DO {"action":"open","target":"https://www.google.com/search?q=weather"}@@
"put chrome on the left half" -> @@DO {"action":"window","target":"Chrome","argument":"left"}@@
"close discord" -> @@DO {"action":"window","target":"Discord","argument":"close"}@@
"switch to Slack" -> @@DO {"action":"window","target":"Slack","argument":"focus"}@@
"turn on dark mode" -> @@DO {"action":"system","target":"darkmode"}@@
"dim the screen" -> @@DO {"action":"system","target":"brightness","amount":-30,"relative":true}@@
"lock my pc" -> @@DO {"action":"system","target":"lock"}@@
"what's on my clipboard" -> @@DO {"action":"clipboard","target":"read"}@@
"type out dear Sam, thanks for the update" -> @@DO {"action":"type","target":"Dear Sam, thanks for the update"}@@
"open my tax folder" -> @@DO {"action":"openpath","target":"tax"}@@
"write me a summary of this in notepad" -> @@DO {"action":"open","target":"Notepad"}@@ then @@DO {"action":"type","target":"<the summary, written out in full>"}@@
"draft an email to Sam saying the release slipped" -> @@DO {"action":"open","target":"https://mail.google.com/mail/u/0/?view=cm&fs=1"}@@ then @@DO {"action":"click","target":"Message Body","argument":"Chrome"}@@ then @@DO {"action":"type","target":"<the draft>"}@@ — the compose URL carries nothing but the compose flag
"play the first Procreate video" -> @@DO {"action":"click","target":"<the first result's title, read off the screenshot>","argument":"Edge"}@@ — a video on a page is a click, never "play"
"click the accept button" -> @@DO {"action":"click","target":"Accept","argument":""}@@
"open the second search result" -> @@DO {"action":"click","target":"<that result's title, read off the screenshot>","argument":"Chrome"}@@
"remind me in twenty minutes to stretch" -> @@DO {"action":"timer","target":"stretch","amount":1200}@@
"what does this button do" -> no action designation at all, just the pointing one
"how much is this going to cost me" -> no action designation at all

When your answer asks the user something you need an answer to before you can go on, add @@LISTEN@@ on its own line and Winly will reopen the microphone for them straight away instead of making them press the key again. Only when you genuinely need the reply — asking them to repeat a request you could not make out, or which of two things they meant. Never on a question you are only asking rhetorically, and never on an answer that is complete without one.

What you are given is speech recognition output, so a word is sometimes misheard. If a request is one small sound away from a plain command — "pinch Claude to the left" for "pin Claude to the left" — act on the command it obviously meant. If you genuinely cannot tell what was asked, say you did not catch that and ask them to say it again: never assemble an answer out of unrelated things on the screen to have something to say.

Add an action designation only when the user actually asked for something to happen; a question about what is on screen is not a request to act. Never mention any designation line in the spoken answer.

One hold of the key is one utterance, and people put several jobs in it. Write a separate designation for every single thing they asked for, in the order they need to happen — an app has to be opened before it can be asked to do anything — up to a maximum of ten. "open spotify and play some jazz" is two: open, then play. "open spotify, put on some jazz, throw chrome on the left and turn dark mode on" is four. Do not merge two jobs into one designation, and do not quietly drop the last one because the request was long. Many requests are still just one.

Say only what you actually wrote a designation for. If you did not emit an action, do not say the thing is done, opening, playing or "should be up" — say plainly that you cannot do that part, or ask what they meant. Winly tells the user itself when an action fails, so an honest "I'm opening it" is right even if it turns out not to work; a claim with no designation behind it is always wrong.

Two rules about writing into other people's apps. Never put a recipient, a subject or any message text in a URL you open — open the bare compose window and type them into the fields instead. And never press Send, Publish or Post: write the draft, say it is ready, and leave it for the user to send themselves.

Three mistakes to avoid: reaching for "open" with a music URL when the user wants to hear something (that is what "play" is for), giving an absolute volume when the user said louder or quieter (that is what "relative" is for), and using "play" for anything that is not music on Spotify — a video, a file or anything visible on the screen is "click" or "openpath".

Earlier turns of this conversation may be attached above. They are there for a follow-up like "and the one next to it?", which arrives seconds later. Unless the latest request actually refers back to one of them, answer it entirely on its own and do not mention them — a question that has nothing to do with the last one is the common case, not the exception.

If a line saying what is playing is attached below, trust it over the screenshots for questions about the current song, and for aiming playback controls.

Sometimes no screenshots are attached because the question did not look like it needed them. If that turns out to be wrong and you genuinely cannot answer without seeing the screen, reply with exactly @@NEEDSCREEN@@ and nothing else — no spoken words at all — and you will be asked again with the screenshots attached. Do not use it for anything you can answer from the conversation, from what is playing, or from your own knowledge.

A web search tool is attached only to questions that looked like they needed one, because its definition costs more than this whole prompt on every turn it is attached to. When it is there, use it only for information that changes — news, prices, scores, weather, release dates — and never for something on the screen or something you already know. When it is not there and the answer truly depends on current information, reply with exactly @@NEEDWEB@@ and nothing else, and you will be asked again with it attached.`;

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function errorResponse(error: ErrorCode, status: number): Response {
  return jsonResponse({ error }, status);
}

/**
 * Whether the request carries the shared client token.
 *
 * ponytail: one shared token, extractable from any copy of the client binary. It closes the
 * actual exposure — the Worker URL leaking into a log, a screenshot or a repo lets a stranger
 * spend the provider credits — and raises control of a linked Spotify account from "learn an
 * install id" to "learn an install id AND hold the binary". It does NOT survive public
 * distribution: before release this wants per-install tokens minted behind a real sign-in.
 *
 * Exported for index.test.ts.
 */
export function isAuthorized(request: Request, env: Env): boolean {
  const expected = env.WINLY_CLIENT_TOKEN;
  if (!expected) {
    console.log("auth.unconfigured");
    return false;
  }

  const header = request.headers.get("Authorization") ?? "";
  const presented = header.startsWith("Bearer ") ? header.slice(7) : "";

  // Constant-time over the expected length. Leaking whether the lengths match is harmless;
  // leaking how many leading characters were right is the thing worth not doing.
  if (presented.length !== expected.length) {
    return false;
  }
  let difference = 0;
  for (let i = 0; i < expected.length; i += 1) {
    difference |= presented.charCodeAt(i) ^ expected.charCodeAt(i);
  }
  return difference === 0;
}

function isTimeout(error: unknown): boolean {
  return error instanceof Error && (error.name === "TimeoutError" || error.name === "AbortError");
}

/**
 * Separates narration from the trailing designations while text is still streaming.
 * Text before any "@@" is safe to forward immediately; everything from the first "@@"
 * onward is held until the stream ends so a partially received tag is never spoken.
 *
 * Exported only so index.test.ts can reach it: this is the sole place designations are parsed,
 * the desktop client trusting whatever comes out of it rather than re-deriving anything.
 */
/**
 * JSON.parse, tolerating the one thing a model reliably gets wrong in a designation: a real
 * line break inside a string. JSON forbids a raw control character there, so a composed email
 * or note -- the only targets that have line breaks at all -- threw and was discarded silently,
 * while the strip below still removed the tag. The user heard "writing that up now" and nothing
 * happened, which is indistinguishable from the model never emitting the designation.
 *
 * Escapes control characters that sit inside a string literal and tries once more. Anything
 * still malformed after that is genuinely malformed.
 */
function parseDesignation(json: string): unknown {
  try {
    return JSON.parse(json);
  } catch {
    // Fall through to the repair below.
  }

  const ESCAPES: Record<string, string> = {
    [String.fromCharCode(10)]: String.fromCharCode(92) + "n",
    [String.fromCharCode(13)]: String.fromCharCode(92) + "r",
    [String.fromCharCode(9)]: String.fromCharCode(92) + "t",
  };
  let repaired = "";
  let inString = false;
  let escaped = false;
  for (const character of json) {
    if (escaped) {
      repaired += character;
      escaped = false;
    } else if (character === String.fromCharCode(92)) {
      repaired += character;
      escaped = true;
    } else if (character === '"') {
      inString = !inString;
      repaired += character;
    } else if (inString && character < " ") {
      // Anything else below 0x20 is dropped: it is not text the user asked to be typed.
      repaired += ESCAPES[character] ?? "";
    } else {
      repaired += character;
    }
  }

  try {
    return JSON.parse(repaired);
  } catch {
    console.log("chat.designation_unparseable");
    return null;
  }
}

export class DesignationSplitter {
  private pending = "";

  push(text: string): string {
    this.pending += text;
    const tagStart = this.pending.indexOf("@@");
    if (tagStart >= 0) {
      const safe = this.pending.slice(0, tagStart);
      this.pending = this.pending.slice(tagStart);
      return safe;
    }
    if (this.pending.endsWith("@")) {
      const safe = this.pending.slice(0, -1);
      this.pending = "@";
      return safe;
    }
    const safe = this.pending;
    this.pending = "";
    return safe;
  }

  finish(): {
    trailingText: string;
    pointingTarget: PointingTarget | null;
    actions: DesktopAction[];
    needsScreen: boolean;
    needsWebSearch: boolean;
    awaitingReply: boolean;
  } {
    let pointingTarget: PointingTarget | null = null;
    const pointMatch = /@@POINT\s*(\{[\s\S]*?\})\s*@@/.exec(this.pending);
    if (pointMatch) {
      try {
        const parsed = parseDesignation(pointMatch[1]) as Partial<PointingTarget> | null;
        if (parsed !== null && typeof parsed.monitorId === "string" && Number.isFinite(parsed.x) && Number.isFinite(parsed.y)) {
          pointingTarget = {
            monitorId: parsed.monitorId,
            x: Math.round(parsed.x as number),
            y: Math.round(parsed.y as number),
            label: typeof parsed.label === "string" ? parsed.label : "",
          };
        }
      } catch {
        // Malformed tag: treated as no designation; the text is dropped below.
      }
    }

    // Every designation, in the order the model wrote them. Each is validated on its own, so a
    // malformed one is discarded while the valid ones around it survive — one bad tag must never
    // void a whole sequence. Not capped here: the client caps and is the only side that can tell
    // the user its request was truncated, which capping here would hide from it (FR-003).
    const actions: DesktopAction[] = [];
    const actionTag = /@@DO\s*(\{[\s\S]*?\})\s*@@/g;
    let actionMatch: RegExpExecArray | null;
    while ((actionMatch = actionTag.exec(this.pending)) !== null) {
      try {
        const parsed = parseDesignation(actionMatch[1]) as Partial<DesktopAction> | null;
        if (parsed !== null && typeof parsed.action === "string" && ACTION_KINDS.includes(parsed.action)) {
          const relative = parsed.relative === true;
          actions.push({
            action: parsed.action as DesktopActionKind,
            target: typeof parsed.target === "string" ? parsed.target.trim() : "",
            argument: typeof parsed.argument === "string" ? parsed.argument.trim() : "",
            amount: Number.isFinite(parsed.amount) ? Math.round(parsed.amount as number) : 0,
            relative,
          });
        }
      } catch {
        // Malformed tag: this one designation is discarded; the rest of the sequence stands.
      }
    }

    const needsScreen = /@@NEEDSCREEN@@/.test(this.pending);
    const needsWebSearch = /@@NEEDWEB@@/.test(this.pending);

    // The answer asked the user something and cannot go on without the reply, so the client reopens
    // the microphone instead of making them press the key again. Marked by the model rather than
    // guessed from a trailing question mark, which would open the microphone on rhetorical phrasing.
    const awaitingReply = /@@LISTEN@@/.test(this.pending);

    const trailingText = this.pending
      .replace(/@@POINT\s*\{[\s\S]*?\}\s*@@/g, "")
      .replace(/@@NOPOINT@@/g, "")
      .replace(/@@DO\s*\{[\s\S]*?\}\s*@@/g, "")
      .replace(/@@NEEDSCREEN@@/g, "")
      .replace(/@@NEEDWEB@@/g, "")
      .replace(/@@LISTEN@@/g, "")
      .replace(/@+\s*$/, "")
      .trimEnd();
    this.pending = "";
    return { trailingText, pointingTarget, actions, needsScreen, needsWebSearch, awaitingReply };
  }
}

async function handleChat(request: Request, env: Env): Promise<Response> {
  let body: ChatRequestBody;
  try {
    body = await request.json<ChatRequestBody>();
  } catch {
    return errorResponse("invalid_request", 400);
  }
  if (typeof body.transcript !== "string" || body.transcript.trim() === "" || !Array.isArray(body.displays)) {
    return errorResponse("invalid_request", 400);
  }

  const content: Anthropic.ContentBlockParam[] = [];
  for (const display of body.displays) {
    const primaryNote = display.isPrimary ? ", primary, contains the cursor" : "";
    content.push({
      type: "text",
      text: `Display "${display.monitorId}" (${display.widthPx}x${display.heightPx} pixels${primaryNote}):`,
    });
    content.push({
      type: "image",
      source: { type: "base64", media_type: "image/jpeg", data: display.imageBase64 },
    });
  }

  if (typeof body.nowPlaying === "string" && body.nowPlaying.trim() !== "") {
    content.push({ type: "text", text: body.nowPlaying.trim() });
  }

  if (body.displays.length === 0) {
    content.push({
      type: "text",
      text: body.screenAvailable === true
        ? "No screenshots are attached this time, because the question did not look like it needed them. Answer without them if you can, or reply with only @@NEEDSCREEN@@ if you truly cannot."
        : "No screenshots are attached and none can be taken right now, so answer without seeing the screen and do not ask for it.",
    });
  }

  if (body.webSearchAllowed !== true) {
    content.push({
      type: "text",
      text: "No web search tool is attached this time. Answer from what you know if you can, or reply with only @@NEEDWEB@@ if the answer truly depends on current information.",
    });
  }

  content.push({ type: "text", text: body.transcript });

  const history: Anthropic.MessageParam[] = (body.history ?? []).map((exchange) => ({
    role: exchange.role,
    content: exchange.content,
  }));

  // No SDK retries: the user pressing the key again is a faster, cheaper retry than
  // making them wait out a second attempt in silence (SC-001 allows 7s at p95).
  const client = new Anthropic({
    apiKey: env.CHAT_PROVIDER_API_KEY.trim(),
    timeout: UPSTREAM_TIMEOUT_MILLISECONDS,
    maxRetries: 0,
  });
  const maxSearches = Number.parseInt(env.WEB_SEARCH_MAX_USES ?? "2", 10);
  const upstream = client.messages.stream({
    model: env.CHAT_MODEL,
    max_tokens: 2048,
    // A 1 hour TTL, not the 5 minute default: a desktop companion is used in bursts spread
    // across a session. Caching is ignored entirely if this block drops below 1024 tokens.
    system: [{ type: "text", text: SYSTEM_PROMPT, cache_control: { type: "ephemeral", ttl: "1h" } }],
    output_config: { effort: "low" },
    // Attached per request, never by default: the tool definition is ~2,700 input tokens, which
    // is more than this prompt, and it would be paid on every turn whether or not it is used.
    ...(body.webSearchAllowed === true && Number.isFinite(maxSearches) && maxSearches > 0
      ? { tools: [{ type: "web_search_20250305", name: "web_search", max_uses: maxSearches }] }
      : {}),
    messages: [...history, { role: "user", content }],
  });

  // Pull the first event before committing to a 200 so upstream failures still map to a JSON error.
  const events = upstream[Symbol.asyncIterator]();
  let first: IteratorResult<Anthropic.MessageStreamEvent>;
  try {
    first = await events.next();
  } catch (error) {
    if (isTimeout(error) || error instanceof Anthropic.APIConnectionTimeoutError) {
      return errorResponse("provider_timeout", 504);
    }
    return errorResponse("provider_unavailable", 502);
  }

  const encoder = new TextEncoder();
  const splitter = new DesignationSplitter();
  const stream = new ReadableStream<Uint8Array>({
    async start(controller) {
      const send = (payload: unknown) => controller.enqueue(encoder.encode(`data: ${JSON.stringify(payload)}\n\n`));
      const usage: Record<string, number | string> = { displays: body.displays.length };
      const forward = (event: Anthropic.MessageStreamEvent) => {
        if (event.type === "content_block_delta" && event.delta.type === "text_delta") {
          const safe = splitter.push(event.delta.text);
          if (safe.length > 0) {
            send({ delta: safe });
          }
        } else if (event.type === "message_start") {
          usage.input = event.message.usage.input_tokens;
          usage.cacheRead = event.message.usage.cache_read_input_tokens ?? 0;
          usage.cacheWrite = event.message.usage.cache_creation_input_tokens ?? 0;
        } else if (event.type === "message_delta") {
          usage.output = event.usage.output_tokens;
          // "max_tokens" here is the one thing that separates a model writing filler from a
          // designation cut in half mid-JSON, which parses as no action at all and says nothing.
          if (event.delta.stop_reason) {
            usage.stop = event.delta.stop_reason;
          }
        }
      };
      try {
        let current = first;
        while (!current.done) {
          forward(current.value);
          current = await events.next();
        }
      } catch {
        // Mid-stream failure: finish with whatever narration arrived rather than leaving the client hanging.
      }
      console.log("chat.usage", JSON.stringify(usage));
      const { trailingText, pointingTarget, actions, needsScreen, needsWebSearch, awaitingReply } = splitter.finish();
      if (trailingText.length > 0 && !needsScreen && !needsWebSearch) {
        send({ delta: trailingText });
      }
      send({ done: true, pointingTarget, actions, needsScreen, needsWebSearch, awaitingReply });
      controller.close();
    },
  });

  return new Response(stream, {
    headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" },
  });
}

async function handleTextToSpeech(request: Request, env: Env): Promise<Response> {
  let body: { text?: unknown; previousText?: unknown };
  try {
    body = await request.json<{ text?: unknown; previousText?: unknown }>();
  } catch {
    return errorResponse("invalid_request", 400);
  }
  if (typeof body.text !== "string" || body.text.trim() === "") {
    return errorResponse("invalid_request", 400);
  }
  const previousText = typeof body.previousText === "string" ? body.previousText : undefined;

  const outputFormat = env.TEXT_TO_SPEECH_OUTPUT_FORMAT || DEFAULT_TEXT_TO_SPEECH_OUTPUT_FORMAT;
  let upstream: Response;
  try {
    upstream = await fetch(textToSpeechUrl(env.TEXT_TO_SPEECH_VOICE, outputFormat), {
      method: "POST",
      headers: {
        "xi-api-key": env.TEXT_TO_SPEECH_PROVIDER_API_KEY.trim(),
        "Content-Type": "application/json",
      },
      body: JSON.stringify(textToSpeechBody(body.text, previousText, env.TEXT_TO_SPEECH_MODEL)),
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MILLISECONDS),
    });
  } catch (error) {
    return errorResponse(isTimeout(error) ? "provider_timeout" : "provider_unavailable", isTimeout(error) ? 504 : 502);
  }
  if (!upstream.ok || upstream.body === null) {
    return errorResponse("provider_unavailable", 502);
  }
  // Raw PCM is headerless, so the sample rate travels in the content type; anything else is MP3.
  const pcm = /^pcm_(\d+)$/.exec(outputFormat);
  const contentType = pcm ? `audio/L16; rate=${pcm[1]}` : "audio/mpeg";
  return new Response(upstream.body, { headers: { "Content-Type": contentType } });
}

async function handleTranscribeToken(env: Env): Promise<Response> {
  let upstream: Response;
  try {
    upstream = await fetch(TRANSCRIBE_TOKEN_URL, {
      headers: { Authorization: env.SPEECH_TO_TEXT_PROVIDER_API_KEY.trim() },
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MILLISECONDS),
    });
  } catch (error) {
    return errorResponse(isTimeout(error) ? "provider_timeout" : "provider_unavailable", isTimeout(error) ? 504 : 502);
  }
  if (!upstream.ok) {
    return errorResponse("provider_unavailable", 502);
  }
  const minted = await upstream.json<{ token?: string; expires_in_seconds?: number }>();
  if (typeof minted.token !== "string") {
    return errorResponse("provider_unavailable", 502);
  }
  return jsonResponse({
    token: minted.token,
    expiresInSeconds: minted.expires_in_seconds ?? TRANSCRIBE_TOKEN_TTL_SECONDS,
    endpoint: TRANSCRIBE_STREAM_ENDPOINT,
  });
}

// ---------------------------------------------------------------------------------------------
// Music: the user's own Spotify account, linked once through the browser.
//
// The refresh token lives in KV here and never reaches the desktop client, which only ever holds
// an opaque install id (Constitution Principle I). That id is effectively a bearer token for this
// account's playback, so it is a random GUID and is validated before it touches a KV key.
// ---------------------------------------------------------------------------------------------

interface StoredTokens {
  refreshToken: string;
  accessToken: string;
  expiresAt: number;
}

class MusicError extends Error {
  constructor(readonly code: ErrorCode, readonly status = 400) {
    super(code);
  }
}

function musicConfigured(env: Env): boolean {
  return Boolean(env.SPOTIFY_CLIENT_ID && env.SPOTIFY_CLIENT_SECRET);
}

function basicAuth(env: Env): string {
  return btoa(`${env.SPOTIFY_CLIENT_ID!.trim()}:${env.SPOTIFY_CLIENT_SECRET!.trim()}`);
}

function redirectUri(request: Request): string {
  return `${new URL(request.url).origin}/spotify/callback`;
}

function tokenKey(installId: string): string {
  return `spotify:${installId}`;
}

async function readInstallId(request: Request): Promise<string> {
  let body: { installId?: unknown };
  try {
    body = await request.json<{ installId?: unknown }>();
  } catch {
    throw new MusicError("invalid_request", 400);
  }
  if (typeof body.installId !== "string" || !INSTALL_ID_PATTERN.test(body.installId)) {
    throw new MusicError("invalid_request", 400);
  }
  return body.installId.toLowerCase();
}

/** Body is read twice on some routes, so callers that need more than the id pass it in. */
async function readMusicBody<T>(request: Request): Promise<T & { installId: string }> {
  let body: Record<string, unknown>;
  try {
    body = await request.json<Record<string, unknown>>();
  } catch {
    throw new MusicError("invalid_request", 400);
  }
  const installId = body.installId;
  if (typeof installId !== "string" || !INSTALL_ID_PATTERN.test(installId)) {
    throw new MusicError("invalid_request", 400);
  }
  return { ...(body as T), installId: installId.toLowerCase() };
}

async function accessTokenFor(env: Env, installId: string): Promise<string> {
  if (!musicConfigured(env)) {
    throw new MusicError("music_not_configured");
  }

  const stored = await env.MUSIC_TOKENS.get<StoredTokens>(tokenKey(installId), "json");
  if (stored === null) {
    throw new MusicError("music_not_linked");
  }
  if (stored.expiresAt > Date.now() + 30_000) {
    return stored.accessToken;
  }

  const refreshed = await fetch("https://accounts.spotify.com/api/token", {
    method: "POST",
    headers: { Authorization: `Basic ${basicAuth(env)}`, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "refresh_token", refresh_token: stored.refreshToken }).toString(),
    signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MILLISECONDS),
  });
  if (!refreshed.ok) {
    // A refresh token Spotify no longer accepts means the user revoked access; make them relink.
    await env.MUSIC_TOKENS.delete(tokenKey(installId));
    throw new MusicError("music_not_linked");
  }

  const token = await refreshed.json<{ access_token?: string; expires_in?: number; refresh_token?: string }>();
  if (typeof token.access_token !== "string") {
    throw new MusicError("music_unavailable", 502);
  }

  const next: StoredTokens = {
    refreshToken: token.refresh_token ?? stored.refreshToken,
    accessToken: token.access_token,
    expiresAt: Date.now() + (token.expires_in ?? 3600) * 1000,
  };
  await env.MUSIC_TOKENS.put(tokenKey(installId), JSON.stringify(next));
  return next.accessToken;
}

async function spotify(
  env: Env,
  installId: string,
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const token = await accessTokenFor(env, installId);
  let response: Response;
  try {
    response = await fetch(`${SPOTIFY_API}${path}`, {
      ...init,
      headers: { ...(init.headers as Record<string, string>), Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MILLISECONDS),
    });
  } catch (error) {
    throw new MusicError(isTimeout(error) ? "provider_timeout" : "music_unavailable", 502);
  }

  if (response.status === 403) {
    // Playback control is a Premium-only capability; everything else 403s far more rarely.
    throw new MusicError("music_needs_premium");
  }
  if (response.status === 404) {
    throw new MusicError("music_no_device");
  }
  if (response.status === 401) {
    throw new MusicError("music_not_linked");
  }
  return response;
}

/** Spotify refuses playback with no active device; waking the most recent one is the fix. */
async function activeDeviceId(env: Env, installId: string): Promise<string | null> {
  const devices = await spotify(env, installId, "/me/player/devices");
  if (!devices.ok) {
    return null;
  }
  const list = (await devices.json<{ devices?: { id?: string; is_active?: boolean }[] }>()).devices ?? [];
  const active = list.find((device) => device.is_active === true) ?? list[0];
  return active?.id ?? null;
}

async function handleMusicStatus(request: Request, env: Env): Promise<Response> {
  const installId = await readInstallId(request);
  if (!musicConfigured(env)) {
    return jsonResponse({ configured: false, linked: false, linkUrl: null });
  }
  const stored = await env.MUSIC_TOKENS.get(tokenKey(installId));
  return jsonResponse({
    configured: true,
    linked: stored !== null,
    linkUrl: `${new URL(request.url).origin}/spotify/login?install=${encodeURIComponent(installId)}`,
  });
}

async function handleMusicState(request: Request, env: Env): Promise<Response> {
  const installId = await readInstallId(request);
  const player = await spotify(env, installId, "/me/player");
  if (player.status === 204) {
    return jsonResponse({ playing: null });
  }
  if (!player.ok) {
    throw new MusicError("music_unavailable", 502);
  }

  const state = await player.json<{
    is_playing?: boolean;
    device?: { name?: string; volume_percent?: number };
    item?: { name?: string; artists?: { name?: string }[] };
  }>();
  return jsonResponse({
    playing: {
      title: state.item?.name ?? "",
      artist: state.item?.artists?.[0]?.name ?? "",
      isPlaying: state.is_playing === true,
      deviceName: state.device?.name ?? "",
      volumePercent: typeof state.device?.volume_percent === "number" ? state.device.volume_percent : -1,
    },
  });
}

async function handleMusicPlay(request: Request, env: Env): Promise<Response> {
  const body = await readMusicBody<{ query?: unknown; queueOnly?: unknown }>(request);
  if (typeof body.query !== "string" || body.query.trim() === "") {
    throw new MusicError("invalid_request", 400);
  }

  const found = await spotify(
    env,
    body.installId,
    `/search?type=track&limit=1&q=${encodeURIComponent(body.query.trim())}`,
  );
  if (!found.ok) {
    throw new MusicError("music_unavailable", 502);
  }
  const track = (
    await found.json<{ tracks?: { items?: { uri?: string; name?: string; artists?: { name?: string }[] }[] } }>()
  ).tracks?.items?.[0];
  if (track === undefined || typeof track.uri !== "string") {
    throw new MusicError("music_no_match");
  }

  const device = await activeDeviceId(env, body.installId);
  const suffix = device === null ? "" : `?device_id=${encodeURIComponent(device)}`;
  const started = body.queueOnly === true
    ? await spotify(env, body.installId, `/me/player/queue?uri=${encodeURIComponent(track.uri)}${device === null ? "" : `&device_id=${encodeURIComponent(device)}`}`, { method: "POST" })
    : await spotify(env, body.installId, `/me/player/play${suffix}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ uris: [track.uri] }),
      });

  if (!started.ok && started.status !== 204) {
    throw new MusicError("music_unavailable", 502);
  }

  return jsonResponse({ title: track.name ?? "", artist: track.artists?.[0]?.name ?? "" });
}

async function handleMusicTransport(request: Request, env: Env): Promise<Response> {
  const body = await readMusicBody<{ command?: unknown }>(request);
  if (typeof body.command !== "string") {
    throw new MusicError("invalid_request", 400);
  }

  const command = body.command.toLowerCase();
  let path: string;
  let method: string;
  if (command === "next" || command === "skip") {
    path = "/me/player/next";
    method = "POST";
  } else if (command === "previous" || command === "back") {
    path = "/me/player/previous";
    method = "POST";
  } else if (command === "pause" || command === "stop") {
    path = "/me/player/pause";
    method = "PUT";
  } else if (command === "play" || command === "resume") {
    path = "/me/player/play";
    method = "PUT";
  } else if (command === "shuffle") {
    path = "/me/player/shuffle?state=true";
    method = "PUT";
  } else if (command === "repeat") {
    path = "/me/player/repeat?state=context";
    method = "PUT";
  } else if (command === "playpause" || command === "toggle") {
    const player = await spotify(env, body.installId, "/me/player");
    const playing = player.status === 200
      && (await player.json<{ is_playing?: boolean }>()).is_playing === true;
    path = playing ? "/me/player/pause" : "/me/player/play";
    method = "PUT";
  } else {
    throw new MusicError("invalid_request", 400);
  }

  const response = await spotify(env, body.installId, path, { method });
  if (!response.ok && response.status !== 204) {
    throw new MusicError("music_unavailable", 502);
  }
  return jsonResponse({ ok: true });
}

async function handleMusicVolume(request: Request, env: Env): Promise<Response> {
  const body = await readMusicBody<{ amount?: unknown; relative?: unknown }>(request);
  if (typeof body.amount !== "number" || !Number.isFinite(body.amount)) {
    throw new MusicError("invalid_request", 400);
  }

  let wanted = Math.round(body.amount);
  if (body.relative === true) {
    // Reading the current level here is the whole point: the model cannot see it, so "turn it
    // down" would otherwise have to guess an absolute number.
    const player = await spotify(env, body.installId, "/me/player");
    const current = player.status === 200
      ? (await player.json<{ device?: { volume_percent?: number } }>()).device?.volume_percent ?? 50
      : 50;
    wanted = current + wanted;
  }
  wanted = Math.max(0, Math.min(100, wanted));

  const device = await activeDeviceId(env, body.installId);
  const suffix = device === null ? "" : `&device_id=${encodeURIComponent(device)}`;
  const response = await spotify(env, body.installId, `/me/player/volume?volume_percent=${wanted}${suffix}`, {
    method: "PUT",
  });
  if (!response.ok && response.status !== 204) {
    throw new MusicError("music_unavailable", 502);
  }
  return jsonResponse({ volumePercent: wanted });
}

async function handleSpotifyLogin(request: Request, env: Env): Promise<Response> {
  if (!musicConfigured(env)) {
    return errorResponse("music_not_configured", 400);
  }
  const install = new URL(request.url).searchParams.get("install") ?? "";
  if (!INSTALL_ID_PATTERN.test(install)) {
    return errorResponse("invalid_request", 400);
  }

  // A random state tied to the install id in KV: no signing needed, and it expires by itself.
  const state = crypto.randomUUID();
  await env.MUSIC_TOKENS.put(`state:${state}`, install.toLowerCase(), { expirationTtl: LINK_STATE_TTL_SECONDS });

  const authorize = new URL("https://accounts.spotify.com/authorize");
  authorize.searchParams.set("client_id", env.SPOTIFY_CLIENT_ID!.trim());
  authorize.searchParams.set("response_type", "code");
  authorize.searchParams.set("redirect_uri", redirectUri(request));
  authorize.searchParams.set("scope", SPOTIFY_SCOPES);
  authorize.searchParams.set("state", state);
  return Response.redirect(authorize.toString(), 302);
}

function closingPage(message: string): Response {
  return new Response(
    `<!doctype html><meta charset="utf-8"><title>Winly</title>` +
      `<body style="font:16px system-ui;padding:3rem;max-width:34rem;margin:auto">` +
      `<h1 style="font-size:1.25rem">Winly</h1><p>${message}</p></body>`,
    { headers: { "Content-Type": "text/html; charset=utf-8" } },
  );
}

async function handleSpotifyCallback(request: Request, env: Env): Promise<Response> {
  if (!musicConfigured(env)) {
    return closingPage("This backend has no Spotify credentials configured.");
  }

  const parameters = new URL(request.url).searchParams;
  const code = parameters.get("code");
  const state = parameters.get("state");
  if (code === null || state === null) {
    return closingPage("Spotify did not grant access. You can close this tab and try again.");
  }

  const install = await env.MUSIC_TOKENS.get(`state:${state}`);
  if (install === null) {
    return closingPage("That sign-in link expired. Press Connect Spotify in Winly again.");
  }
  await env.MUSIC_TOKENS.delete(`state:${state}`);

  const exchanged = await fetch("https://accounts.spotify.com/api/token", {
    method: "POST",
    headers: { Authorization: `Basic ${basicAuth(env)}`, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "authorization_code",
      code,
      redirect_uri: redirectUri(request),
    }).toString(),
    signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MILLISECONDS),
  });
  if (!exchanged.ok) {
    return closingPage("Spotify refused the sign-in. You can close this tab and try again.");
  }

  const token = await exchanged.json<{ access_token?: string; refresh_token?: string; expires_in?: number }>();
  if (typeof token.access_token !== "string" || typeof token.refresh_token !== "string") {
    return closingPage("Spotify's answer was not what Winly expected. Please try again.");
  }

  const stored: StoredTokens = {
    refreshToken: token.refresh_token,
    accessToken: token.access_token,
    expiresAt: Date.now() + (token.expires_in ?? 3600) * 1000,
  };
  await env.MUSIC_TOKENS.put(tokenKey(install), JSON.stringify(stored));
  return closingPage("Spotify is connected. You can close this tab and talk to Winly.");
}

export default {
  async fetch(request, env): Promise<Response> {
    const path = new URL(request.url).pathname;

    // The two browser-facing OAuth steps are the only GETs; everything else is a client POST.
    if (request.method === "GET") {
      if (path === "/spotify/login") {
        return handleSpotifyLogin(request, env);
      }
      if (path === "/spotify/callback") {
        return handleSpotifyCallback(request, env);
      }
      return errorResponse("invalid_request", 405);
    }

    if (request.method !== "POST") {
      return errorResponse("invalid_request", 405);
    }

    // Every POST spends money — provider credits, or someone's Spotify account. One gate, above
    // the routing table, so a route added later cannot be added unprotected.
    if (!isAuthorized(request, env)) {
      return errorResponse("unauthorized", 401);
    }

    try {
      switch (path) {
        case "/chat":
          return await handleChat(request, env);
        case "/tts":
          return await handleTextToSpeech(request, env);
        case "/transcribe-token":
          return await handleTranscribeToken(env);
        case "/music/status":
          return await handleMusicStatus(request, env);
        case "/music/state":
          return await handleMusicState(request, env);
        case "/music/play":
          return await handleMusicPlay(request, env);
        case "/music/transport":
          return await handleMusicTransport(request, env);
        case "/music/volume":
          return await handleMusicVolume(request, env);
        default:
          return errorResponse("invalid_request", 404);
      }
    } catch (error) {
      if (error instanceof MusicError) {
        return errorResponse(error.code, error.status);
      }
      console.log("route.failure", path, String(error));
      return errorResponse("provider_unavailable", 502);
    }
  },
} satisfies ExportedHandler<Env>;
