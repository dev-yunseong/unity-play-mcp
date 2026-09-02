import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { UnityUnreachableError } from "./connection.js";
import type { ActionRequest, ActionResult, UnityConnection } from "./connection.js";
import { objectKey, type PulseObject, type PulseStore } from "./pulse.js";
import { foldIntoTree, UNLIMITED_DEPTH, type TreeNode } from "./tree.js";

type ToolContent =
  | { type: "text"; text: string }
  | { type: "image"; data: string; mimeType: string };

export interface ToolResponse {
  [key: string]: unknown;
  content: ToolContent[];
  isError?: boolean;
}

let nextActionId = 1;

/// 값 제약을 부르는 곳마다 새 schema 로 만든다.
///
/// 하나를 만들어 여러 곳에서 나눠 쓰면 zod-to-json-schema 가 두 번째부터 `$ref` 로 적는다. 그
/// `$ref` 는 처음 나온 자리를 가리키는 JSON pointer 라서, 다른 union 가지를 가리키게 되고 가지
/// 순서가 바뀌면 조용히 다른 곳을 가리킨다. 매번 새로 만들면 schema 가 스스로 완결된다.
const targetIdSchema = () => z.number().int();
const mouseButtonSchema = () => z.number().int().min(0).max(2);
const keySchema = () => z.string().min(1);
const inputNameSchema = () => z.string().min(1);
const maxEdgeSchema = () => z.number().int().positive();
const paddingSchema = () => z.number().int().nonnegative();

/// `perform_actions` 가 받는 action 하나.
///
/// Unity 로 나가는 wire 형식은 `{ method, params: [...] }` 의 위치 인자지만, 입력은 이름 있는
/// field 로 받는다. `z.tuple` 은 draft-07 의 배열형 `items` 로 변환되고, draft 2020-12 는 위치별
/// schema 를 `prefixItems` 로만 받으므로 Anthropic API 가 tool 목록 전체를 400 으로 거절한다.
/// `params` 배열은 `toWireAction` 이 만든다.
export const performActionSchema = z.discriminatedUnion("method", [
  z.object({ method: z.literal("button_click"), targetId: targetIdSchema() }).strict(),
  z.object({ method: z.literal("enter_text"), targetId: targetIdSchema(), text: z.string() }).strict(),
  z.object({ method: z.literal("move_mouse"), x: z.number(), y: z.number() }).strict(),
  z.object({ method: z.literal("mouse_down"), button: mouseButtonSchema() }).strict(),
  z.object({ method: z.literal("mouse_up"), button: mouseButtonSchema() }).strict(),
  z.object({ method: z.literal("key_click"), key: keySchema(), seconds: z.number().positive() }).strict(),
  z.object({ method: z.literal("key_down"), key: keySchema() }).strict(),
  z.object({ method: z.literal("key_up"), key: keySchema() }).strict(),
  z.object({ method: z.literal("set_axis"), name: inputNameSchema(), value: z.number() }).strict(),
  z.object({ method: z.literal("set_button"), name: inputNameSchema(), pressed: z.boolean() }).strict(),
  z.object({ method: z.literal("pause_time") }).strict(),
  z.object({ method: z.literal("resume_time") }).strict(),
  z.object({ method: z.literal("reset_game"), clearPlayerPrefs: z.boolean() }).strict(),
  z.object({ method: z.literal("start_readings") }).strict(),
  z.object({ method: z.literal("stop_readings") }).strict(),
  z.object({
    method: z.literal("capture_screen"),
    targetId: targetIdSchema().optional(),
    maxEdge: maxEdgeSchema().optional(),
    padding: paddingSchema().optional(),
  }).strict(),
]).superRefine((action, ctx) => {
  // `maxEdge` 와 `padding` 은 잘라낼 대상을 두고 하는 말이라 `targetId` 없이는 뜻이 없다.
  // capture_screen tool 이 같은 조합을 거절하는 것과 같은 규칙이다.
  if (action.method !== "capture_screen") return;
  if (action.targetId !== undefined) return;
  if (action.maxEdge === undefined && action.padding === undefined) return;
  ctx.addIssue({
    code: z.ZodIssueCode.custom,
    path: ["targetId"],
    message: "capture_screen requires targetId when maxEdge or padding is set.",
  });
});

export type PerformAction = z.infer<typeof performActionSchema>;

interface CaptureScreenArguments {
  targetId?: number;
  maxEdge?: number;
  padding?: number;
}

/// `capture_screen` 이 Unity 로 보내는 `params` 배열. 대상이 없으면 빈 배열, 옵션이 없으면
/// `[targetId]`, 있으면 `[targetId, options]` 세 경우뿐이다.
function captureScreenParams(capture: CaptureScreenArguments): unknown[] {
  if (capture.targetId === undefined) return [];
  const options = {
    ...(capture.maxEdge === undefined ? {} : { maxEdge: capture.maxEdge }),
    ...(capture.padding === undefined ? {} : { padding: capture.padding }),
  };
  return Object.keys(options).length === 0 ? [capture.targetId] : [capture.targetId, options];
}

/// 이름 있는 field 를 Unity 가 받는 위치 인자 배열로 되돌린다.
///
/// 배열의 순서와 값은 Unity 쪽 protocol 이라 바꿀 수 없다. 이 함수가 그 계약이 적힌 유일한 자리다.
export function toWireAction(action: PerformAction): { method: string; params: unknown[] } {
  switch (action.method) {
    case "button_click":
      return { method: action.method, params: [action.targetId] };
    case "enter_text":
      return { method: action.method, params: [action.targetId, action.text] };
    case "move_mouse":
      return { method: action.method, params: [action.x, action.y] };
    case "mouse_down":
    case "mouse_up":
      return { method: action.method, params: [action.button] };
    case "key_click":
      return { method: action.method, params: [action.key, action.seconds] };
    case "key_down":
    case "key_up":
      return { method: action.method, params: [action.key] };
    case "set_axis":
      return { method: action.method, params: [action.name, action.value] };
    case "set_button":
      return { method: action.method, params: [action.name, action.pressed] };
    case "reset_game":
      return { method: action.method, params: [{ clearPlayerPrefs: action.clearPlayerPrefs }] };
    case "capture_screen":
      return { method: action.method, params: captureScreenParams(action) };
    case "pause_time":
    case "resume_time":
    case "start_readings":
    case "stop_readings":
      return { method: action.method, params: [] };
  }
}

function text(textValue: string): ToolResponse {
  return { content: [{ type: "text", text: textValue }] };
}

function describeResults(results: ActionResult[]): string {
  return JSON.stringify(results, null, 2);
}

export async function dispatchActions(
  connection: Pick<UnityConnection, "sendActions">,
  actions: ReadonlyArray<{ method: string; params: unknown[] }>,
): Promise<ToolResponse> {
  const requests: ActionRequest[] = actions.map((action) => ({
    id: nextActionId++,
    method: action.method,
    params: action.params,
  }));

  try {
    const results = await connection.sendActions(requests);
    const failed = results.filter((result) => !result.success);
    return {
      content: [{
        type: "text",
        text: failed.length === 0
          ? `Action batch completed.\n${describeResults(results)}`
          : `${failed.length} of ${results.length} actions failed.\n${describeResults(results)}`,
      }],
      ...(failed.length > 0 ? { isError: true } : {}),
    };
  } catch (error) {
    return {
      content: [{ type: "text", text: failureText("Unity action failed", error) }],
      isError: true,
    };
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/// 실패를 agent 가 읽을 문장으로 바꾼다.
///
/// Unity 에 닿지 못한 것은 요청의 문제가 아니라 게임이 안 돌고 있다는 뜻이라, 무엇을 하려다 실패했는지
/// 는 도움이 되지 않는다. 그래서 그 경우에만 접두사를 떼고 사용자가 할 일만 남긴다. 소켓 오류를
/// 그대로 흘리면 agent 가 자기 인자를 의심하거나 재시도한다.
export function failureText(attempted: string, error: unknown): string {
  if (error instanceof UnityUnreachableError) {
    return error.message;
  }
  return `${attempted}: ${errorMessage(error)}`;
}

export interface UnityStatus {
  connected: boolean;
  endpoint: string;
  reading?: number;
  frame?: number;
  scene?: string;
  lastReadingAt?: number;
  now: number;
}

/// status tool 이 돌려줄 문장.
///
/// 순수 함수인 이유는 이 문장이 계약이기 때문이다. 연결됨과 안 됨, pulse 가 시작됨과 안 됨의 네 경우가
/// 서로 다른 말을 해야 하고, 그것을 실제 소켓 없이 확인할 수 있어야 한다.
export function describeStatus(status: UnityStatus): string {
  if (!status.connected) {
    return [
      `Unity is not running. Nothing is listening at ${status.endpoint}.`,
      "The Unity editor must be open with the project in Play Mode.",
      "Ask the user to enter Play Mode before calling any other tool.",
    ].join(" ");
  }

  const head = `Unity is running and connected at ${status.endpoint}.`;

  if (status.lastReadingAt === undefined) {
    return `${head} No scene reading has arrived yet. Call start_readings before get_scene_state.`;
  }

  const secondsAgo = Math.max(0, Math.round((status.now - status.lastReadingAt) / 1000));
  const age = secondsAgo === 0 ? "just now" : `${secondsAgo}s ago`;

  return [
    head,
    `Readings are running: reading ${status.reading} on frame ${status.frame} arrived ${age}.`,
    `Scene: ${status.scene}.`,
  ].join(" ");
}

async function dispatchOne(
  connection: Pick<UnityConnection, "sendActions">,
  method: string,
  params: unknown[],
): Promise<ToolResponse> {
  return dispatchActions(connection, [{ method, params }]);
}

/// 이력 map 의 내부 키 — `component.on \0 member` 또는 `component.on \0 member \0 among` —
/// 를 읽는 쪽이 보게 될 이름으로 바꾼다.
function historyLabel(path: string): string {
  const [on, member, among] = path.split("\u0000");
  const named = `${on ?? ""}.${member ?? ""}`;
  return among === undefined ? named : `${named}#${among}`;
}

function historyOf(
  store: PulseStore,
  objects: readonly unknown[],
  scene: string,
): Record<string, Record<string, unknown>> {
  const collected: Record<string, Record<string, unknown>> = {};
  for (const item of objects) {
    if (typeof item !== "object" || item === null) continue;
    const key = objectKey(item as PulseObject, scene);
    const series = store.getObjectHistory(key);
    if (series.size === 0) continue;
    const named: Record<string, unknown> = {};
    for (const [path, readings] of series) {
      named[historyLabel(path)] = readings;
    }
    collected[key] = named;
  }
  return collected;
}

/// tree 가 실제로 펼친 마디에 앉은 객체들. 접힌 마디 아래는 세지 않는다 — 응답에 나오지도
/// 않는 객체의 이력을 실을 이유가 없다.
function objectsShownIn(nodes: readonly TreeNode[]): PulseObject[] {
  const found: PulseObject[] = [];
  for (const node of nodes) {
    if (node.object !== undefined) {
      found.push(node.object);
    }
    if (node.children !== undefined) {
      found.push(...objectsShownIn(node.children));
    }
  }
  return found;
}

/// 한 객체의 멤버들이 마지막으로 움직인 `reading`.
function latestReadingOf(store: PulseStore, scene: string) {
  return (object: PulseObject): number | undefined => {
    let latest: number | undefined;
    for (const series of store.getObjectHistory(objectKey(object, scene)).values()) {
      const last = series[series.length - 1];
      if (last !== undefined && (latest === undefined || last.reading > latest)) {
        latest = last.reading;
      }
    }
    return latest;
  };
}

function stateResponse(
  store: PulseStore,
  selector?: string,
  includeInactive = false,
  includeHistory = false,
  root?: string,
  depth?: number,
): ToolResponse {
  const state = store.getState();
  if (state === undefined || state === null) {
    return text("No scene reading has arrived. Call start_readings to begin a play session, then try again.");
  }

  const record = state as unknown as Record<string, unknown>;
  const matches = (candidate: string): boolean =>
    selector === undefined || candidate.includes(selector);
  const filterObjects = (value: unknown): unknown[] => {
    if (!Array.isArray(value)) return [];
    if (selector === undefined) return value;
    return value.filter((item) => {
      if (typeof item !== "object" || item === null) return false;
      return matches(String((item as Record<string, unknown>).selector ?? ""));
    });
  };
  // 파괴된 것은 `{ object, goneAtReading }` 이라 객체 자체와 모양이 다르다. selector 는 그
  // 안쪽 객체에 걸어야 한다.
  const filterGone = (value: unknown): unknown[] => {
    if (!Array.isArray(value)) return [];
    if (selector === undefined) return value;
    return value.filter((item) => {
      if (typeof item !== "object" || item === null) return false;
      const inner = (item as Record<string, unknown>).object;
      if (typeof inner !== "object" || inner === null) return false;
      return matches(String((inner as Record<string, unknown>).selector ?? ""));
    });
  };
  const active = filterObjects(record.active);
  const deactive = filterObjects(record.deactive);
  const scene = String(record.scene ?? "");

  // `root` 도 `depth` 도 없으면 지금까지와 똑같은 평평한 응답이다. 기존 호출이 갑자기 다른
  // 모양을 받지 않게 한다.
  if (root !== undefined || depth !== undefined) {
    const considered = includeInactive ? [...active, ...deactive] : active;
    const tree = foldIntoTree(
      considered as PulseObject[],
      latestReadingOf(store, scene),
      root,
      depth ?? UNLIMITED_DEPTH,
    );
    // `statics` 는 어느 객체에도 매달리지 않으므로 tree 로는 표현되지 않는다. 빼면 이 모드에서만
    // 사라지고, 양이 적어 뺄 이유도 없다. `changed` 는 반대다 — 분주한 씬에서 길고, 마디마다
    // 붙는 `lastChangedReading` 이 같은 물음에 tree 모양으로 답한다.
    return text(JSON.stringify({
      reading: record.reading,
      frame: record.frame,
      scene: record.scene,
      ...(root === undefined ? {} : { root }),
      statics: record.statics ?? [],
      gone: filterGone(record.gone),
      tree,
      ...(includeHistory
        ? { history: historyOf(store, objectsShownIn(tree), scene) }
        : {}),
    }, null, 2));
  }

  const response = {
    reading: record.reading,
    frame: record.frame,
    scene: record.scene,
    changed: record.changed ?? [],
    statics: record.statics ?? [],
    active,
    ...(includeInactive ? { deactive } : {}),
    gone: filterGone(record.gone),
    ...(includeHistory
      ? {
          history: historyOf(
            store,
            includeInactive ? [...active, ...deactive] : active,
            scene,
          ),
        }
      : {}),
  };
  return text(JSON.stringify(response, null, 2));
}

interface CapturePayload {
  mimeType: string;
  width: number;
  height: number;
  targetId?: number;
  clipped: boolean;
  data: string;
}

const capturePayloadSchema = z.object({
  mimeType: z.enum(["image/png", "image/jpeg"]),
  width: z.number().int().positive(),
  height: z.number().int().positive(),
  targetId: z.number().int().optional(),
  clipped: z.boolean(),
  data: z.string().min(1).regex(/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/),
}).strict();

function findCapture(results: ActionResult[]): CapturePayload | undefined {
  const successful = results.find((result) => result.success);
  if (successful === undefined) return undefined;
  const parsed = capturePayloadSchema.safeParse(successful.returnValue);
  return parsed.success ? parsed.data : undefined;
}

export function registerTools(server: McpServer, connection: UnityConnection, store: PulseStore): void {
  server.registerTool("get_unity_status", {
    description: "Check whether the Unity game is running and reachable, and whether scene readings have started. Call this first, and whenever another tool reports that Unity is not running.",
    inputSchema: {},
  }, async () => {
    // 닿지 못하는 것은 이 tool 의 실패가 아니라 이 tool 이 물어본 것에 대한 답이다. isError 를 붙이면
    // agent 가 상태를 물어본 것마저 실패했다고 읽는다.
    try {
      await connection.ensureConnected();
    } catch (error) {
      if (error instanceof UnityUnreachableError) {
        return text(describeStatus({ connected: false, endpoint: connection.endpoint, now: Date.now() }));
      }
      return { ...text(failureText("Could not check Unity", error)), isError: true };
    }

    const state = store.getState() as unknown as Record<string, unknown> | undefined;
    return text(describeStatus({
      connected: connection.isConnected(),
      endpoint: connection.endpoint,
      reading: state?.reading as number | undefined,
      frame: state?.frame as number | undefined,
      scene: state?.scene as string | undefined,
      lastReadingAt: store.getLastReadingAt(),
      now: Date.now(),
    }));
  });

  server.registerTool("get_scene_state", {
    description: "Read the latest folded Unity scene state. Set includeHistory to see how each member's value moved over its last readings. Set root or depth to get the scene as a hierarchy instead of a flat list; a collapsed node reports how many objects sit beneath it and the reading its subtree last moved on.",
    inputSchema: {
      selector: z.string().min(1).optional(),
      includeInactive: z.boolean().optional(),
      includeHistory: z.boolean().optional(),
      root: z.string().min(1).optional(),
      depth: z.number().int().positive().optional(),
    },
  }, async ({ selector, includeInactive, includeHistory, root, depth }) => {
    try {
      await connection.ensureConnected();
      return stateResponse(store, selector, includeInactive, includeHistory, root, depth);
    } catch (error) {
      return {
        ...text(failureText("Scene state is unavailable", error)),
        isError: true,
      };
    }
  });

  server.registerTool("capture_screen", {
    description: "Capture the whole game screen or one target as a PNG.",
    inputSchema: {
      targetId: targetIdSchema().optional(),
      maxEdge: maxEdgeSchema().optional(),
      padding: paddingSchema().optional(),
    },
  }, async ({ targetId, maxEdge, padding }) => {
    if (targetId === undefined && (maxEdge !== undefined || padding !== undefined)) {
      return { ...text("capture_screen requires targetId when maxEdge or padding is set."), isError: true };
    }
    const params = captureScreenParams({ targetId, maxEdge, padding });
    try {
      const requests: ActionRequest[] = [{ id: nextActionId++, method: "capture_screen", params }];
      const results = await connection.sendActions(requests);
      if (results.some((result) => !result.success)) {
        return { content: [{ type: "text", text: `Screenshot failed.\n${describeResults(results)}` }], isError: true };
      }
      const capture = findCapture(results);
      if (capture === undefined) {
        return { ...text("Screenshot failed: Unity returned an invalid capture payload."), isError: true };
      }
      return { content: [
        { type: "image", data: capture.data, mimeType: capture.mimeType },
        { type: "text", text: `${capture.width}x${capture.height}; clipped=${capture.clipped}` },
      ] };
    } catch (error) {
      return { ...text(failureText("Screenshot failed", error)), isError: true };
    }
  });

  server.registerTool("click", {
    description: "Click a Unity target by instance id.",
    inputSchema: { targetId: targetIdSchema() },
  }, async ({ targetId }) => dispatchOne(connection, "button_click", [targetId]));

  server.registerTool("enter_text", {
    description: "Enter text into a Unity target by instance id.",
    inputSchema: { targetId: targetIdSchema(), text: z.string() },
  }, async ({ targetId, text: value }) => dispatchOne(connection, "enter_text", [targetId, value]));

  server.registerTool("move_mouse", {
    description: "Move the virtual mouse in top-left-origin screen pixels.",
    inputSchema: { x: z.number(), y: z.number() },
  }, async ({ x, y }) => dispatchOne(connection, "move_mouse", [x, y]));

  server.registerTool("mouse_button", {
    description: "Click, hold, or release a virtual mouse button.",
    inputSchema: { button: mouseButtonSchema(), action: z.enum(["click", "down", "up"]) },
  }, async ({ button, action }) => dispatchActions(connection, action === "click"
    ? [{ method: "mouse_down", params: [button] }, { method: "mouse_up", params: [button] }]
    : [{ method: action === "down" ? "mouse_down" : "mouse_up", params: [button] }]));

  server.registerTool("press_key", {
    description: "Click, hold, or release a Unity KeyCode key.",
    inputSchema: {
      key: keySchema(),
      action: z.enum(["click", "down", "up"]),
      seconds: z.number().positive().optional(),
    },
  }, async ({ key, action, seconds }) => {
    if (action !== "click" && seconds !== undefined) {
      return { ...text("press_key seconds is valid only when action is click."), isError: true };
    }
    if (action === "click") return dispatchOne(connection, "key_click", [key, seconds ?? 0.05]);
    return dispatchOne(connection, action === "down" ? "key_down" : "key_up", [key]);
  });

  server.registerTool("set_axis", {
    description: "Set a virtual Unity input axis.",
    inputSchema: { name: inputNameSchema(), value: z.number() },
  }, async ({ name, value }) => dispatchOne(connection, "set_axis", [name, value]));

  server.registerTool("set_button", {
    description: "Set a virtual Unity input button state.",
    inputSchema: { name: inputNameSchema(), pressed: z.boolean() },
  }, async ({ name, pressed }) => dispatchOne(connection, "set_button", [name, pressed]));

  const noInput = {};
  server.registerTool("pause_game", { description: "Pause Unity game time.", inputSchema: noInput },
    async () => dispatchOne(connection, "pause_time", []));
  server.registerTool("resume_game", { description: "Resume Unity game time.", inputSchema: noInput },
    async () => dispatchOne(connection, "resume_time", []));
  server.registerTool("reset_game", {
    description: "Reset the current Unity game.",
    inputSchema: { clearPlayerPrefs: z.boolean().optional() },
  }, async ({ clearPlayerPrefs }) => dispatchOne(connection, "reset_game", [{ clearPlayerPrefs: clearPlayerPrefs ?? false }]));
  server.registerTool("start_readings", { description: "Start the play-session scene readings.", inputSchema: noInput },
    async () => dispatchOne(connection, "start_readings", []));
  server.registerTool("stop_readings", { description: "Stop the play-session scene readings.", inputSchema: noInput },
    async () => dispatchOne(connection, "stop_readings", []));

  server.registerTool("perform_actions", {
    description: "Send a raw action sequence to Unity in one frame-aligned batch. Each action carries a method and that method's own named arguments, such as {\"method\":\"key_down\",\"key\":\"Space\"}.",
    inputSchema: { actions: z.array(performActionSchema).min(1) },
  }, async ({ actions }) => dispatchActions(connection, actions.map(toWireAction)));
}
