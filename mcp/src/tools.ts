import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { UnityUnreachableError } from "./connection.js";
import type { ActionRequest, ActionResult, UnityConnection } from "./connection.js";
import {
  objectKey,
  type FoldedPulseState,
  type PulseObject,
  type PulseStatic,
  type PulseStore,
  type Staleness,
  type UnreadableFrame,
} from "./pulse.js";
import {
  clickWireActions,
  dragWireActions,
  hoverWireActions,
  resolveObjectId,
  resolvePoint,
  targetRefIssue,
  type Resolution,
  type TargetRef,
} from "./target-ref.js";
import { REDACTED_TEXT } from "./secrets.js";
import { searchTargets } from "./search.js";
import { foldIntoTree, UNLIMITED_DEPTH, type TreeNode } from "./tree.js";
import { describeWaitOutcome, waitForCondition } from "./wait.js";
import { visibleElements } from "./visible.js";

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

/// 대상을 가리키는 field 셋. 대상을 받는 모든 tool 이 이 모양을 쓴다(`target-ref.ts`).
/// 부를 때마다 새로 만든다 — 이유는 위 주석과 같다.
const targetRefShape = () => ({
  targetId: targetIdSchema().optional(),
  selector: z.string().min(1).optional(),
  x: z.number().optional(),
  y: z.number().optional(),
});
const targetRefObjectSchema = () => z.object(targetRefShape()).strict();
/// 게임 오브젝트만 받는 tool 용. 화면 좌표는 오브젝트를 가리키지 않는다.
const objectRefShape = () => ({
  targetId: targetIdSchema().optional(),
  selector: z.string().min(1).optional(),
});
const mouseButtonSchema = () => z.number().int().min(0).max(2);
const keySchema = () => z.string().min(1);
const inputNameSchema = () => z.string().min(1);
const maxEdgeSchema = () => z.number().int().positive();
const paddingSchema = () => z.number().int().nonnegative();

/// `wait_for_condition` 의 `memberEquals[].equals` 가 받는 값. 재귀적인 `JsonValue` 전체를
/// 받지 않는 이유는 두 가지다: 이 tool 이 실제로 겨누는 값들(`IsStreaming` 같은 boolean,
/// 개수 같은 number, 상태 이름 같은 string)이 전부 primitive 이고, `z.lazy` 로 자기 자신을
/// 참조하는 recursive schema 는 `zod-to-json-schema` 가 무한 재귀로 죽인다(`schema.test.ts` 가
/// 이것을 잡는다). `MemberCondition.equals` 의 선언 type 은 여전히 `JsonValue` 다 — primitive
/// 는 그 부분집합이라 그대로 대입된다.
const equalsValueSchema = () => z.union([z.string(), z.number(), z.boolean(), z.null()]);

const memberEqualsSchema = () => z.object({
  selector: z.string().min(1),
  on: z.string().min(1),
  member: z.string().min(1),
  among: z.number().int().optional(),
  equals: equalsValueSchema(),
}).strict();

/// `timeoutMilliseconds` 를 안 주면 이 값을 쓴다. `wait_for_condition` 이 "절대 무한히 기다리지
/// 않는다" 를 지키려면 상한도 있어야 하므로 zod schema 에 `WAIT_MAX_TIMEOUT_MS` 를 건다.
const WAIT_DEFAULT_TIMEOUT_MS = 5_000;
const WAIT_MAX_TIMEOUT_MS = 30_000;

/// `perform_actions` 가 받는 action 하나.
///
/// Unity 로 나가는 wire 형식은 `{ method, params: [...] }` 의 위치 인자지만, 입력은 이름 있는
/// field 로 받는다. `z.tuple` 은 draft-07 의 배열형 `items` 로 변환되고, draft 2020-12 는 위치별
/// schema 를 `prefixItems` 로만 받으므로 Anthropic API 가 tool 목록 전체를 400 으로 거절한다.
/// `params` 배열은 `toWireAction` 이 만든다.
export const performActionSchema = z.discriminatedUnion("method", [
  z.object({ method: z.literal("click"), ...targetRefShape() }).strict(),
  z.object({ method: z.literal("hover"), ...targetRefShape() }).strict(),
  z.object({ method: z.literal("drag"), from: targetRefObjectSchema(), to: targetRefObjectSchema() }).strict(),
  z.object({ method: z.literal("enter_text"), ...objectRefShape(), text: z.string() }).strict(),
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
    ...objectRefShape(),
    maxEdge: maxEdgeSchema().optional(),
    padding: paddingSchema().optional(),
  }).strict(),
]).superRefine((action, ctx) => {
  const report = (issue: string | undefined, path: string[] = []) => {
    if (issue !== undefined) ctx.addIssue({ code: z.ZodIssueCode.custom, path, message: issue });
  };
  switch (action.method) {
    case "click":
    case "hover":
      return report(targetRefIssue(action, action.method));
    case "drag":
      report(targetRefIssue(action.from, "drag from"), ["from"]);
      return report(targetRefIssue(action.to, "drag to"), ["to"]);
    case "enter_text":
      return report(objectRefIssue(action, "enter_text"));
    case "capture_screen":
      return report(captureScreenIssue(action));
  }
});

/// 오브젝트만 받는 tool 의 대상 규칙: 하나만, 그리고 좌표는 없다.
function objectRefIssue(ref: TargetRef, what: string): string | undefined {
  if (ref.targetId !== undefined && ref.selector !== undefined) {
    return `${what} takes either targetId or selector, not both.`;
  }
  return ref.targetId === undefined && ref.selector === undefined
    ? `${what} requires targetId or selector.`
    : undefined;
}

/// `maxEdge` 와 `padding` 은 잘라낼 대상을 두고 하는 말이라 대상 없이는 뜻이 없다. 대상은 아예 없어도 된다(전체 화면).
function captureScreenIssue(capture: CaptureScreenArguments): string | undefined {
  const hasTarget = capture.targetId !== undefined || capture.selector !== undefined;
  if (hasTarget) return objectRefIssue(capture, "capture_screen");
  return capture.maxEdge === undefined && capture.padding === undefined
    ? undefined
    : "capture_screen requires targetId or selector when maxEdge or padding is set.";
}

export type PerformAction = z.infer<typeof performActionSchema>;

type WireAction = { method: string; params: unknown[] };

/// batch 를 Unity 로 보낼 wire action 으로 편다.
///
/// `click`, `hover`, `drag` 는 대상을 화면 점으로 풀고 `move_mouse`/`mouse_down`/`mouse_up` 으로 편다.
/// `enter_text`, `capture_screen` 은 selector 를 id 로 바꾼다. 풀려면 reading 이 필요해서 순수 변환인
/// `toWireAction` 이 아니라 여기서 한다. 하나라도 풀지 못하면 아무것도 보내지 않고 그 이유를 낸다.
///
/// 같은 batch 안의 대상은 batch 가 돌기 전의 reading 으로 푼다. 앞선 action 이 대상을 옮기더라도 그 결과는
/// 보지 못하므로, 그런 순서는 batch 를 나누라는 뜻이다.
export function expandActions(
  actions: readonly PerformAction[],
  store: PulseStore,
): { ok: true; wire: WireAction[] } | { ok: false; error: string } {
  const wire: WireAction[] = [];
  for (const action of actions) {
    const expanded = expandAction(action, store);
    if (!expanded.ok) return { ok: false, error: expanded.error };
    wire.push(...expanded.value);
  }
  return { ok: true, wire };
}

function readingOf(store: PulseStore) {
  const staleness = store.getStaleness();
  return { state: store.getState(), staleNote: staleness === undefined ? undefined : stalenessMessage(staleness) };
}

function expandAction(action: PerformAction, store: PulseStore): Resolution<WireAction[]> {
  const { state, staleNote } = readingOf(store);
  const point = (ref: TargetRef, tool: string) => resolvePoint(ref, state, staleNote, tool);
  switch (action.method) {
    case "click":
    case "hover": {
      const at = point(action, action.method);
      if (!at.ok) return at;
      return { ok: true, value: (action.method === "click" ? clickWireActions : hoverWireActions)(at.value) };
    }
    case "drag": {
      const from = point(action.from, "drag from");
      if (!from.ok) return from;
      const to = point(action.to, "drag to");
      if (!to.ok) return to;
      return { ok: true, value: dragWireActions(from.value, to.value) };
    }
    case "enter_text": {
      const id = resolveObjectId(action, state, staleNote, "enter_text");
      if (!id.ok) return id;
      return { ok: true, value: [{ method: "enter_text", params: [id.value, action.text] }] };
    }
    case "capture_screen": {
      if (action.targetId === undefined && action.selector === undefined) {
        return { ok: true, value: [toWireAction(action)] };
      }
      const id = resolveObjectId(action, state, staleNote, "capture_screen");
      if (!id.ok) return id;
      return { ok: true, value: [toWireAction({ ...action, targetId: id.value })] };
    }
    default:
      return { ok: true, value: [toWireAction(action)] };
  }
}

interface CaptureScreenArguments {
  targetId?: number;
  selector?: string;
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
export function toWireAction(action: Exclude<PerformAction, { method: "click" | "hover" | "drag" | "enter_text" }>): { method: string; params: unknown[] } {
  switch (action.method) {
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
  lastUnreadableFrame?: UnreadableFrame;
  staleness?: Staleness;
  now: number;
}

const STALE_RECOVERY =
  "Call start_readings and read again; if the state stays stale, call stop_readings and then start_readings.";

/// 낡은 까닭 하나를 agent 가 읽을 문장으로 바꾼다. 무엇을 믿으면 안 되는지와 어떻게 되돌리는지를 함께 말한다.
function stalenessMessage(staleness: Staleness): string {
  switch (staleness.reason) {
    case "restarted":
      return "Unity restarted its readings and no whole reading of the new run has arrived yet. "
        + "This state belongs to the previous run: its ids, rects, and scene may no longer exist. "
        + STALE_RECOVERY;
    case "disconnected":
      return "The connection to Unity dropped after this reading, so changes since then may be missing. "
        + STALE_RECOVERY;
    case "stopped":
      return "Readings were stopped with stop_readings, so this state no longer follows the game. "
        + "Call start_readings to resume.";
  }
}

function isoTime(at: number | undefined): string | undefined {
  return at === undefined ? undefined : new Date(at).toISOString();
}

/// 모든 읽기 tool 이 상태 앞에 싣는 머리.
///
/// 낡았으면 `stale` 을 맨 앞에 둔다. 그래야 응답을 위에서부터 읽는 agent 가 id 를 쓰기 전에 본다.
/// 낡은 상태를 아예 내주지 않는 쪽은 택하지 않았다 — 직전 장면을 확인하는 데는 여전히 쓸모 있고,
/// 무엇이 낡았는지 말해 주면 쓸지 말지는 agent 가 정할 수 있다.
export function readingHeader(store: PulseStore, state: FoldedPulseState): Record<string, unknown> {
  const staleness = store.getStaleness();
  return {
    ...(staleness === undefined
      ? {}
      : {
          stale: {
            reason: staleness.reason,
            message: stalenessMessage(staleness),
            since: isoTime(staleness.at),
            lastReadingAt: isoTime(staleness.lastReadingAt),
          },
        }),
    ...(state.run === undefined ? {} : { run: state.run }),
    reading: state.reading,
    frame: state.frame,
    scene: state.scene,
  };
}

function describeAge(now: number, at: number): string {
  const secondsAgo = Math.max(0, Math.round((now - at) / 1000));
  return secondsAgo === 0 ? "just now" : `${secondsAgo}s ago`;
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
  const lines = status.lastReadingAt === undefined
    ? [head, "No scene reading has arrived yet. Call start_readings before get_scene_state."]
    : [
        head,
        `${status.staleness?.reason === "stopped" ? "Readings are stopped" : "Readings are running"}: `
        + `reading ${status.reading} on frame ${status.frame} `
        + `arrived ${describeAge(status.now, status.lastReadingAt)}.`,
        `Scene: ${status.scene}.`,
      ];

  if (status.lastReadingAt !== undefined && status.staleness !== undefined) {
    lines.push(
      `This reading is stale (${status.staleness.reason} ${describeAge(status.now, status.staleness.at)}): `
      + stalenessMessage(status.staleness),
    );
  }

  // 마지막으로 읽지 못한 frame 이 있으면(그리고 그 뒤로 정상 reading 이 하나도 안 왔으면 —
  // `PulseStore` 가 성공할 때마다 이 값을 스스로 지운다) 두 분기 모두에 덧붙인다. reading 이
  // 아예 안 왔을 때도, 잘 오다가 멈췄을 때도 사람이 무엇을 못 읽었는지 알아야 하기 때문이다.
  if (status.lastUnreadableFrame !== undefined) {
    lines.push(
      `A frame arrived but could not be read ${describeAge(status.now, status.lastUnreadableFrame.at)}: `
      + `${status.lastUnreadableFrame.reason}.`,
    );
  }

  return lines.join(" ");
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

/// `get_scene_state` 가 static 을 어떻게 실을지.
export interface StaticsQuery {
  /// 싣는지. 안 주면 범위를 좁히지 않은 조회에만 싣는다.
  includeStatics?: boolean;
  /// 선언 타입 이름에 이 문자열이 든 static 만 싣는다(대소문자 구분). 주면 `includeStatics` 를 안 줘도 싣는다.
  staticsDeclaring?: string;
}

/// 응답에 실을 static 과, 싣지 않았다면 그 사실.
///
/// `root`/`selector` 로 좁힌 조회는 버튼·카드 상태를 읽으려는 것이고 static 은 어느 객체에도 매달리지 않는다. 그래서
/// 기본으로 빼고, 뺐다는 것과 몇 개인지를 말한다 — 없는 것과 뺀 것이 같아 보이면 안 된다 (#72). 명시적으로 청하면
/// 싣되, 값은 이미 `PulseStore` 가 가린 그대로다.
function staticsSection(
  statics: readonly PulseStatic[],
  scoped: boolean,
  query: StaticsQuery,
): Record<string, unknown> {
  const wanted = query.includeStatics ?? (!scoped || query.staticsDeclaring !== undefined);
  if (!wanted) {
    return {
      staticsOmitted: {
        count: statics.length,
        reason: "A scoped query leaves out the statics this reading carries. "
          + "Set includeStatics, or staticsDeclaring to pick them by declaring type.",
      },
    };
  }
  const declaring = query.staticsDeclaring;
  return {
    statics: declaring === undefined
      ? statics
      : statics.filter((declared) => declared.declaring.includes(declaring)),
  };
}

/// 좁힌 조회에서 `changed` 는 보여 준 객체의 것만 남긴다.
///
/// 게임은 키를 `<scene>/<selector>|<무엇>` 으로 쓴다(`LiveState.cs` 의 `identity`). static 은 `Declaring::Member` 이고
/// `scene` 은 장면 이름 하나라 어느 객체에도 속하지 않는다.
function changedFor(changed: readonly string[], shown: readonly PulseObject[], scene: string) {
  // 첫 `|` 에서 자르지 않는다. GameObject 이름에 `|` 가 들어가면 selector 안에도 들어간다.
  const prefixes = shown.map((object) => `${objectKey(object, scene)}|`);
  const kept = changed.filter((key) => prefixes.some((prefix) => key.startsWith(prefix)));
  return {
    changed: kept,
    ...(kept.length === changed.length ? {} : { changedOmitted: changed.length - kept.length }),
  };
}

/// 가린 값이 하나라도 실린 응답에 그 뜻을 적는다.
///
/// 구조를 훑지 않고 직렬화한 글에서 찾는다. `wait_for_condition` 은 멤버 값을 `unmet` 문장 안에 JSON 으로 싣기 때문에,
/// 구조만 보면 그 안의 가린 값을 놓친다.
function withRedactionNote(response: Record<string, unknown>): Record<string, unknown> {
  const serialized = JSON.stringify(response);
  if (!serialized.includes("$redacted") && !serialized.includes(`"${REDACTED_TEXT}"`)) return response;
  return {
    redaction: "Values shown as {\"$redacted\":true,...} (or displayed text \"[redacted]\") looked like credentials, "
      + "by name or shape, and are hidden. length is the original string length; null and empty strings are never hidden.",
    ...response,
  };
}

function stateResponse(
  store: PulseStore,
  selector?: string,
  includeInactive = false,
  includeHistory = false,
  root?: string,
  depth?: number,
  staticsQuery: StaticsQuery = {},
): ToolResponse {
  const state = store.getState();
  if (state === undefined || state === null) {
    return text("No scene reading has arrived. Call start_readings to begin a play session, then try again.");
  }

  const record = state as unknown as Record<string, unknown>;
  const header = readingHeader(store, state);
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
  const scoped = selector !== undefined || root !== undefined;

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
    return text(JSON.stringify(withRedactionNote({
      ...header,
      ...(root === undefined ? {} : { root }),
      ...staticsSection(state.statics, scoped, staticsQuery),
      gone: filterGone(record.gone),
      tree,
      ...(includeHistory
        ? { history: historyOf(store, objectsShownIn(tree), scene) }
        : {}),
    }), null, 2));
  }

  const response = withRedactionNote({
    ...header,
    ...(scoped
      ? changedFor(state.changed, (includeInactive ? [...active, ...deactive] : active) as PulseObject[], scene)
      : { changed: state.changed }),
    ...staticsSection(state.statics, scoped, staticsQuery),
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
  });
  return text(JSON.stringify(response, null, 2));
}

const captureAreaSchema = z.object({
  x: z.number(),
  y: z.number(),
  width: z.number().nonnegative(),
  height: z.number().nonnegative(),
}).strict();

/// Unity 가 `capture_screen` 에 돌려주는 값.
///
/// 좌표 metadata(`screen` 이하)는 optional 이다. 0.2.x package 는 그것을 싣지 않고, 그 package 를 쓰는 사람도
/// 새 server 에서 이미지는 계속 받아야 한다. 반대로 이 schema 이전의 server 는 `.strict()` 라 새 package 의
/// 캡처를 거절한다 — package 가 자신과 맞는 server 버전을 고정해 두는 이유 중 하나다.
const capturePayloadSchema = z.object({
  mimeType: z.enum(["image/png", "image/jpeg"]),
  width: z.number().int().positive(),
  height: z.number().int().positive(),
  targetId: z.number().int().optional(),
  clipped: z.boolean(),
  screen: z.object({ width: z.number().int().positive(), height: z.number().int().positive() }).strict().optional(),
  region: captureAreaSchema.optional(),
  requestedRegion: captureAreaSchema.optional(),
  scale: z.object({ x: z.number().positive(), y: z.number().positive() }).strict().optional(),
  frame: z.number().int().optional(),
  scene: z.string().optional(),
  data: z.string().min(1).regex(/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/),
}).strict();

type CapturePayload = z.infer<typeof capturePayloadSchema>;

/// 스크린샷과 함께 싣는 설명. 이미지 픽셀을 `move_mouse` 좌표로 되돌리는 데 필요한 것과, 그 이미지가 어느
/// 순간의 것인지.
export interface CaptureDescription {
  image: { width: number; height: number; mimeType: string };
  clipped: boolean;
  targetId?: number;
  screen?: CapturePayload["screen"];
  region?: CapturePayload["region"];
  requestedRegion?: CapturePayload["requestedRegion"];
  scale?: CapturePayload["scale"];
  toScreen?: string;
  frame?: number;
  scene?: string;
  missing?: string;
  reading?: {
    reading: number;
    frame: number;
    scene: string;
    relation: string;
    sameScene?: boolean;
    stale?: unknown;
  };
}

/// reading 과 캡처 사이가 이만큼 벌어지면 reading 의 rect 가 이 이미지와 맞는다고 말하지 않는다.
///
/// reading 은 초당 한 번 찍힌다(`Pulse.DefaultInterval`). 60fps 에서 두 박자쯤이다 — 그보다 멀면 그 사이 무엇이든
/// 움직였을 수 있다. 같은 이름의 장면을 다시 불렀거나 Play Mode 를 다시 시작해 frame 이 되돌아간 경우도 여기서 걸린다.
const READING_FRAME_TOLERANCE = 120;

/// 캡처 결과와, 지금 든 reading 을 함께 설명한다.
///
/// reading 은 캡처와 같은 순간의 것이 아니다. 그래서 frame 의 선후와 scene 일치를 말하고, scene 이 다르면
/// 그 reading 의 id 를 이 화면에 쓰지 말라고 한다 — reading 이 멈춘 동안 스크린샷만 새 장면을 보였던 것이
/// #69/#71 에서 관찰된 장면이다.
export function describeCapture(
  capture: CapturePayload,
  state: { reading: number; frame: number; scene: string } | undefined,
): CaptureDescription {
  const description: CaptureDescription = {
    image: { width: capture.width, height: capture.height, mimeType: capture.mimeType },
    clipped: capture.clipped,
    ...(capture.targetId === undefined ? {} : { targetId: capture.targetId }),
  };

  if (capture.screen === undefined || capture.region === undefined || capture.scale === undefined) {
    return {
      ...description,
      missing: "This Unity package does not report the capture's screen size, region, scale, frame, or scene. "
        + "Update the package to convert image pixels to move_mouse coordinates.",
    };
  }

  Object.assign(description, {
    screen: capture.screen,
    region: capture.region,
    ...(capture.requestedRegion === undefined ? {} : { requestedRegion: capture.requestedRegion }),
    scale: capture.scale,
    toScreen: "screenX = region.x + imageX / scale.x; screenY = region.y + imageY / scale.y "
      + "(top-left origin, the space move_mouse and scene rects use)"
      + (capture.clipped
        ? ". The image holds only region, the on-screen part of requestedRegion; the rest was off screen and is not in the image"
        : ""),
    ...(capture.frame === undefined ? {} : { frame: capture.frame }),
    ...(capture.scene === undefined ? {} : { scene: capture.scene }),
  });

  if (state === undefined || capture.frame === undefined) {
    return description;
  }

  const gap = capture.frame - state.frame;
  const sameScene = capture.scene === undefined ? undefined : capture.scene === state.scene;
  const order = gap >= 0
    ? `The held reading ${state.reading} was taken ${gap} frames before this image.`
    : `The held reading ${state.reading} was taken ${-gap} frames after this image.`;
  const relation = sameScene === false
    ? `The held reading ${state.reading} is from scene "${state.scene}", but this image shows "${capture.scene}". `
      + "Do not use that reading's ids or rects for what this image shows; read the scene again."
    : Math.abs(gap) > READING_FRAME_TOLERANCE
      ? `${order} Its rects and values describe frame ${state.frame}, not this image; `
        + "read the scene again before aiming at anything in this image."
      : `${order} Its rects and values describe frame ${state.frame}.`;
  return {
    ...description,
    reading: {
      reading: state.reading,
      frame: state.frame,
      scene: state.scene,
      relation,
      ...(sameScene === undefined ? {} : { sameScene }),
    },
  };
}

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
      lastUnreadableFrame: store.getLastUnreadableFrame(),
      staleness: store.getStaleness(),
      now: Date.now(),
    }));
  });

  server.registerTool("get_scene_state", {
    description: "Read the latest folded Unity scene state. selector narrows to objects whose full selector path contains that substring, matched case-sensitively; it never does a whole-value match. For narrowing by name, displayed text, component, or whether an object is actionable, and for a compact result instead of this tool's full changed/statics payload, call search_targets instead. Set includeHistory to see how each member's value moved over its last readings. Set root or depth to get the scene as a hierarchy instead of a flat list; a collapsed node reports how many objects sit beneath it and the reading its subtree last moved on. A query scoped with selector or root leaves out statics (staticsOmitted says how many) and keeps only the changed entries of the objects it shows; set includeStatics, or staticsDeclaring to pick statics whose declaring type contains that substring. Values that look like credentials, by name or by shape, are always replaced with {\"$redacted\":true,\"because\":...,\"length\":...}; null and empty strings are never hidden.",
    inputSchema: {
      selector: z.string().min(1).optional(),
      includeInactive: z.boolean().optional(),
      includeHistory: z.boolean().optional(),
      root: z.string().min(1).optional(),
      depth: z.number().int().positive().optional(),
      includeStatics: z.boolean().optional(),
      staticsDeclaring: z.string().min(1).optional(),
    },
  }, async ({ selector, includeInactive, includeHistory, root, depth, includeStatics, staticsDeclaring }) => {
    try {
      await connection.ensureConnected();
      return stateResponse(
        store, selector, includeInactive, includeHistory, root, depth, { includeStatics, staticsDeclaring });
    } catch (error) {
      return {
        ...text(failureText("Scene state is unavailable", error)),
        isError: true,
      };
    }
  });

  server.registerTool("get_visible_elements", {
    description: [
      "List the UI elements the player can see right now, and what each one is showing:",
      "the text of a label, the fill of a bar, the value of a slider or toggle.",
      "This reads the latest scene reading already in hand, so it costs no round trip to the game.",
      "Elements that are turned off, off-screen, or covered by something drawn on top of them are left out.",
      "Set includeHidden to get them back; every element then carries the active, onScreen, and covered",
      "flags that say why it would have been left out.",
      "The covered flag is a guess made from overlapping rectangles. It does not know canvas sorting order,",
      "overrideSorting, transparent images, or masks, so it can be wrong; when you need a reliable answer",
      "about what is on the screen, call capture_screen.",
      "UI types a game defined itself by subclassing Image or Button arrive under the game's own type name",
      "and are not listed here.",
      "selector here narrows to elements whose full selector path contains that substring, matched",
      "case-sensitively, the same contract get_scene_state's selector uses.",
    ].join(" "),
    inputSchema: {
      selector: z.string().min(1).optional(),
      includeHidden: z.boolean().optional(),
    },
  }, async ({ selector, includeHidden }) => {
    try {
      await connection.ensureConnected();
      const state = store.getState();
      if (state === undefined || state === null) {
        return text("No scene reading has arrived. Call start_readings to begin a play session, then try again.");
      }
      return text(JSON.stringify(withRedactionNote({
        ...readingHeader(store, state),
        elements: visibleElements(state, { selector, includeHidden }),
      }), null, 2));
    } catch (error) {
      return {
        ...text(failureText("Visible elements are unavailable", error)),
        isError: true,
      };
    }
  });

  server.registerTool("search_targets", {
    description: [
      "Search Unity scene objects and return a compact candidate list, without get_scene_state's full",
      "changed/statics payload. Narrow with name (the object's own name, its sibling index stripped),",
      "displayedText (a string a UI component on the object itself is currently showing, such as a label),",
      "component (a substring of a type name in the object's own component list, such as \"Button\" or",
      "\"TMPro\"), and actionable (true keeps only objects offering a click, a key, or a pointer message;",
      "false keeps only the ones offering none). Every filter given must match; omit a filter to skip it.",
      "Matching is substring and case-insensitive by default. Set exact for a whole-value match instead of",
      "substring, and caseSensitive to require exact case; both apply to name, displayedText, and component",
      "alike. This is a separate, narrower contract from get_scene_state's own selector, which stays an",
      "unchanged case-sensitive substring match against the full selector path.",
      "Results are capped at limit (default 20); when more objects matched, truncated is true and total",
      "carries the real count, even beyond the cap. duplicateNames lists any leaf name shared by two or more",
      "matching objects, counted before the cap is applied, since those are distinct instances an id or full",
      "selector is needed to tell apart.",
      "Follow up on a candidate with get_scene_state({ selector: candidate.selector }) for its full state, or",
      "click/enter_text with candidate.id to act on it. This tool never creates or guesses objects, and never",
      "acts on a candidate itself.",
    ].join(" "),
    inputSchema: {
      name: z.string().min(1).optional(),
      displayedText: z.string().min(1).optional(),
      component: z.string().min(1).optional(),
      actionable: z.boolean().optional(),
      exact: z.boolean().optional(),
      caseSensitive: z.boolean().optional(),
      includeInactive: z.boolean().optional(),
      limit: z.number().int().positive().optional(),
    },
  }, async ({ name, displayedText, component, actionable, exact, caseSensitive, includeInactive, limit }) => {
    try {
      await connection.ensureConnected();
      const state = store.getState();
      if (state === undefined || state === null) {
        return text("No scene reading has arrived. Call start_readings to begin a play session, then try again.");
      }
      return text(JSON.stringify(withRedactionNote({
        ...readingHeader(store, state),
        ...searchTargets(state, {
          name, displayedText, component, actionable, exact, caseSensitive, includeInactive, limit,
        }),
      }), null, 2));
    } catch (error) {
      return {
        ...text(failureText("Target search is unavailable", error)),
        isError: true,
      };
    }
  });

  server.registerTool("capture_screen", {
    description: "Capture the whole game screen (JPEG, longest edge 1024 by default) or one target given by targetId or selector (PNG, cropped with padding). Alongside the image it returns the Unity Screen.width/height that input uses, the captured region in top-left screen pixels, the image-per-screen-pixel scale, and a toScreen formula to turn an image pixel into move_mouse coordinates; plus the Unity frame and scene the image was taken on, and how the held scene reading relates to it (frames before/after, same scene or not). For a target crop, region includes the padding and is larger than the element. Never assume the screen size from the image size.",
    inputSchema: {
      ...objectRefShape(),
      maxEdge: maxEdgeSchema().optional(),
      padding: paddingSchema().optional(),
    },
  }, async (input) => {
    const issue = captureScreenIssue(input);
    if (issue !== undefined) return { ...text(issue), isError: true };
    let targetId = input.targetId;
    if (input.selector !== undefined) {
      const { state, staleNote } = readingOf(store);
      const id = resolveObjectId(input, state, staleNote, "capture_screen");
      if (!id.ok) return { ...text(id.error), isError: true };
      targetId = id.value;
    }
    const params = captureScreenParams({ targetId, maxEdge: input.maxEdge, padding: input.padding });
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
      // 첫 줄은 예전 모양 그대로 둔다. 그 뒤에 좌표 변환과 reading 관계를 JSON 으로 싣는다.
      const state = store.getState();
      const description = describeCapture(capture, state);
      // 낡은 reading 은 frame 이 가까워도 이 화면의 상태가 아니다. `readingHeader` 와 같은 `stale` 을 관계에 싣는다 (#69).
      const stale = state === undefined ? undefined : readingHeader(store, state).stale;
      if (description.reading !== undefined && stale !== undefined) {
        description.reading = { ...description.reading, stale };
      }
      return { content: [
        { type: "image", data: capture.data, mimeType: capture.mimeType },
        {
          type: "text",
          text: `${capture.width}x${capture.height}; clipped=${capture.clipped}\n`
            + JSON.stringify(description, null, 2),
        },
      ] };
    } catch (error) {
      return { ...text(failureText("Screenshot failed", error)), isError: true };
    }
  });

  const TARGET_HELP = "Aim with exactly one of targetId (instance id from get_scene_state), selector (the exact selector from get_scene_state or search_targets, e.g. \"Canvas[0]/Panel[1]/Button[0]\"), or x and y (game screen pixels from the top left, the same space as move_mouse). "
    + "id and selector aim at the center of the last reading's rect and fail without moving when the target is inactive, off screen, covered, or the reading is stale.";

  // click, hover, drag 는 같은 일을 한다: 대상을 점으로 풀어 가상 마우스로 보낸다. 각 handler 는 action 하나를 만들어
  // `perform_actions` 와 같은 `expandActions` 로 보낸다.
  const runAction = async (action: PerformAction) => {
    const expanded = expandActions([action], store);
    if (!expanded.ok) return { ...text(expanded.error), isError: true };
    return dispatchActions(connection, expanded.wire);
  };
  const refIssue = (issue: string | undefined) => issue === undefined ? undefined : { ...text(issue), isError: true };

  server.registerTool("click", {
    description: "Click a Unity object or screen position with the virtual mouse: move the pointer there, press the left button, release it. " + TARGET_HELP
      + " It goes through the game's own input path, so Buttons, uGUI pointer handlers, and colliders (OnMouseDown) all respond, and something drawn on top of the target takes the click. A successful result only means the input was sent; read the state again to see the effect.",
    inputSchema: targetRefShape(),
  }, async (target) => refIssue(targetRefIssue(target, "click")) ?? runAction({ method: "click", ...target }));

  server.registerTool("hover", {
    description: "Rest the virtual pointer on a Unity object or screen position without pressing any button, so hover handlers run through the game's own input path: uGUI OnPointerEnter/OnPointerExit and collider OnMouseEnter/OnMouseOver. The pointer stays there until the next pointer input moves it. " + TARGET_HELP
      + " Read tooltips or highlights afterwards with get_scene_state or capture_screen.",
    inputSchema: targetRefShape(),
  }, async (target) => refIssue(targetRefIssue(target, "hover")) ?? runAction({ method: "hover", ...target }));

  server.registerTool("drag", {
    description: "Drag from one place to another: press the left mouse button on `from`, glide the pointer to `to`, and release there. `from` and `to` each take exactly one of targetId, selector, or x and y, as click does. " + TARGET_HELP
      + " Both ends are resolved before the button is pressed.",
    inputSchema: { from: targetRefObjectSchema(), to: targetRefObjectSchema() },
  }, async ({ from, to }) => refIssue(targetRefIssue(from, "drag from") ?? targetRefIssue(to, "drag to"))
    ?? runAction({ method: "drag", from, to }));

  server.registerTool("enter_text", {
    description: "Enter text into a Unity input field. Give targetId (instance id) or selector (exact selector from get_scene_state or search_targets), not both; a screen position is not accepted.",
    inputSchema: { ...objectRefShape(), text: z.string() },
  }, async (input) => refIssue(objectRefIssue(input, "enter_text")) ?? runAction({ method: "enter_text", ...input }));

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
    description: "Send a raw action sequence to Unity in one frame-aligned batch. Each action carries a method and that method's own named arguments, such as {\"method\":\"key_down\",\"key\":\"Space\"}. "
      + "A successful result only means Unity accepted the input, not that its in-game effect has appeared yet; call wait_for_condition or read the state again to confirm the effect.",
    inputSchema: { actions: z.array(performActionSchema).min(1) },
  }, async ({ actions }) => {
    const expanded = expandActions(actions, store);
    if (!expanded.ok) return { ...text(expanded.error), isError: true };
    return dispatchActions(connection, expanded.wire);
  });

  server.registerTool("wait_for_condition", {
    description: [
      "Wait, up to a time limit, for the folded scene state to satisfy a condition: a reading or frame",
      "newer than a baseline you already hold (from an earlier get_scene_state or get_unity_status),",
      "a target scene, one or more member values, or several of these together — all given conditions",
      "must hold at once. Use this after perform_actions when the action's in-game effect may not be",
      "visible in the very next reading yet, such as a scene transition or a value that settles a moment",
      "later. This never blocks forever: it always returns within timeoutMilliseconds (default "
      + `${WAIT_DEFAULT_TIMEOUT_MS}, maximum ${WAIT_MAX_TIMEOUT_MS}), even when nothing changes and`,
      "Unity therefore sends no pulse at all. Read the returned met, disconnected, and unmet fields —",
      "a successful action result never guarantees this condition held.",
      "Reading and frame numbers restart when Unity starts a new reading run; pass the run from the same",
      "response as sinceRun, and any reading of a different run counts as newer. A stale state never meets a condition.",
    ].join(" "),
    inputSchema: {
      sinceRun: z.string().min(1).optional(),
      sinceReading: z.number().int().nonnegative().optional(),
      sinceFrame: z.number().int().nonnegative().optional(),
      scene: z.string().min(1).optional(),
      memberEquals: z.array(memberEqualsSchema()).min(1).optional(),
      timeoutMilliseconds: z.number().int().positive().max(WAIT_MAX_TIMEOUT_MS).optional(),
    },
  }, async ({ sinceRun, sinceReading, sinceFrame, scene, memberEquals, timeoutMilliseconds }, extra) => {
    if (sinceReading === undefined && sinceFrame === undefined && scene === undefined && memberEquals === undefined) {
      return {
        ...text("wait_for_condition requires at least one of sinceReading, sinceFrame, scene, or memberEquals."),
        isError: true,
      };
    }
    try {
      await connection.ensureConnected();
    } catch (error) {
      return { ...text(failureText("Unity is unreachable", error)), isError: true };
    }
    const outcome = await waitForCondition(store, connection, {
      sinceRun,
      sinceReading,
      sinceFrame,
      scene,
      memberEquals,
      timeoutMilliseconds: timeoutMilliseconds ?? WAIT_DEFAULT_TIMEOUT_MS,
    }, extra.signal);
    const { payload, isError } = describeWaitOutcome(outcome, store.getLastUnreadableFrame());
    return {
      ...text(JSON.stringify(withRedactionNote(payload as unknown as Record<string, unknown>), null, 2)),
      ...(isError ? { isError: true } : {}),
    };
  });
}
