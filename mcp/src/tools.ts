import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

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

const targetIdSchema = z.number().int();
const mouseButtonSchema = z.number().int().min(0).max(2);
const keySchema = z.string().min(1);
const captureOptionsSchema = z.object({
  maxEdge: z.number().int().positive().optional(),
  padding: z.number().int().nonnegative().optional(),
}).strict();
const rawActionSchema = z.union([
  z.object({ method: z.literal("button_click"), params: z.tuple([targetIdSchema]) }).strict(),
  z.object({ method: z.literal("enter_text"), params: z.tuple([targetIdSchema, z.string()]) }).strict(),
  z.object({ method: z.literal("move_mouse"), params: z.tuple([z.number(), z.number()]) }).strict(),
  z.object({ method: z.literal("mouse_down"), params: z.tuple([mouseButtonSchema]) }).strict(),
  z.object({ method: z.literal("mouse_up"), params: z.tuple([mouseButtonSchema]) }).strict(),
  z.object({ method: z.literal("key_click"), params: z.tuple([keySchema, z.number().positive()]) }).strict(),
  z.object({ method: z.literal("key_down"), params: z.tuple([keySchema]) }).strict(),
  z.object({ method: z.literal("key_up"), params: z.tuple([keySchema]) }).strict(),
  z.object({ method: z.literal("set_axis"), params: z.tuple([z.string().min(1), z.number()]) }).strict(),
  z.object({ method: z.literal("set_button"), params: z.tuple([z.string().min(1), z.boolean()]) }).strict(),
  z.object({ method: z.literal("pause_time"), params: z.tuple([]) }).strict(),
  z.object({ method: z.literal("resume_time"), params: z.tuple([]) }).strict(),
  z.object({
    method: z.literal("reset_game"),
    params: z.tuple([z.object({ clearPlayerPrefs: z.boolean() }).strict()]),
  }).strict(),
  z.object({ method: z.literal("start_readings"), params: z.tuple([]) }).strict(),
  z.object({ method: z.literal("stop_readings"), params: z.tuple([]) }).strict(),
  z.object({
    method: z.literal("capture_screen"),
    params: z.union([
      z.tuple([]),
      z.tuple([targetIdSchema]),
      z.tuple([targetIdSchema, captureOptionsSchema]),
    ]),
  }).strict(),
]);

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
      content: [{ type: "text", text: `Unity action failed: ${errorMessage(error)}` }],
      isError: true,
    };
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
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
        ...text(`Scene state is unavailable: ${errorMessage(error)}`),
        isError: true,
      };
    }
  });

  server.registerTool("capture_screen", {
    description: "Capture the whole game screen or one target as a PNG.",
    inputSchema: {
      targetId: z.number().int().optional(),
      maxEdge: z.number().int().positive().optional(),
      padding: z.number().int().nonnegative().optional(),
    },
  }, async ({ targetId, maxEdge, padding }) => {
    const optionsSpecified = maxEdge !== undefined || padding !== undefined;
    if (optionsSpecified && targetId === undefined) {
      return { ...text("capture_screen requires targetId when maxEdge or padding is set."), isError: true };
    }
    const params = targetId === undefined ? [] : optionsSpecified
      ? [targetId, { ...(maxEdge === undefined ? {} : { maxEdge }), ...(padding === undefined ? {} : { padding }) }]
      : [targetId];
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
      return { ...text(`Screenshot failed: ${errorMessage(error)}`), isError: true };
    }
  });

  server.registerTool("click", {
    description: "Click a Unity target by instance id.",
    inputSchema: { targetId: targetIdSchema },
  }, async ({ targetId }) => dispatchOne(connection, "button_click", [targetId]));

  server.registerTool("enter_text", {
    description: "Enter text into a Unity target by instance id.",
    inputSchema: { targetId: targetIdSchema, text: z.string() },
  }, async ({ targetId, text: value }) => dispatchOne(connection, "enter_text", [targetId, value]));

  server.registerTool("move_mouse", {
    description: "Move the virtual mouse in top-left-origin screen pixels.",
    inputSchema: { x: z.number(), y: z.number() },
  }, async ({ x, y }) => dispatchOne(connection, "move_mouse", [x, y]));

  server.registerTool("mouse_button", {
    description: "Click, hold, or release a virtual mouse button.",
    inputSchema: { button: mouseButtonSchema, action: z.enum(["click", "down", "up"]) },
  }, async ({ button, action }) => dispatchActions(connection, action === "click"
    ? [{ method: "mouse_down", params: [button] }, { method: "mouse_up", params: [button] }]
    : [{ method: action === "down" ? "mouse_down" : "mouse_up", params: [button] }]));

  server.registerTool("press_key", {
    description: "Click, hold, or release a Unity KeyCode key.",
    inputSchema: {
      key: keySchema,
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
    inputSchema: { name: z.string().min(1), value: z.number() },
  }, async ({ name, value }) => dispatchOne(connection, "set_axis", [name, value]));

  server.registerTool("set_button", {
    description: "Set a virtual Unity input button state.",
    inputSchema: { name: z.string().min(1), pressed: z.boolean() },
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
    description: "Send a raw action sequence to Unity in one frame-aligned batch.",
    inputSchema: { actions: z.array(rawActionSchema).min(1) },
  }, async ({ actions }) => dispatchActions(connection, actions.map((action) => ({
    method: action.method,
    params: [...action.params],
  }))));
}
