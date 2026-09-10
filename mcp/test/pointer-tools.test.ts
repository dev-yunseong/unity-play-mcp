import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { ActionRequest, ActionResult, UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

/// `pointer_click` 과 `pointer_drag` tool 이 실제로 Unity 로 내보내는 `{ method, params }`.
///
/// `perform-actions.test.ts` 는 `perform_actions` 안에 실린 action 을 본다. 같은 두 method 를
/// tool 로 직접 부를 때도 같은 배열이 나가야 하고, 두 경로가 갈라지면 여기서 걸린다.
type CallToolHandler = (
  request: { method: string; params: { name: string; arguments: Record<string, unknown> } },
  extra: { signal: AbortSignal },
) => Promise<unknown>;

/// tool 하나를 `tools/call` 로 부르고, 그 호출이 connection 에 건넨 action 들을 돌려준다.
///
/// agent 가 부르는 그 경로를 그대로 탄다. `server._requestHandlers` 는 SDK 내부 이름이라
/// 판올림에 사라질 수 있고, 그때는 조용히 통과하지 말고 여기서 실패해야 한다 — 아무것도 부르지
/// 못한 채 초록불이 켜지는 것이 이 test 가 막으려는 실수보다 나쁘다. `schema.test.ts` 가
/// `tools/list` 에 대고 하는 것과 같은 이유다.
async function sentBy(
  toolName: string,
  args: Record<string, unknown>,
): Promise<ActionRequest[]> {
  let sent: ActionRequest[] = [];
  const connection = {
    endpoint: "ws://127.0.0.1:17311/ws",
    isConnected: () => true,
    async ensureConnected(): Promise<void> {},
    async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
      sent = actions;
      return actions.map((action) => ({ id: action.id, success: true }));
    },
  } as unknown as UnityConnection;

  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  registerTools(server, connection, new PulseStore());

  const handler = (server.server as unknown as {
    _requestHandlers?: Map<string, CallToolHandler>;
  })._requestHandlers?.get("tools/call");
  assert.ok(
    handler !== undefined,
    "server.server._requestHandlers no longer carries tools/call; this test cannot call any tool",
  );

  await handler(
    { method: "tools/call", params: { name: toolName, arguments: args } },
    { signal: new AbortController().signal },
  );
  return sent;
}

test("pointer_click sends the target id as the only positional argument", async () => {
  const sent = await sentBy("pointer_click", { targetId: -3518 });

  assert.equal(sent.length, 1);
  assert.equal(sent[0].method, "pointer_click");
  assert.deepEqual(sent[0].params, [-3518]);
});

test("pointer_drag sends the source before the target", async () => {
  const sent = await sentBy("pointer_drag", { sourceId: -4102, targetId: -2277 });

  assert.equal(sent.length, 1);
  assert.equal(sent[0].method, "pointer_drag");
  // 순서가 계약이다. 뒤집히면 Unity 는 목적지에서 눌러 원본으로 끄는 드래그를 한다.
  assert.deepEqual(sent[0].params, [-4102, -2277]);
});

test("click still goes to button_click, untouched by the new pointer tools", async () => {
  const sent = await sentBy("click", { targetId: 42 });

  assert.equal(sent[0].method, "button_click");
  assert.deepEqual(sent[0].params, [42]);
});
