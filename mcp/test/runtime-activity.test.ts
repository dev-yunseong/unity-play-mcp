import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { UnityConnection, type ActionRequest } from "../src/connection.js";
import { PulseStore, type PulseFrame } from "../src/pulse.js";
import { keepReadingsActive, requestRuntimeActivity } from "../src/runtime-activity.js";
import { registerTools } from "../src/tools.js";

const frame = (run: string): PulseFrame => ({
  type: "PULSE", id: 1, schema: 2, run, reading: 1, frame: 10, scene: "Lobby",
  statics: [], active: [], deactive: [], whole: true, watching: 0, unresolved: 0, unwatchable: 0, changed: [],
});
function fixture(store: PulseStore, run: string | null, beforeResponse?: () => void) {
  const sent: ActionRequest[] = [];
  const connection = {
    isConnected: () => true,
    onDisconnect: () => () => undefined,
    async sendActions(actions: ActionRequest[]) {
      sent.push(...actions); beforeResponse?.();
      return actions.map(({ id }) => ({ id, success: true,
        returnValue: { reading: run !== null, resumed: true, run } }));
    },
  };
  return { connection, sent };
}

test("fresh run may arrive before or after the activity response", async () => {
  for (const early of [true, false]) {
    const store = new PulseStore(); store.fold(frame("old"));
    const { connection } = fixture(store, "new", () => {
      if (early) store.fold(frame("new"));
      else setTimeout(() => store.fold(frame("new")), 1);
    });
    await requestRuntimeActivity(connection, store, 1, "readings");
    assert.equal(store.getState()?.run, "new");
    assert.equal(store.getStaleness(), undefined);
  }
});

test("old-run whole and missing fresh pulse cannot satisfy resume", async () => {
  const store = new PulseStore(); store.fold(frame("old"));
  const { connection } = fixture(store, "new", () => store.fold({ ...frame("old"), reading: 2 }));
  await assert.rejects(requestRuntimeActivity(connection, store, 1, "readings", undefined, 10), /fresh scene reading/);
  assert.ok(store.getStaleness());
});

test("status preserves active readings and only marks an actually stopped run stale", async () => {
  const store = new PulseStore(); store.fold(frame("current"));
  await requestRuntimeActivity(fixture(store, "current").connection, store, 1, "status");
  assert.equal(store.getStaleness(), undefined);
  await requestRuntimeActivity(fixture(store, null).connection, store, 2, "status");
  assert.equal(store.getStaleness()?.reason, "stopped");
});

test("old SDK response gives an explicit upgrade error", async () => {
  const store = new PulseStore();
  const { connection } = fixture(store, null);
  connection.sendActions = async () => [];
  await assert.rejects(requestRuntimeActivity(connection, store, 1, "status"), /Update the Unity Play MCP SDK/);
});

test("active-wait refreshes do not overlap and stop on completion or cancellation", async (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  for (const cancel of [false, true]) {
    let count = 0;
    let release!: () => void;
    const controller = new AbortController();
    const stop = keepReadingsActive(async () => { count++; await new Promise<void>((r) => { release = r; }); }, controller.signal);
    t.mock.timers.tick(5_000);
    assert.equal(count, 1);
    t.mock.timers.tick(20_000);
    assert.equal(count, 1);
    if (cancel) controller.abort(); else stop();
    release(); await Promise.resolve(); await Promise.resolve();
    t.mock.timers.tick(20_000);
    assert.equal(count, 1);
  }
});

test("stalled handshake is bounded and never sends activity after timeout or abort", async () => {
  for (const abort of [false, true]) {
    const store = new PulseStore();
    class Socket extends EventEmitter {
      readyState = 0;
      sent: string[] = [];
      send(value: string) { this.sent.push(value); }
      close() { this.readyState = 3; this.emit("close"); }
    }
    const socket = new Socket();
    const connection = new UnityConnection({ pulseStore: store, createWebSocket: () => socket });
    const controller = new AbortController();
    const pending = requestRuntimeActivity(connection, store, 1, "readings", controller.signal, 10);
    if (abort) controller.abort();
    await assert.rejects(pending, /cancelled/);
    socket.readyState = 1; socket.emit("open");
    await Promise.resolve(); await Promise.resolve();
    assert.deepEqual(socket.sent, []);
    connection.close();
  }
});

test("cached tools wake readings, status preserves them, and raw input sends no collection request", async () => {
  const store = new PulseStore();
  const sent: ActionRequest[] = [];
  let reading = false;
  const connection = {
    endpoint: "ws://test", isConnected: () => true, onDisconnect: () => () => undefined,
    async sendActions(actions: ActionRequest[]) {
      sent.push(...actions);
      return actions.map((action) => {
        if (action.method !== "runtime_activity") return { id: action.id, success: true };
        const resumed = !reading && action.params[0] === "readings";
        if (resumed) { reading = true; store.fold(frame("live")); }
        return { id: action.id, success: true, returnValue: { reading, resumed, run: reading ? "live" : null } };
      });
    },
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "idle-test", version: "0" });
  registerTools(server, connection, store);
  type Handler = (request: { method: string; params: { name: string; arguments: object } },
    extra: { signal: AbortSignal }) => Promise<{ isError?: boolean; content: { text?: string }[] }>;
  const handler = (server.server as unknown as { _requestHandlers: Map<string, Handler> })._requestHandlers.get("tools/call")!;
  const call = (name: string, args = {}) => handler({ method: "tools/call", params: { name, arguments: args } },
    { signal: new AbortController().signal });
  await call("get_unity_status");
  assert.equal(reading, false);
  for (const name of ["get_scene_state", "get_visible_elements", "search_targets"]) {
    const result = await call(name);
    assert.notEqual(result.isError, true);
    assert.deepEqual(sent.at(-1)?.params, ["readings"]);
  }
  await call("get_unity_status");
  assert.equal(store.getStaleness(), undefined);
  const before = sent.length;
  await call("move_mouse", { x: 1, y: 2 });
  assert.deepEqual(sent.slice(before).map((action) => action.method), ["move_mouse"]);
  const target = await call("click", { selector: "Missing[0]" });
  assert.equal(target.isError, true);
  assert.equal(sent.at(-1)?.method, "runtime_activity");
  assert.match(target.content[0].text ?? "", /Missing/);
});
