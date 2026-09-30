import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { UnityConnection } from "../src/connection.js";
import { PlayClient } from "../src/play-client.js";
import { registerPlayTools } from "../src/play-tools.js";
import { PulseStore } from "../src/pulse.js";
import { baseUnity, entity, FakeUnity, stampOf } from "./play-fake.js";

type ToolHandler = (args: Record<string, unknown>, extra: { signal: AbortSignal }) => Promise<{
  content: Array<{ type: string; text?: string }>;
  structuredContent?: Record<string, unknown>;
  isError?: boolean;
}>;

function register(unity: FakeUnity) {
  const server = new McpServer({ name: "t", version: "0" });
  const store = new PulseStore();
  const connection = unity as unknown as UnityConnection;
  registerPlayTools(server, connection, store, { client: new PlayClient(unity) });
  const tools = (server as unknown as { _registeredTools: Record<string, { handler: ToolHandler }> })._registeredTools;
  return {
    store,
    call: (name: string, args: Record<string, unknown> = {}) => (tools[name] as { handler: ToolHandler }).handler(args, { signal: new AbortController().signal }),
    names: Object.keys(tools),
  };
}

test("all six tools are registered", () => {
  const { names } = register(baseUnity());
  for (const name of ["get_play_capabilities", "observe", "inspect_action", "query_space", "act_and_observe", "watch_events"]) {
    assert.ok(names.includes(name), name);
  }
});

test("get_play_capabilities reports both sides, and an old Unity yields unsupported_capability", async () => {
  const ok = await register(baseUnity()).call("get_play_capabilities");
  assert.equal(ok.isError, undefined);
  const structured = ok.structuredContent as { server: { protocolVersion: number }; unity: { sessionId: string } };
  assert.equal(structured.server.protocolVersion, 1);
  assert.ok(structured.unity.sessionId);

  const old = await register(new FakeUnity()).call("get_play_capabilities");
  assert.equal(old.isError, true);
  assert.equal((old.structuredContent?.error as { code: string }).code, "unsupported_capability");
});

test("structured content and the text block are the same compact JSON, and the image is a separate block", async () => {
  const unity = baseUnity({
    play_observe: () => ({
      stamp: stampOf(), entities: [entity(1)], coherent: true,
      image: { mimeType: "image/jpeg", data: "AAAA", width: 1, height: 1, frame: 100 },
    }),
  });
  const result = await register(unity).call("observe", { include: ["entities", "image"] });
  assert.equal(result.content[0]?.type, "text");
  assert.equal(result.content[0]?.text, JSON.stringify(result.structuredContent));
  assert.equal(result.content[1]?.type, "image");
});

test("query_space refuses to guess: a screen point needs an explicit plane, and image points need capture metadata", async () => {
  const unity = baseUnity({ play_query_space: () => ({}) });
  const { call } = register(unity);
  const noPlane = await call("query_space", { query: { kind: "project_screen_to_world", point: { space: "screen", x: 1, y: 2 } } });
  assert.equal(noPlane.isError, true);
  assert.match(JSON.stringify(noPlane.structuredContent), /explicit plane/);
  const noCapture = await call("query_space", { query: { kind: "hit_test", point: { space: "image", x: 1, y: 2 } } });
  assert.match(JSON.stringify(noCapture.structuredContent), /capture/);
  assert.equal(unity.count("play_query_space"), 0);
});

test("image pixels are converted to screen pixels with the stated mapping before Unity is asked", async () => {
  const unity = baseUnity({ play_query_space: () => ({ hits: [] }) });
  const { call } = register(unity);
  await call("query_space", { query: { kind: "hit_test", point: {
    space: "image", x: 50, y: 20,
    capture: { observationId: "o", region: { x: 100, y: 200, width: 400, height: 300 }, scale: { x: 0.5, y: 0.5 } },
  } } });
  const sent = unity.calls.find((item) => item.method === "play_query_space")?.params[0] as { point: { space: string; x: number; y: number } };
  assert.deepEqual([sent.point.space, sent.point.x, sent.point.y], ["screen", 200, 240]);
});

test("line_test must state its dimension", async () => {
  const { call } = register(baseUnity({ play_query_space: () => ({}) }));
  const result = await call("query_space", { query: { kind: "line_test", from: { space: "world3d", x: 0, y: 0, z: 0 }, to: { space: "world3d", x: 1, y: 0, z: 0 } } });
  assert.equal(result.isError, true);
  assert.match(JSON.stringify(result.structuredContent), /dimension/);
});

test("outputs redact credential-like values but keep identifiers such as sessionId", async () => {
  const unity = baseUnity({
    play_query_space: () => ({ stamp: stampOf(), hits: [{ label: "Login", AccessToken: "shh", ref: { sessionId: "sess", id: 1, generation: 1 } }] }),
  });
  const { call } = register(unity);
  const result = await call("query_space", { query: { kind: "hit_test", point: { space: "screen", x: 1, y: 1 } } });
  const text = result.content[0]?.text ?? "";
  assert.doesNotMatch(text, /shh/);
  assert.match(text, /"sessionId":"sess"/);
});

test("inspect_action passes through provider data and keeps declared and observed effects separate", async () => {
  const unity = baseUnity({
    play_inspect: () => ({ availability: "unknown", expectedEffects: [{ text: "declared" }], observedEffects: null, recipe: [] }),
  });
  const result = await register(unity).call("inspect_action", { actionRef: "act:1" });
  const body = result.structuredContent as { expectedEffects: unknown[]; observedEffects: unknown; availability: string };
  assert.equal(body.availability, "unknown");
  assert.equal(body.observedEffects, null);
  assert.equal(body.expectedEffects.length, 1);
});

test("existing tools are unaffected when the new protocol is unsupported", async () => {
  // 새 도구가 실패해도 PulseStore 와 기존 도구 경로를 건드리지 않는다.
  const { store, call } = register(new FakeUnity());
  const result = await call("observe", {});
  assert.equal(result.isError, true);
  assert.equal(store.getState(), undefined);
});
