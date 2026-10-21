import assert from "node:assert/strict";
import test from "node:test";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { compareVersions, probeCompatibility } from "../src/compatibility.js";
import type { ActionRequest, UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

test("SDK and CLI numbers may differ when the declared CLI matches", () => {
  assert.equal(compareVersions("1.2.0", {
    runtimeVersion: "2.0.0", requiredServerVersion: "1.2.0",
  }).status, "matched");
});

test("older, newer, and prerelease CLI pins must match exactly", () => {
  for (const cli of ["1.0.0", "3.0.0", "2.0.0-rc.1"]) {
    const result = compareVersions(cli, { runtimeVersion: "2.0.0", requiredServerVersion: "2.0.0" });
    assert.equal(result.status, "mismatch");
    assert.equal(result.requiredCliVersion, "2.0.0");
    assert.match(result.message, /Update CLI/);
    assert.match(result.message, /Restart/);
  }
});

test("missing, malformed, and unsupported SDK metadata are unknown", () => {
  for (const raw of [undefined, null, {}, { runtimeVersion: "1.0.0" },
    { runtimeVersion: "1.0.0", requiredServerVersion: "latest" },
    { runtimeVersion: 1, requiredServerVersion: "1.0.0" }]) {
    assert.equal(compareVersions("1.0.0", raw).status, "unknown");
  }
});

test("version probe is bounded and preserves metadata even with a different protocol", async () => {
  const result = await probeCompatibility({
    isConnected: () => true,
    async sendActions(actions, options) {
      assert.equal(options?.timeoutMilliseconds, 1_500);
      assert.deepEqual(actions, [{ id: 42, method: "play_capabilities", params: [] }]);
      return [{ id: 42, success: true, returnValue: {
        protocolVersion: 999, runtimeVersion: "2.0.0", requiredServerVersion: "2.1.0",
      } }];
    },
  }, "2.0.0", 42);
  assert.equal(result.status, "mismatch");
});

test("old SDK failure, query timeout, and a dropped connection remain unknown", async () => {
  for (const mode of ["unsupported", "timeout", "disconnected"]) {
    const result = await probeCompatibility({
      isConnected: () => mode !== "disconnected",
      async sendActions() {
        if (mode === "timeout") throw new Error("timed out");
        return [{ id: 1, success: mode !== "unsupported", returnValue: {
          runtimeVersion: "1.0.0", requiredServerVersion: "1.0.0",
        } }];
      },
    }, "1.0.0", 1);
    assert.equal(result.status, "unknown");
  }
});

type ToolHandler = (request: {
  method: string; params: { name: string; arguments: object };
}, extra: { signal: AbortSignal }) => Promise<{ isError?: boolean; content: { type: string; text?: string }[] }>;

function statusTool(cliVersion: string, state: { connected: boolean; required: string; fail?: boolean; legacy?: boolean }) {
  const connection = {
    endpoint: "ws://localhost/test",
    async ensureConnected() { state.connected = true; },
    isConnected: () => state.connected,
    async sendActions(actions: ActionRequest[]) {
      if (actions[0].method === "runtime_activity") return actions.map(({ id }) => state.legacy
        ? { id, success: false, error: "Unsupported method" }
        : { id, success: true, returnValue: { reading: false, resumed: false, run: null } });
      if (state.fail) { state.connected = false; throw new Error("disconnected"); }
      return actions.map(({ id }) => ({ id, success: true, returnValue: {
        runtimeVersion: "2.0.0", requiredServerVersion: state.required,
      } }));
    },
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "version-test", version: cliVersion });
  registerTools(server, connection, new PulseStore(), cliVersion);
  const handler = (server.server as unknown as { _requestHandlers: Map<string, ToolHandler> })
    ._requestHandlers.get("tools/call")!;
  return () => handler({ method: "tools/call", params: { name: "get_unity_status", arguments: {} } },
    { signal: new AbortController().signal });
}

test("status warns until the running CLI changes and probes the new SDK again", async () => {
  const state = { connected: true, required: "2.0.0" };
  const oldProcess = statusTool("1.0.0", state);
  for (let attempt = 0; attempt < 2; attempt++) {
    const response = await oldProcess();
    assert.notEqual(response.isError, true);
    assert.match(response.content[0].text!, /Compatibility \(mismatch\)/);
  }
  const restarted = statusTool("2.0.0", state);
  assert.match((await restarted()).content[0].text!, /Compatibility \(matched\)/);
  state.required = "3.0.0";
  assert.match((await restarted()).content[0].text!, /requires CLI 3.0.0/);
});

test("status rechecks connection after a failed probe without becoming a tool error", async () => {
  const response = await statusTool("1.0.0", { connected: true, required: "1.0.0", fail: true })();
  assert.notEqual(response.isError, true);
  assert.match(response.content[0].text!, /Unity is not running/);
  assert.match(response.content[0].text!, /Compatibility \(unknown\)/);
});

test("an older SDK still reports compatibility when idle activity is unsupported", async () => {
  const response = await statusTool("1.0.0", { connected: true, required: "2.0.0", legacy: true })();
  assert.notEqual(response.isError, true);
  assert.match(response.content[0].text!, /Compatibility \(mismatch\)/);
  assert.match(response.content[0].text!, /Runtime activity unavailable/);
});
