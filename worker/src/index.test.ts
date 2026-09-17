import { describe, expect, it } from "vitest";
import { DesignationSplitter, isAuthorized, textToSpeechBody, type Env } from "./index";

/**
 * The designation parser is the only place actions are extracted: the desktop client carries
 * through whatever arrives in `actions` and never re-parses the text (see DesktopActionParser).
 * These are the cases that used to be covered on the client side, before that recovery path was
 * deleted as unreachable.
 */
function split(answer: string) {
  const splitter = new DesignationSplitter();
  const spoken = splitter.push(answer);
  const finished = splitter.finish();
  return { ...finished, spoken: (spoken + finished.trailingText).trim() };
}

const open = '@@DO {"action":"open","target":"Spotify"}@@';

describe("a composed body with real line breaks in it", () => {
  // A model asked to write an email puts the line breaks in, and sometimes writes them as
  // actual newlines rather than \n escapes. JSON forbids a raw control character inside a
  // string, so JSON.parse threw and the designation was dropped -- while the strip regex
  // ([\s\S] crosses newlines) still removed the tag, so the user heard "writing that up now"
  // and got nothing. Silent, and indistinguishable from the model never emitting it.
  const raw =
    '@@DO {"action":"type","target":"Dear Sam,' + String.fromCharCode(10) + String.fromCharCode(10) +
    'The release slipped.' + String.fromCharCode(10) + String.fromCharCode(10) + 'Best"}@@';

  it("still yields the action", () => {
    const result = split("Writing that up now. @@NOPOINT@@ " + raw);
    expect(result.actions).toHaveLength(1);
    expect(result.actions[0].action).toBe("type");
  });

  it("keeps the line breaks it was given", () => {
    const target = split("Writing that up now. @@NOPOINT@@ " + raw).actions[0].target;
    expect(target.split(String.fromCharCode(10)).length).toBe(5);
    expect(target.startsWith("Dear Sam,")).toBe(true);
    expect(target.endsWith("Best")).toBe(true);
  });

  it("does not leak the tag into the spoken answer", () => {
    expect(split("Writing that up now. @@NOPOINT@@ " + raw).spoken).toBe("Writing that up now.");
  });
});

const play = '@@DO {"action":"play","target":"jazz"}@@';

describe("DesignationSplitter", () => {
  it("keeps every designation in the order the model wrote them", () => {
    const { actions, spoken } = split(`Opening Spotify and putting some jazz on. ${open} ${play}`);

    expect(actions.map((one) => one.action)).toEqual(["open", "play"]);
    expect(actions[1].target).toBe("jazz");
    expect(spoken).toBe("Opening Spotify and putting some jazz on.");
  });

  it("discards a malformed designation without voiding the valid ones around it", () => {
    const { actions } = split(`On it. ${open} @@DO {oops}@@ @@DO {"action":"media","target":"next"}@@`);

    expect(actions.map((one) => one.action)).toEqual(["open", "media"]);
  });

  it("drops a designation naming a verb the client cannot act on", () => {
    const { actions } = split(`Sure. @@DO {"action":"delete","target":"everything"}@@ ${play}`);

    expect(actions.map((one) => one.action)).toEqual(["play"]);
  });

  // Not capped here: the client caps, so that it can tell the user its request was truncated.
  it("forwards every designation the model wrote", () => {
    const { actions } = split(`On it. ${open.repeat(1)}${play.repeat(7)}`);

    expect(actions).toHaveLength(8);
  });

  it("never lets a designation through into the spoken text", () => {
    const { spoken, trailingText } = split(`Done. ${open} ${play}`);

    expect(spoken).toBe("Done.");
    expect(trailingText).not.toContain("@@");
  });

  it("holds a designation back while it is still arriving, so half a tag is never spoken", () => {
    const splitter = new DesignationSplitter();

    expect(splitter.push("Opening Spotify. ")).toBe("Opening Spotify. ");
    expect(splitter.push('@@DO {"action":"open",')).toBe("");
    expect(splitter.push('"target":"Spotify"}@@')).toBe("");
    expect(splitter.finish().actions.map((one) => one.action)).toEqual(["open"]);
  });

  it("reports an answer with no designation at all as having no actions", () => {
    const { actions, spoken } = split("That button saves your work.");

    expect(actions).toEqual([]);
    expect(spoken).toBe("That button saves your work.");
  });
});

/**
 * The gate every POST passes through. Its one job is to be wrong in the safe direction when
 * something is missing, which is exactly the case a live smoke test cannot cover.
 */
function ask(token: string | undefined, header?: string) {
  return isAuthorized(
    new Request("https://winly.example/chat", {
      method: "POST",
      headers: header ? { Authorization: header } : {},
    }),
    { WINLY_CLIENT_TOKEN: token } as unknown as Env,
  );
}

describe("@@LISTEN@@", () => {
  it("marks the answer as awaiting a reply and never speaks the tag", () => {
    const { awaitingReply, spoken } = split("I didn't catch that. Could you say it again? @@LISTEN@@ @@NOPOINT@@");

    expect(awaitingReply).toBe(true);
    expect(spoken).toBe("I didn't catch that. Could you say it again?");
  });

  it("is absent from an ordinary answer, so the microphone stays shut", () => {
    expect(split("It's the settings panel. @@NOPOINT@@").awaitingReply).toBe(false);
  });

  it("survives arriving alongside an action, which is parsed last", () => {
    const { awaitingReply, actions, spoken } = split(`Opening it. ${open} @@LISTEN@@`);

    expect(awaitingReply).toBe(true);
    expect(actions.map((one) => one.action)).toEqual(["open"]);
    expect(spoken).toBe("Opening it.");
  });
});

describe("isAuthorized", () => {
  it("accepts the configured token", () => {
    expect(ask("s3cret", "Bearer s3cret")).toBe(true);
  });

  it("refuses a wrong token, a missing header and a bare token without the scheme", () => {
    expect(ask("s3cret", "Bearer nope!!")).toBe(false);
    expect(ask("s3cret")).toBe(false);
    expect(ask("s3cret", "s3cret")).toBe(false);
  });

  // The failure this whole check exists to prevent: a secret that never reached the live
  // deployment must lock the door, not leave it open and look like it worked.
  it("refuses everything when no token is configured", () => {
    expect(ask(undefined, "Bearer anything")).toBe(false);
    expect(ask("", "Bearer ")).toBe(false);
  });
});

describe("textToSpeechBody", () => {
  it("carries what was already spoken, so the voice does not restart at every sentence seam", () => {
    const body = textToSpeechBody("It also closes the dialog.", "It saves your file. ", "eleven_flash_v2_5");

    expect(body.previous_text).toBe("It saves your file. ");
    expect(body.language_code).toBe("en");
  });

  it("omits the context on the first chunk, and the language hint on a model that would reject it", () => {
    const body = textToSpeechBody("It saves your file.", undefined, "eleven_multilingual_v2");

    expect(body).not.toHaveProperty("previous_text");
    expect(body).not.toHaveProperty("language_code");
  });

  it("sends only the tail of a long answer as context", () => {
    const body = textToSpeechBody("And that is it.", "x".repeat(900), "eleven_flash_v2_5");

    expect(body.previous_text).toHaveLength(500);
  });
});
