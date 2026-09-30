import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import { clickTargetIssue, resolveClickPoint } from "../src/click-target.js";
import type { ActionRequest, ActionResult, UnityConnection } from "../src/connection.js";
import type { FoldedPulseState, PulseObject } from "../src/pulse.js";
import { PulseStore } from "../src/pulse.js";
import { expandActions, performActionSchema, registerTools } from "../src/tools.js";

function object(id: number, selector: string, extra: Partial<PulseObject> = {}): PulseObject {
  return {
    id,
    path: selector,
    selector,
    rect: { x: 100, y: 200, w: 40, h: 20 },
    ...extra,
  };
}

function state(active: PulseObject[], deactive: PulseObject[] = []): FoldedPulseState {
  return {
    reading: 1, frame: 10, scene: "Battle", schema: 1, statics: [], active, deactive,
    watching: 0, unresolved: 0, unwatchable: 0, changed: [], gone: [],
  };
}

const button = object(7, "Canvas[0]/Button[0]");
const ok = (folded: FoldedPulseState, target: Parameters<typeof resolveClickPoint>[0]) => resolveClickPoint(target, folded, undefined);

test("a coordinate click needs no reading and passes the point through", () => {
  assert.deepEqual(resolveClickPoint({ x: 5, y: 6 }, undefined, undefined), { ok: true, point: { x: 5, y: 6 } });
});

test("an id click aims at the center of the rect", () => {
  assert.deepEqual(ok(state([button]), { targetId: 7 }), { ok: true, point: { x: 120, y: 210 } });
});

test("a selector click matches the whole selector, not a fragment", () => {
  assert.deepEqual(ok(state([button]), { selector: "Canvas[0]/Button[0]" }), { ok: true, point: { x: 120, y: 210 } });
  const miss = ok(state([button]), { selector: "Button" });
  assert.equal(miss.ok, false);
});

test("a selector matching two scenes lists both and refuses", () => {
  const other = object(9, "Canvas[0]/Button[0]", { scene: "Menu" });
  const result = ok(state([button, other]), { selector: "Canvas[0]/Button[0]" });
  assert.equal(result.ok, false);
  if (!result.ok) {
    assert.match(result.error, /matches 2 objects/);
    assert.match(result.error, /Menu\/Canvas\[0\]\/Button\[0\] \(id 9\)/);
  }
});

test("targets that cannot be hit fail before anything is pressed", () => {
  const cases: Array<[string, FoldedPulseState, RegExp]> = [
    ["inactive", state([], [button]), /not active/],
    ["off screen", state([object(7, "A[0]", { onScreen: false })]), /off screen/],
    ["covered", state([object(7, "A[0]", { covered: true })]), /covered/],
    ["no rect", state([object(7, "A[0]", { rect: undefined })]), /no on-screen area/],
    ["empty rect", state([object(7, "A[0]", { rect: { x: 1, y: 1, w: 0, h: 5 } })]), /no on-screen area/],
    ["unknown id", state([]), /no object with id 7/],
  ];
  for (const [name, folded, message] of cases) {
    const result = ok(folded, { targetId: 7 });
    assert.equal(result.ok, false, name);
    if (!result.ok) assert.match(result.error, message, name);
  }
});

test("a stale reading and a missing reading refuse id and selector clicks", () => {
  const stale = resolveClickPoint({ targetId: 7 }, state([button]), "Call start_readings.");
  assert.equal(stale.ok, false);
  const none = resolveClickPoint({ selector: "A[0]" }, undefined, undefined);
  assert.equal(none.ok, false);
});

test("clickTargetIssue demands exactly one way to aim", () => {
  assert.equal(clickTargetIssue({ targetId: 1 }), undefined);
  assert.equal(clickTargetIssue({ selector: "A[0]" }), undefined);
  assert.equal(clickTargetIssue({ x: 1, y: 2 }), undefined);
  assert.ok(clickTargetIssue({}));
  assert.ok(clickTargetIssue({ x: 1 }));
  assert.ok(clickTargetIssue({ targetId: 1, selector: "A[0]" }));
  assert.ok(clickTargetIssue({ targetId: 1, x: 1, y: 2 }));
});

test("expandActions turns a click into move, press, release around its neighbours", () => {
  const store = new PulseStore();
  const actions = [
    performActionSchema.parse({ method: "key_down", key: "W" }),
    performActionSchema.parse({ method: "click", x: 30, y: 40 }),
  ];
  const expanded = expandActions(actions, store);
  assert.ok(expanded.ok);
  if (expanded.ok) {
    assert.deepEqual(expanded.wire.map((action) => action.method), ["key_down", "move_mouse", "mouse_down", "mouse_up"]);
    assert.deepEqual(expanded.wire[1]?.params, [30, 40]);
    assert.deepEqual(expanded.wire[2]?.params, [0]);
    assert.deepEqual(expanded.wire[3]?.params, [0]);
  }
});

test("expandActions sends nothing when a click cannot be resolved", () => {
  const expanded = expandActions([performActionSchema.parse({ method: "click", targetId: 7 })], new PulseStore());
  assert.equal(expanded.ok, false);
});

type CallToolHandler = (
  request: { method: string; params: { name: string; arguments: Record<string, unknown> } },
  extra: { signal: AbortSignal },
) => Promise<{ isError?: boolean; content: Array<{ text?: string }> }>;

async function callTool(name: string, args: Record<string, unknown>) {
  const sent: ActionRequest[][] = [];
  const connection = {
    endpoint: "ws://127.0.0.1:17311/ws",
    isConnected: () => true,
    async ensureConnected(): Promise<void> {},
    async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
      sent.push(actions);
      return actions.map((action) => ({ id: action.id, success: true }));
    },
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  registerTools(server, connection, new PulseStore());
  const handler = (server.server as unknown as { _requestHandlers?: Map<string, CallToolHandler> })
    ._requestHandlers?.get("tools/call");
  assert.ok(handler !== undefined, "tools/call handler missing");
  const response = await handler(
    { method: "tools/call", params: { name, arguments: args } },
    { signal: new AbortController().signal },
  );
  return { sent, response };
}

test("the click tool sends move_mouse, mouse_down, mouse_up as one batch", async () => {
  const { sent } = await callTool("click", { x: 12.5, y: 30 });
  assert.equal(sent.length, 1);
  assert.deepEqual(sent[0]?.map((a) => [a.method, a.params]), [
    ["move_mouse", [12.5, 30]],
    ["mouse_down", [0]],
    ["mouse_up", [0]],
  ]);
});

test("the click tool refuses a missing target without touching Unity", async () => {
  const { sent, response } = await callTool("click", {});
  assert.equal(sent.length, 0);
  assert.equal(response.isError, true);
});

test("the removed pointer_click tool is gone", async () => {
  const { sent, response } = await callTool("pointer_click", { targetId: 1 });
  assert.equal(sent.length, 0);
  assert.equal(response.isError, true);
});
