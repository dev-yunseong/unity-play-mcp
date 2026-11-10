import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { ActionRequest, UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

import { baseUnity, capabilitiesPayload } from "./play-fake.js";

const REGISTERED_TOOL_COUNT = 28;

test("set_overlay validates tool arguments and sends the same boolean as batches", async () => {
  const sent: ActionRequest[] = [];
  const connection = {
    async sendActions(actions: ActionRequest[]) {
      sent.push(...actions);
      return actions.map((action) => ({ id: action.id, success: true, returnValue: action.params[0] }));
    },
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "overlay-test", version: "0" });
  registerTools(server, connection, new PulseStore());
  type Handler = (request: {
    method: string; params: { name: string; arguments: unknown };
  }, extra: { signal: AbortSignal }) => Promise<{ isError?: boolean }>;
  const handlers = (server.server as unknown as {
    _requestHandlers: Map<string, Handler>;
  })._requestHandlers;
  const registered = (server as unknown as { _registeredTools: object })._registeredTools;
  assert.equal(Object.keys(registered).length, REGISTERED_TOOL_COUNT);
  const call = handlers.get("tools/call")!;
  for (const visible of [true, false]) {
    const response = await call({
      method: "tools/call", params: { name: "set_overlay", arguments: { visible } },
    }, { signal: new AbortController().signal });
    assert.notEqual(response.isError, true);
  }
  assert.deepEqual(sent.map(({ method, params }) => ({ method, params })), [
    { method: "set_overlay", params: [true] }, { method: "set_overlay", params: [false] },
  ]);
  const rejected = await call({
    method: "tools/call", params: { name: "set_overlay", arguments: { visible: "false" } },
  }, { signal: new AbortController().signal });
  assert.equal(rejected.isError, true);
  assert.equal(sent.length, 2);
});

test("project actions are discoverable and run through perform_actions", async () => {
  const action = { name: "test_echo", description: "Return the input value.", parameters: "[value: string]" };
  const unity = baseUnity({
    play_capabilities: () => capabilitiesPayload({ registeredActions: [action] }),
    unity_action: ([, value]) => value,
  });
  const server = new McpServer({ name: "project-action-test", version: "0" });
  registerTools(server, unity as unknown as UnityConnection, new PulseStore());

  type Handler = (request: {
    method: string; params: { name: string; arguments: unknown };
  }, extra: { signal: AbortSignal }) => Promise<{
    isError?: boolean; content: Array<{ text?: string }>;
    structuredContent?: { unity: { registeredActions?: unknown[] } };
  }>;
  const call = (server.server as unknown as {
    _requestHandlers: Map<string, Handler>;
  })._requestHandlers.get("tools/call")!;
  const extra = { signal: new AbortController().signal };
  const capabilities = await call({
    method: "tools/call", params: { name: "get_play_capabilities", arguments: {} },
  }, extra);
  assert.deepEqual(capabilities.structuredContent?.unity.registeredActions, [action]);

  const request = {
    method: "tools/call",
    params: { name: "perform_actions", arguments: {
      actions: [{ method: "unity_action", name: action.name, params: ["hello"] }],
    } },
  };
  const response = await call(request, extra);
  assert.notEqual(response.isError, true);
  assert.match(response.content[0]?.text ?? "", /hello/);
  assert.deepEqual(unity.calls.at(-1)?.params, ["test_echo", "hello"]);

  unity.handlers.delete("unity_action");
  const oldResponse = await call(request, extra);
  assert.match(oldResponse.content[0]?.text ?? "", /Unsupported method: unity_action/);
});
