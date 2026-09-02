import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

const REGISTERED_TOOL_COUNT = 16;

interface ListedTool {
  name: string;
  inputSchema: unknown;
}

type ListToolsHandler = (
  request: { method: string; params: Record<string, never> },
  extra: { signal: AbortSignal },
) => Promise<{ tools: ListedTool[] }>;

/// agent 에게 실제로 나가는 schema 를 `tools/list` 응답 그대로 가져온다.
///
/// `server.server._requestHandlers` 는 SDK 내부 이름이라 판올림에 사라질 수 있다. 그때는 조용히
/// 통과하지 말고 여기서 실패해야 한다 — schema 를 한 개도 보지 못한 채 초록불이 켜지는 것이 이
/// test 가 막으려는 실수보다 나쁘다.
async function listRegisteredTools(): Promise<ListedTool[]> {
  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  const connection = {
    endpoint: "ws://127.0.0.1:17311/ws",
    isConnected: () => false,
  } as unknown as UnityConnection;
  registerTools(server, connection, new PulseStore());

  const handlers = (server.server as unknown as {
    _requestHandlers?: Map<string, ListToolsHandler>;
  })._requestHandlers;
  const handler = handlers?.get("tools/list");
  assert.ok(
    handler !== undefined,
    "server.server._requestHandlers no longer carries tools/list; this test cannot read any schema",
  );

  const listed = await handler(
    { method: "tools/list", params: {} },
    { signal: new AbortController().signal },
  );
  assert.ok(Array.isArray(listed.tools), "tools/list did not return a tools array");
  assert.equal(
    listed.tools.length,
    REGISTERED_TOOL_COUNT,
    "the registered tool count moved; update REGISTERED_TOOL_COUNT after checking the new tool's schema",
  );
  return listed.tools;
}

/// draft-07 에서만 뜻이 있는 구성을 찾는다.
///
/// Anthropic API 는 tool 의 input schema 가 draft 2020-12 이기를 요구하고, 어긋난 tool 이 하나만
/// 있어도 요청 전체를 400 으로 거절한다. 위치별 schema 는 `prefixItems`, 정의 모음은 `$defs` 여야
/// 하고, `dependencies` 는 `dependentSchemas` 와 `dependentRequired` 로 갈라졌으며,
/// `exclusiveMinimum` 과 `exclusiveMaximum` 은 boolean 이 아니라 숫자다.
function draftSevenLeftovers(node: unknown, path: string, found: string[] = []): string[] {
  if (Array.isArray(node)) {
    node.forEach((item, index) => draftSevenLeftovers(item, `${path}[${index}]`, found));
    return found;
  }
  if (node === null || typeof node !== "object") return found;

  for (const [key, value] of Object.entries(node)) {
    if (key === "items" && Array.isArray(value)) {
      found.push(`${path}.items is an array (needs prefixItems)`);
    }
    if (key === "definitions") found.push(`${path}.definitions (needs $defs)`);
    if (key === "dependencies") found.push(`${path}.dependencies`);
    if ((key === "exclusiveMinimum" || key === "exclusiveMaximum") && typeof value === "boolean") {
      found.push(`${path}.${key} is boolean`);
    }
    draftSevenLeftovers(value, `${path}.${key}`, found);
  }
  return found;
}

function collectRefs(node: unknown, path: string, found: string[] = []): string[] {
  if (Array.isArray(node)) {
    node.forEach((item, index) => collectRefs(item, `${path}[${index}]`, found));
    return found;
  }
  if (node === null || typeof node !== "object") return found;

  for (const [key, value] of Object.entries(node)) {
    if (key === "$ref") found.push(`${path}.$ref -> ${String(value)}`);
    collectRefs(value, `${path}.${key}`, found);
  }
  return found;
}

test("the checker recognizes the shape that made the API reject the tool list", () => {
  const draftSevenTuple = {
    type: "array",
    minItems: 1,
    maxItems: 1,
    items: [{ type: "integer" }],
  };
  assert.deepEqual(draftSevenLeftovers(draftSevenTuple, "$"), [
    "$.items is an array (needs prefixItems)",
  ]);
  assert.deepEqual(draftSevenLeftovers({ minimum: 0, exclusiveMinimum: true }, "$"), [
    "$.exclusiveMinimum is boolean",
  ]);
  assert.deepEqual(draftSevenLeftovers({ definitions: {}, dependencies: {} }, "$"), [
    "$.definitions (needs $defs)",
    "$.dependencies",
  ]);
});

test("every registered tool schema is free of draft-07-only constructs", async () => {
  const tools = await listRegisteredTools();
  const leftovers = tools.flatMap((tool) => draftSevenLeftovers(tool.inputSchema, tool.name));
  assert.deepEqual(leftovers, []);
});

/// `$ref` 자체는 draft 2020-12 에서도 옳지만, zod-to-json-schema 는 같은 zod schema 를 두 곳에서
/// 쓰면 두 번째를 처음 나온 자리를 가리키는 JSON pointer 로 적는다. 그 자리는 대개 다른 union
/// 가지 안이라, 가지 순서만 바뀌어도 제약이 조용히 다른 것을 가리킨다. schema 는 스스로 완결돼야
/// 한다.
test("no tool schema points at another branch with $ref", async () => {
  const tools = await listRegisteredTools();
  const refs = tools.flatMap((tool) => collectRefs(tool.inputSchema, tool.name));
  assert.deepEqual(refs, []);
});

test("perform_actions still takes a non-empty action array", async () => {
  const tools = await listRegisteredTools();
  const performActions = tools.find((tool) => tool.name === "perform_actions");
  assert.ok(performActions !== undefined);

  const schema = performActions.inputSchema as {
    properties: { actions: { type: string; minItems: number; items: { anyOf: unknown[] } } };
    required: string[];
  };
  assert.equal(schema.properties.actions.type, "array");
  assert.equal(schema.properties.actions.minItems, 1);
  assert.deepEqual(schema.required, ["actions"]);
  assert.equal(schema.properties.actions.items.anyOf.length, 16);
});
