import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { UnityConnection } from "../src/connection.js";
import { PulseStore, type PulseFrame, type PulseObject, type PulseStatic } from "../src/pulse.js";
import { nameLooksSecret, redactSecrets, valueLooksSecret } from "../src/secrets.js";
import { registerTools } from "../src/tools.js";

/// #72 의 회귀 test.
///
/// 실제 인증 값은 쓰지 않는다. 아래 토큰은 `{"fake":"test"}`, `{"not":"real"}`, `fake-signature` 를
/// base64url 로 이은 JWT 모양의 가짜 값이다.
const FAKE_JWT = ["eyJmYWtlIjoidGVzdCJ9", "eyJub3QiOiJyZWFsIn0", "ZmFrZS1zaWduYXR1cmU"].join(".");
const FAKE_API_KEY = "test0000fake1111ABCDEFGHIJKLMNOPqrstuvwx";

function object(id: number, selector: string, on: string, members: { member: string; value: unknown }[]): PulseObject {
  return {
    id, path: selector.replace(/\[\d+\]/g, ""), selector, scene: "Lobby",
    by: [{ on, m: members as never }],
  };
}

function lobby(statics: PulseStatic[], extra: PulseObject[] = []): PulseFrame {
  return {
    type: "PULSE", id: 1, schema: 2, reading: 1, frame: 10, scene: "Lobby",
    statics,
    active: [
      object(1, "UI[0]/LowerBar[0]/PlayButton[0]", "UnityEngine.UI.Text", [{ member: "text", value: "Play" }]),
      object(2, "UI[0]/LowerBar[0]/Gold[0]", "UnityEngine.UI.Text", [{ member: "text", value: "1200" }]),
      object(3, "Popup[0]/Title[0]", "UnityEngine.UI.Text", [{ member: "text", value: "Daily reward" }]),
      ...extra,
    ],
    deactive: [], whole: true, watching: 3, unresolved: 0, unwatchable: 0,
    changed: [
      "Global.SceneContext::JwtToken",
      "Lobby/UI[0]/LowerBar[0]/Gold[0]|0UnityEngine.UI.Text::text",
      "Lobby/Popup[0]/Title[0]|0UnityEngine.UI.Text::text",
      "scene",
    ],
  };
}

const TOKEN_STATIC: PulseStatic = {
  declaring: "Global.SceneContext", member: "JwtToken", type: "System.String", value: FAKE_JWT,
};

function sceneStateTool(store: PulseStore) {
  const connection = {
    endpoint: "ws://127.0.0.1:17311/ws",
    isConnected: () => true,
    async ensureConnected(): Promise<void> {},
    onDisconnect: () => () => undefined,
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  registerTools(server, connection, store);
  const handler = (server.server as unknown as {
    _requestHandlers?: Map<string, (request: unknown, extra: unknown) => Promise<{ content: { text: string }[] }>>;
  })._requestHandlers?.get("tools/call");
  assert.ok(handler !== undefined, "server.server._requestHandlers no longer carries tools/call");
  return async (name: string, args: Record<string, unknown> = {}) => {
    const result = await handler(
      { method: "tools/call", params: { name, arguments: args } },
      { signal: new AbortController().signal },
    );
    return result.content.map((item) => item.text).join("\n");
  };
}

test("names that point at credentials are recognised, and short words only as whole words", () => {
  for (const name of [
    "Global.SceneContext.JwtToken", "jwt_token", "API-KEY", "UserPassword", "sessionId",
    "Auth.Current", "LoginPanel.PIN", "Client.RefreshToken", "OAuth.ClientSecret",
  ]) {
    assert.equal(nameLooksSecret(name), true, name);
  }
  for (const name of ["text", "Spinner.speed", "Pinned.count", "Author.name", "Hotpot.level", "priceText"]) {
    assert.equal(nameLooksSecret(name), false, name);
  }
});

test("values shaped like credentials are recognised even under an innocent name", () => {
  assert.equal(valueLooksSecret(FAKE_JWT), true);
  assert.equal(valueLooksSecret(`Bearer ${FAKE_API_KEY}`), true);
  assert.equal(valueLooksSecret(FAKE_API_KEY), true);
  assert.equal(valueLooksSecret("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----"), true);

  for (const value of ["Score: 12", "Press Start to Play", "abc123", "Daily reward", "LobbyScene", ""]) {
    assert.equal(valueLooksSecret(value), false, value);
  }
});

test("redaction keeps null and empty strings, and walks into structs and lists", () => {
  assert.equal(redactSecrets("Session.JwtToken", null), null);
  assert.equal(redactSecrets("Session.JwtToken", ""), "");
  assert.deepEqual(redactSecrets("Session.JwtToken", FAKE_JWT), {
    $redacted: true, because: "name", length: FAKE_JWT.length,
  });
  assert.deepEqual(redactSecrets("Account.Current", { nickname: "tester", token: "abc", history: [FAKE_JWT] }), {
    nickname: "tester",
    token: { $redacted: true, because: "name", length: 3 },
    history: [{ $redacted: true, because: "value", length: FAKE_JWT.length }],
  });
});

test("a full get_scene_state never carries the token, and says a value was hidden", async () => {
  const store = new PulseStore();
  store.fold(lobby([TOKEN_STATIC]));
  const call = sceneStateTool(store);

  const response = await call("get_scene_state");

  assert.doesNotMatch(response, new RegExp(FAKE_JWT.split(".")[0]));
  const parsed = JSON.parse(response) as { redaction?: string; statics: PulseStatic[] };
  assert.match(parsed.redaction ?? "", /looked like credentials/);
  assert.deepEqual(parsed.statics[0]?.value, { $redacted: true, because: "name", length: FAKE_JWT.length });
});

test("a token under an innocent static name is hidden by its shape", async () => {
  const store = new PulseStore();
  store.fold(lobby([{ declaring: "Global.SceneContext", member: "Current", type: "System.String", value: FAKE_JWT }]));

  const response = await sceneStateTool(store)("get_scene_state");

  assert.doesNotMatch(response, new RegExp(FAKE_JWT.split(".")[1]));
  assert.match(response, /"because": "value"/);
});

test("a scene without a token carries statics as before and no redaction note", async () => {
  const store = new PulseStore();
  store.fold(lobby([{ declaring: "Global.Stage", member: "Number", type: "System.Int32", value: 3 }]));

  const parsed = JSON.parse(await sceneStateTool(store)("get_scene_state")) as {
    redaction?: string; statics: PulseStatic[];
  };

  assert.equal(parsed.redaction, undefined);
  assert.deepEqual(parsed.statics.map(({ value }) => value), [3]);
});

test("a root-scoped query leaves statics out and says how many it left out", async () => {
  const store = new PulseStore();
  store.fold(lobby([TOKEN_STATIC]));

  const response = await sceneStateTool(store)("get_scene_state", { root: "UI[0]/LowerBar[0]", depth: 2 });
  const parsed = JSON.parse(response) as { statics?: unknown; staticsOmitted?: { count: number; reason: string } };

  assert.equal(parsed.statics, undefined);
  assert.equal(parsed.staticsOmitted?.count, 1);
  assert.match(parsed.staticsOmitted?.reason ?? "", /includeStatics/);
  assert.doesNotMatch(response, /JwtToken/);
});

test("a selector-scoped query keeps only the changed entries of the objects it shows", async () => {
  const store = new PulseStore();
  store.fold(lobby([TOKEN_STATIC]));

  const parsed = JSON.parse(await sceneStateTool(store)("get_scene_state", { selector: "LowerBar" })) as {
    changed: string[]; changedOmitted?: number; statics?: unknown; staticsOmitted?: { count: number };
  };

  assert.deepEqual(parsed.changed, ["Lobby/UI[0]/LowerBar[0]/Gold[0]|0UnityEngine.UI.Text::text"]);
  assert.equal(parsed.changedOmitted, 3);
  assert.equal(parsed.statics, undefined);
  assert.equal(parsed.staticsOmitted?.count, 1);
});

test("statics can still be asked for explicitly, by declaring type, and stay hidden when secret", async () => {
  const store = new PulseStore();
  store.fold(lobby([
    TOKEN_STATIC,
    { declaring: "Global.Stage", member: "Number", type: "System.Int32", value: 3 },
  ]));
  const call = sceneStateTool(store);

  const all = JSON.parse(await call("get_scene_state", { selector: "LowerBar", includeStatics: true })) as {
    statics: PulseStatic[];
  };
  assert.equal(all.statics.length, 2);
  assert.doesNotMatch(JSON.stringify(all), new RegExp(FAKE_JWT.split(".")[0]));

  const picked = JSON.parse(await call("get_scene_state", { root: "UI[0]", staticsDeclaring: "Stage" })) as {
    statics: PulseStatic[];
  };
  assert.deepEqual(picked.statics.map(({ member }) => member), ["Number"]);
});

test("a secret member on an object is hidden in state, history, tree, visible elements, and wait", async () => {
  const store = new PulseStore();
  store.fold(lobby([], [
    object(4, "Login[0]/Form[0]", "Game.LoginForm", [{ member: "password", value: "hunter2-fake" }]),
    object(5, "Debug[0]/Session[0]", "UnityEngine.UI.Text", [{ member: "text", value: FAKE_JWT }]),
  ]));
  const call = sceneStateTool(store);

  const responses = [
    await call("get_scene_state", { includeHistory: true }),
    await call("get_scene_state", { root: "Login" }),
    await call("get_visible_elements", { includeHidden: true }),
    await call("wait_for_condition", {
      memberEquals: [{ selector: "Login[0]/Form[0]", on: "Game.LoginForm", member: "password", equals: "x" }],
      timeoutMilliseconds: 1,
    }),
  ];
  for (const response of responses) {
    assert.doesNotMatch(response, /hunter2-fake/);
    assert.doesNotMatch(response, new RegExp(FAKE_JWT.split(".")[0]));
    assert.match(response, /"redaction"/);
  }
});

test("search keeps finding an object whose label was hidden, and says the label was hidden", async () => {
  const store = new PulseStore();
  store.fold(lobby([], [
    object(5, "Debug[0]/Session[0]", "UnityEngine.UI.Text", [{ member: "text", value: FAKE_JWT }]),
  ]));

  const response = await sceneStateTool(store)("search_targets", { name: "Session" });

  assert.doesNotMatch(response, new RegExp(FAKE_JWT.split(".")[0]));
  const parsed = JSON.parse(response) as { redaction?: string; candidates: { selector: string; displayedText?: string }[] };
  assert.equal(parsed.candidates[0]?.selector, "Debug[0]/Session[0]");
  assert.equal(parsed.candidates[0]?.displayedText, "[redacted]");
  assert.match(parsed.redaction ?? "", /\[redacted\]/);
});

test("the package's own reference shapes, paths, and type names come through unchanged", () => {
  const reference = {
    path: "UI[0]/Canvas[0]/LowerBar[0]/PlayButton[0]", active: true, label: "Play",
  };
  assert.deepEqual(redactSecrets("target", reference), reference);
  assert.deepEqual(redactSecrets("step", { is: "Game.Tutorial.Steps.Step3HandlerForBattle" }), {
    is: "Game.Tutorial.Steps.Step3HandlerForBattle",
  });
  for (const value of [
    "Canvas/Shop/ItemList/Item_Sword_01/Icon",
    "Cards.Spells.FireballSpell2Definition",
    "item_legendary_sword_of_fire_v2_upgraded",
  ]) {
    assert.equal(valueLooksSecret(value), false, value);
  }
});

test("namespace and type words do not hide a component's members; the member name decides", () => {
  const store = new PulseStore();
  store.fold(lobby([], [
    object(6, "Shop[0]/Price[0]", "Game.TokenShop", [{ member: "priceText", value: "120 tokens" }]),
    object(7, "Login[0]/Status[0]", "Company.Auth.LoginScreen", [{ member: "statusMessage", value: "Wrong code" }]),
  ]));

  const values = store.getState()?.active
    .filter(({ id }) => id === 6 || id === 7)
    .map((held) => held.by?.[0]?.members?.[0]?.value);
  assert.deepEqual(values, ["120 tokens", "Wrong code"]);
});

test("a value under a secret-named member is hidden even when its inner key is plain", () => {
  assert.deepEqual(
    redactSecrets("tokenLabel", { path: "UI[0]/Debug[0]/Token[0]", active: true, label: "abc123" }),
    {
      path: "UI[0]/Debug[0]/Token[0]",
      active: true,
      label: { $redacted: true, because: "name", length: 6 },
    },
  );
});

test("statics and changed stay scoped and redacted on a later delta reading too", async () => {
  const store = new PulseStore();
  store.fold(lobby([TOKEN_STATIC]));
  store.fold({
    ...lobby([{ ...TOKEN_STATIC, value: `${FAKE_JWT}x` }]),
    reading: 2, frame: 20, whole: false,
    active: [object(2, "UI[0]/LowerBar[0]/Gold[0]", "UnityEngine.UI.Text", [{ member: "text", value: "1300" }])],
    changed: ["Global.SceneContext::JwtToken", "Lobby/UI[0]/LowerBar[0]/Gold[0]|0UnityEngine.UI.Text::text"],
  });
  const call = sceneStateTool(store);

  const full = await call("get_scene_state");
  assert.doesNotMatch(full, new RegExp(FAKE_JWT.split(".")[0]));

  const scoped = JSON.parse(await call("get_scene_state", { selector: "Gold" })) as {
    changed: string[]; statics?: unknown;
  };
  assert.deepEqual(scoped.changed, ["Lobby/UI[0]/LowerBar[0]/Gold[0]|0UnityEngine.UI.Text::text"]);
  assert.equal(scoped.statics, undefined);
});

test("a scoped query without includeInactive drops the changed entries of inactive objects", async () => {
  const store = new PulseStore();
  store.fold({
    ...lobby([]),
    deactive: [object(8, "UI[0]/LowerBar[0]/Hidden[0]", "UnityEngine.UI.Text", [{ member: "text", value: "x" }])],
    changed: ["Lobby/UI[0]/LowerBar[0]/Hidden[0]|0UnityEngine.UI.Text::text"],
  });
  const call = sceneStateTool(store);

  const shownOnly = JSON.parse(await call("get_scene_state", { selector: "LowerBar" })) as { changed: string[] };
  assert.deepEqual(shownOnly.changed, []);
  const withInactive = JSON.parse(
    await call("get_scene_state", { selector: "LowerBar", includeInactive: true }),
  ) as { changed: string[] };
  assert.deepEqual(withInactive.changed, ["Lobby/UI[0]/LowerBar[0]/Hidden[0]|0UnityEngine.UI.Text::text"]);
});

test("a root that names nothing says so and lists the real top-level names", async () => {
  const store = new PulseStore();
  store.fold(lobby([], [object(4, "Login[0]/Form[0]", "Game.LoginForm", [{ member: "user", value: "u" }])]));
  const call = sceneStateTool(store);

  const missing = JSON.parse(await call("get_scene_state", { root: "Canvas", depth: 1 })) as {
    rootNotFound?: string; topLevelObjects?: string[]; tree: unknown[];
  };
  assert.match(missing.rootNotFound ?? "", /Canvas/);
  assert.ok(missing.topLevelObjects?.includes("Login"));
  assert.deepEqual(missing.tree, []);

  const present = JSON.parse(await call("get_scene_state", { root: "Login", depth: 1 })) as {
    rootNotFound?: string; tree: Array<{ object?: unknown; hasObject?: boolean }>;
  };
  assert.equal(present.rootNotFound, undefined);
  assert.equal(present.tree.some((node) => node.object !== undefined), false);
});
