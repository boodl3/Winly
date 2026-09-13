import { describe, expect, it } from "vitest";
import { DesignationSplitter, isAuthorized, type Env } from "./index";

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
