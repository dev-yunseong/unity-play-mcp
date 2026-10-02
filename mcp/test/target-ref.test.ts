import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import { resolveObjectId, resolvePoint, targetRefIssue, type TargetRef } from "../src/target-ref.js";
import type { ActionRequest, ActionResult, UnityConnection } from "../src/connection.js";
import { PulseStore, type FoldedPulseState, type PulseObject } from "../src/pulse.js";
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
const point = (folded: FoldedPulseState | undefined, target: TargetRef, stale?: string) =>
  resolvePoint(target, folded, stale, "click");

function storeOf(folded: FoldedPulseState): PulseStore {
  return { getState: () => folded, getStaleness: () => undefined, getReadingAgeMs: () => undefined } as unknown as PulseStore;
}

test("a coordinate target needs no reading and passes the point through", () => {
  assert.deepEqual(point(undefined, { x: 5, y: 6 }), { ok: true, value: { x: 5, y: 6 } });
});

test("an id target aims at the center of the rect", () => {
  assert.deepEqual(point(state([button]), { targetId: 7 }), { ok: true, value: { x: 120, y: 210 } });
});

test("a selector target matches the whole selector, not a fragment", () => {
  assert.deepEqual(point(state([button]), { selector: "Canvas[0]/Button[0]" }), { ok: true, value: { x: 120, y: 210 } });
  assert.equal(point(state([button]), { selector: "Button" }).ok, false);
});

test("a selector matching two scenes lists both and refuses", () => {
  const other = object(9, "Canvas[0]/Button[0]", { scene: "Menu" });
  const result = point(state([button, other]), { selector: "Canvas[0]/Button[0]" });
  assert.equal(result.ok, false);
  if (!result.ok) {
    assert.match(result.error, /matches 2 objects/);
    assert.match(result.error, /Menu\/Canvas\[0\]\/Button\[0\] \(id 9\)/);
  }
});

test("targets that cannot be hit fail before anything is pressed, naming the tool", () => {
  const cases: Array<[string, FoldedPulseState, RegExp]> = [
    ["inactive", state([], [button]), /not active/],
    ["off screen", state([object(7, "A[0]", { onScreen: false })]), /off screen/],
    ["covered", state([object(7, "A[0]", { covered: true })]), /covered/],
    ["no rect", state([object(7, "A[0]", { rect: undefined })]), /no on-screen area/],
    ["empty rect", state([object(7, "A[0]", { rect: { x: 1, y: 1, w: 0, h: 5 } })]), /no on-screen area/],
    ["unknown id", state([]), /no object with id 7/],
  ];
  for (const [name, folded, message] of cases) {
    const result = point(folded, { targetId: 7 });
    assert.equal(result.ok, false, name);
    if (!result.ok) {
      assert.match(result.error, message, name);
      assert.match(result.error, /^click:/, name);
    }
  }
});

test("a stale reading and a missing reading refuse id and selector targets", () => {
  assert.equal(point(state([button]), { targetId: 7 }, "Call start_readings.").ok, false);
  assert.equal(point(undefined, { selector: "A[0]" }).ok, false);
});

test("object consumers take an id or a selector and refuse a screen position", () => {
  assert.deepEqual(resolveObjectId({ targetId: 7 }, undefined, undefined, "enter_text"), { ok: true, value: 7 });
  assert.deepEqual(
    resolveObjectId({ selector: "Canvas[0]/Button[0]" }, state([button]), undefined, "enter_text"),
    { ok: true, value: 7 },
  );
  // 꺼진 대상도 id 는 있다. 살아 있는지는 Unity 가 답한다.
  assert.deepEqual(resolveObjectId({ selector: "Canvas[0]/Button[0]" }, state([], [button]), undefined, "enter_text"), { ok: true, value: 7 });
  const byPosition = resolveObjectId({ x: 1, y: 2 }, undefined, undefined, "enter_text");
  assert.equal(byPosition.ok, false);
  assert.equal(resolveObjectId({ selector: "A[0]" }, state([]), undefined, "enter_text").ok, false);
});

test("targetRefIssue demands exactly one way to aim", () => {
  assert.equal(targetRefIssue({ targetId: 1 }, "click"), undefined);
  assert.equal(targetRefIssue({ selector: "A[0]" }, "click"), undefined);
  assert.equal(targetRefIssue({ x: 1, y: 2 }, "click"), undefined);
  assert.match(targetRefIssue({}, "drag to") ?? "", /^drag to requires exactly one/);
  assert.ok(targetRefIssue({ x: 1 }, "click"));
  assert.ok(targetRefIssue({ targetId: 1, selector: "A[0]" }, "click"));
  assert.ok(targetRefIssue({ targetId: 1, x: 1, y: 2 }, "click"));
});

const methodsOf = (wire: Array<{ method: string }>) => wire.map((action) => action.method);

test("expandActions turns click, hover and drag into mouse primitives", () => {
  const store = storeOf(state([button, object(8, "Canvas[0]/Slot[0]", { rect: { x: 300, y: 400, w: 20, h: 20 } })]));
  const expand = (action: unknown) => {
    const result = expandActions([performActionSchema.parse(action)], store);
    assert.ok(result.ok, JSON.stringify(result));
    return result.ok ? result.wire : [];
  };

  assert.deepEqual(methodsOf(expand({ method: "click", x: 30, y: 40 })), ["move_mouse", "mouse_down", "mouse_up"]);
  // hover 는 누르지 않는다. mouse_down 이 함께 나가면 카드가 선택된다.
  assert.deepEqual(expand({ method: "hover", targetId: 7 }), [{ method: "move_mouse", params: [120, 210] }]);
  // 순서가 계약이다. 뒤집히면 목적지에서 눌러 원본으로 끄는 드래그가 된다.
  assert.deepEqual(
    expand({ method: "drag", from: { targetId: 7 }, to: { selector: "Canvas[0]/Slot[0]" } }),
    [
      { method: "move_mouse", params: [120, 210] },
      { method: "mouse_down", params: [0] },
      { method: "move_mouse", params: [310, 410] },
      { method: "mouse_up", params: [0] },
    ],
  );
});

test("expandActions turns a selector into the id enter_text and capture_screen send", () => {
  const store = storeOf(state([button]));
  const result = expandActions([
    performActionSchema.parse({ method: "enter_text", selector: "Canvas[0]/Button[0]", text: "hi" }),
    performActionSchema.parse({ method: "capture_screen", selector: "Canvas[0]/Button[0]", padding: 4 }),
  ], store);
  assert.ok(result.ok);
  if (result.ok) {
    assert.deepEqual(result.wire[0], { method: "enter_text", params: [7, "hi"] });
    assert.deepEqual(result.wire[1], { method: "capture_screen", params: [7, { padding: 4 }] });
  }
});

test("expandActions surrounds a click with its neighbours in order", () => {
  const expanded = expandActions([
    performActionSchema.parse({ method: "key_down", key: "W" }),
    performActionSchema.parse({ method: "click", x: 30, y: 40 }),
  ], new PulseStore());
  assert.ok(expanded.ok);
  if (expanded.ok) {
    assert.deepEqual(methodsOf(expanded.wire), ["key_down", "move_mouse", "mouse_down", "mouse_up"]);
  }
});

test("expandActions sends nothing when a target cannot be resolved", () => {
  for (const action of [
    { method: "click", targetId: 7 },
    { method: "drag", from: { x: 1, y: 2 }, to: { targetId: 7 } },
    { method: "enter_text", selector: "A[0]", text: "x" },
  ]) {
    assert.equal(expandActions([performActionSchema.parse(action)], new PulseStore()).ok, false);
  }
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

test("the hover tool only moves the pointer", async () => {
  const { sent } = await callTool("hover", { x: 12.5, y: 30 });
  assert.deepEqual(sent[0]?.map((a) => a.method), ["move_mouse"]);
});

test("the drag tool takes from and to, each a full target", async () => {
  const { sent } = await callTool("drag", { from: { x: 1, y: 2 }, to: { x: 30, y: 40 } });
  assert.deepEqual(sent[0]?.map((a) => [a.method, a.params]), [
    ["move_mouse", [1, 2]],
    ["mouse_down", [0]],
    ["move_mouse", [30, 40]],
    ["mouse_up", [0]],
  ]);
});

test("the enter_text tool refuses a screen position without touching Unity", async () => {
  const { sent, response } = await callTool("enter_text", { x: 1, y: 2, text: "a" });
  assert.equal(sent.length, 0);
  assert.equal(response.isError, true);
});

test("tools refuse a missing or doubled target without touching Unity", async () => {
  for (const [name, args] of [
    ["click", {}],
    ["hover", { targetId: 1, selector: "A[0]" }],
    ["drag", { from: { x: 1, y: 2 }, to: {} }],
  ] as const) {
    const { sent, response } = await callTool(name, args as Record<string, unknown>);
    assert.equal(sent.length, 0, name);
    assert.equal(response.isError, true, name);
  }
});

test("the removed pointer_ tools are gone", async () => {
  for (const name of ["pointer_click", "pointer_drag", "pointer_hover"]) {
    const { sent, response } = await callTool(name, { targetId: 1 });
    assert.equal(sent.length, 0, name);
    assert.equal(response.isError, true, name);
  }
});
