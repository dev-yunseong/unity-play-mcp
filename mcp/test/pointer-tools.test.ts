import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { ActionRequest, ActionResult, UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

/// `pointer_click` 과 `pointer_drag` tool 이 Unity 로 보내는 `{ method, params }`.
///
/// tool 로 직접 부를 때도 `perform_actions` 경로(`perform-actions.test.ts`)와 같은 배열이 나가야 한다.
type CallToolHandler = (
  request: { method: string; params: { name: string; arguments: Record<string, unknown> } },
  extra: { signal: AbortSignal },
) => Promise<unknown>;

/// tool 하나를 `tools/call` 로 부르고 그 호출이 connection 에 넘긴 action 들을 돌려준다.
///
/// `server._requestHandlers` 는 SDK 내부 이름이라 버전이 오르면 사라질 수 있다. 그때 조용히 통과하지
/// 않고 여기서 실패해야 한다.
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
  // 순서가 계약이다. 뒤집히면 Unity 는 목적지에서 원본으로 드래그한다.
  assert.deepEqual(sent[0].params, [-4102, -2277]);
});

test("pointer_hover sends the target id as the only positional argument", async () => {
  const sent = await sentBy("pointer_hover", { targetId: -3518 });

  // hover 는 누르지 않으므로 action 은 하나다. mouse_down 이 함께 나가면 카드가 선택된다.
  assert.equal(sent.length, 1);
  assert.equal(sent[0].method, "pointer_hover");
  assert.deepEqual(sent[0].params, [-3518]);
});

test("click still goes to button_click, untouched by the new pointer tools", async () => {
  const sent = await sentBy("click", { targetId: 42 });

  assert.equal(sent[0].method, "button_click");
  assert.deepEqual(sent[0].params, [42]);
});
