import type { FoldedPulseState, PulseObject } from "./pulse.js";

/// 게임 안의 대상을 가리키는 방법. 세 방식 중 정확히 하나를 담는다.
///
/// - `targetId`: scan 이 보고한 instance id
/// - `selector`: `get_scene_state`/`search_targets` 가 보고한 selector 와 정확히 같은 문자열
/// - `x`, `y`: `move_mouse` 와 같은 좌상단 원점 게임 화면 픽셀
///
/// 대상을 받는 모든 tool 이 이 모양을 쓴다(`.agents/docs/tool-design.md`). 무엇을 받는지는 소비자가 갈린다:
/// 점을 겨누는 tool(`click`, `hover`, `drag`)은 셋 다, 게임 오브젝트를 받는 tool(`enter_text`,
/// `capture_screen`)은 앞의 둘만 받는다.
export interface TargetRef {
  targetId?: number;
  selector?: string;
  x?: number;
  y?: number;
}

/// 게임 화면 위의 한 점. `move_mouse` 가 받는 것과 같은 좌상단 원점 픽셀이다.
export interface Point {
  x: number;
  y: number;
}

export type Resolution<T> = { ok: true; value: T } | { ok: false; error: string };

/// 입력 조합이 잘못됐으면 그 이유를, 맞으면 `undefined` 를.
///
/// zod schema 의 `superRefine` 과 tool handler 가 같은 규칙을 쓰도록 여기 한 자리에 둔다. `what` 은
/// 오류 문장에 들어갈 이름이다(`click`, `drag from`).
export function targetRefIssue(ref: TargetRef, what: string): string | undefined {
  const byCoordinates = ref.x !== undefined || ref.y !== undefined;
  const given = [ref.targetId !== undefined, ref.selector !== undefined, byCoordinates]
    .filter(Boolean).length;
  if (given !== 1) {
    return `${what} requires exactly one of targetId, selector, or x and y.`;
  }
  if (byCoordinates && (ref.x === undefined || ref.y === undefined)) {
    return `${what} requires both x and y when aiming at a screen position.`;
  }
  return undefined;
}

interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

function rectOf(object: PulseObject): Rect | undefined {
  const rect = object.rect;
  if (typeof rect !== "object" || rect === null || Array.isArray(rect)) return undefined;
  const { x, y, w, h } = rect as Record<string, unknown>;
  if (![x, y, w, h].every((value) => typeof value === "number" && Number.isFinite(value))) {
    return undefined;
  }
  return { x, y, w, h } as Rect;
}

function flagOf(object: PulseObject, name: string): boolean | undefined {
  const value = object[name];
  return typeof value === "boolean" ? value : undefined;
}

function describe(object: PulseObject, scene: string): string {
  return `${object.scene ?? scene}/${object.selector} (id ${object.id})`;
}

interface Pooled {
  object: PulseObject;
  active: boolean;
}

/// reading 에서 대상 하나를 찾는다. 낡았거나 없거나 못 찾았거나 모호하면 그 까닭을 낸다.
///
/// selector 는 `object.selector` 와 정확히 같은 것만 대상이다 — `get_scene_state` 의 contains 검색과 달리,
/// 무언가를 하는 일은 "아마 이것" 으로 하지 않는다. `staleNote` 는 reading 이 낡았을 때 그 까닭과 복구 방법이고,
/// 낡은 reading 의 id 와 rect 는 이미 없어졌거나 옮겨 간 것을 가리킬 수 있어 그 자리에서 거절한다.
function find(
  ref: TargetRef,
  state: FoldedPulseState | undefined,
  staleNote: string | undefined,
  tool: string,
): Resolution<Pooled> {
  if (state === undefined) {
    return { ok: false, error: `${tool}: no scene reading has arrived yet. Call start_readings first.` };
  }
  if (staleNote !== undefined) {
    return { ok: false, error: `${tool}: the reading is stale, so its ids and rects cannot be trusted. ${staleNote}` };
  }

  const pool: Pooled[] = [
    ...state.active.map((object) => ({ object, active: true })),
    ...state.deactive.map((object) => ({ object, active: false })),
  ];

  if (ref.targetId !== undefined) {
    const found = pool.find((entry) => entry.object.id === ref.targetId);
    return found === undefined
      ? { ok: false, error: `${tool}: no object with id ${ref.targetId} in the last reading. Ids stop being valid when the scene reloads or the object is destroyed or recreated; read again with get_visible_elements or get_scene_state to get a new id.` }
      : { ok: true, value: found };
  }

  const matches = pool.filter((entry) => entry.object.selector === ref.selector);
  if (matches.length === 0) {
    return { ok: false, error: `${tool}: no object has the selector "${ref.selector}". Use search_targets to find its exact selector.` };
  }
  if (matches.length > 1) {
    const candidates = matches.map((entry) => describe(entry.object, state.scene)).join(", ");
    return { ok: false, error: `${tool}: selector "${ref.selector}" matches ${matches.length} objects: ${candidates}. Use targetId.` };
  }
  return { ok: true, value: matches[0]! };
}

/// 대상을 화면 점으로 푼다. 좌표는 그대로 쓰고, id 와 selector 는 그 rect 의 중심을 겨눈다.
///
/// 꺼졌거나 화면 밖이거나 가려졌거나 겨눌 면적이 없는 대상은 누르기 전에 거절한다. 그런 대상에는 입력을
/// 보내도 소용이 없고, 다른 오브젝트가 대신 받을 수 있다.
export function resolvePoint(
  ref: TargetRef,
  state: FoldedPulseState | undefined,
  staleNote: string | undefined,
  tool: string,
): Resolution<Point> {
  if (ref.x !== undefined && ref.y !== undefined) {
    return { ok: true, value: { x: ref.x, y: ref.y } };
  }

  const found = find(ref, state, staleNote, tool);
  if (!found.ok) return found;

  const { object, active } = found.value;
  const name = describe(object, state!.scene);
  if (!active) return { ok: false, error: `${tool}: ${name} is not active in the scene.` };
  if (flagOf(object, "onScreen") === false) return { ok: false, error: `${tool}: ${name} is off screen.` };
  if (flagOf(object, "covered") === true) {
    return { ok: false, error: `${tool}: ${name} is covered by something drawn on top of it.` };
  }
  const rect = rectOf(object);
  if (rect === undefined || rect.w <= 0 || rect.h <= 0) {
    return { ok: false, error: `${tool}: ${name} has no on-screen area to aim at. Use x and y instead.` };
  }
  return { ok: true, value: { x: rect.x + rect.w / 2, y: rect.y + rect.h / 2 } };
}

/// 대상을 게임 오브젝트의 instance id 로 푼다. id 는 그대로 통과하고(Unity 가 살아 있는지 확인한다),
/// selector 는 reading 에서 id 로 바꾼다. 좌표는 오브젝트를 가리키지 않으므로 거절한다.
export function resolveObjectId(
  ref: TargetRef,
  state: FoldedPulseState | undefined,
  staleNote: string | undefined,
  tool: string,
): Resolution<number> {
  if (ref.targetId !== undefined) return { ok: true, value: ref.targetId };
  if (ref.selector === undefined) {
    return { ok: false, error: `${tool} takes a targetId or selector, not a screen position.` };
  }
  const found = find(ref, state, staleNote, tool);
  return found.ok ? { ok: true, value: found.value.object.id } : found;
}

type WireAction = { method: string; params: unknown[] };

const moveTo = (point: Point): WireAction => ({ method: "move_mouse", params: [point.x, point.y] });
const press = (): WireAction => ({ method: "mouse_down", params: [0] });
const release = (): WireAction => ({ method: "mouse_up", params: [0] });

/// 왼쪽 버튼(0)만 쓴다 — 엔진이 `OnMouse*` 를 왼쪽에만 보낸다.
export const clickWireActions = (point: Point): WireAction[] => [moveTo(point), press(), release()];

/// 포인터를 옮기기만 한다. hover 핸들러는 `move_mouse` 가 옮길 때 게임의 입력 경로로 나간다.
export const hoverWireActions = (point: Point): WireAction[] => [moveTo(point)];

/// 원본에서 누르고, 목적지까지 활강한 뒤 놓는다. `move_mouse` 는 활강하므로 누른 채 옮기면 드래그가 된다.
export const dragWireActions = (from: Point, to: Point): WireAction[] =>
  [moveTo(from), press(), moveTo(to), release()];
