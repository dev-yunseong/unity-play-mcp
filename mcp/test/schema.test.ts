import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { ActionRequest, UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

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
