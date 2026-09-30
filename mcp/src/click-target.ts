import type { FoldedPulseState, PulseObject } from "./pulse.js";

/// `click` 이 받는 대상. 세 방식 중 정확히 하나를 담는다.
export interface ClickTarget {
  targetId?: number;
  selector?: string;
  x?: number;
  y?: number;
}

/// 게임 화면 위의 한 점. `move_mouse` 가 받는 것과 같은 좌상단 원점 픽셀이다.
export interface ClickPoint {
  x: number;
  y: number;
}

export type ClickResolution =
  | { ok: true; point: ClickPoint }
  | { ok: false; error: string };

/// 입력 조합이 잘못됐으면 그 이유를, 맞으면 `undefined` 를.
///
/// zod schema 의 `superRefine` 두 곳(tool, `perform_actions`)이 같은 규칙을 쓰도록 여기 한 자리에 둔다.
export function clickTargetIssue(target: ClickTarget): string | undefined {
  const byCoordinates = target.x !== undefined || target.y !== undefined;
  const given = [target.targetId !== undefined, target.selector !== undefined, byCoordinates]
    .filter(Boolean).length;
  if (given !== 1) {
    return "click requires exactly one of targetId, selector, or x and y.";
  }
  if (byCoordinates && (target.x === undefined || target.y === undefined)) {
    return "click requires both x and y when aiming at a screen position.";
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

function pointOf(object: PulseObject, active: boolean, scene: string): ClickResolution {
  const name = describe(object, scene);
  if (!active) {
    return { ok: false, error: `click: ${name} is not active in the scene.` };
  }
  if (flagOf(object, "onScreen") === false) {
    return { ok: false, error: `click: ${name} is off screen.` };
  }
  if (flagOf(object, "covered") === true) {
    return { ok: false, error: `click: ${name} is covered by something drawn on top of it.` };
  }
  const rect = rectOf(object);
  if (rect === undefined || rect.w <= 0 || rect.h <= 0) {
    return { ok: false, error: `click: ${name} has no on-screen area to aim at. Click it by x and y instead.` };
  }
  return { ok: true, point: { x: rect.x + rect.w / 2, y: rect.y + rect.h / 2 } };
}

/// 대상을 화면 점으로 푼다.
///
/// id 와 selector 는 마지막 reading 에서 풀고 그 rect 의 중심을 겨눈다. 좌표는 그대로 쓴다.
/// selector 는 `object.selector` 와 정확히 같은 것만 대상이다 — `get_scene_state` 의 contains 검색과
/// 달리, 누르는 일은 "아마 이것" 으로 하지 않는다. `staleNote` 는 reading 이 낡았을 때 그 까닭과 복구
/// 방법이고, 낡은 rect 는 이미 옮겨 간 자리를 가리킬 수 있어 id/selector 는 그 자리에서 거절한다.
export function resolveClickPoint(
  target: ClickTarget,
  state: FoldedPulseState | undefined,
  staleNote: string | undefined,
): ClickResolution {
  if (target.x !== undefined && target.y !== undefined) {
    return { ok: true, point: { x: target.x, y: target.y } };
  }

  if (state === undefined) {
    return { ok: false, error: "click: no scene reading has arrived yet. Call start_readings, or click by x and y." };
  }
  if (staleNote !== undefined) {
    return { ok: false, error: `click: the reading is stale, so its rects cannot be trusted. ${staleNote}` };
  }

  const pool = [
    ...state.active.map((object) => ({ object, active: true })),
    ...state.deactive.map((object) => ({ object, active: false })),
  ];

  if (target.targetId !== undefined) {
    const found = pool.find((entry) => entry.object.id === target.targetId);
    if (found === undefined) {
      return {
        ok: false,
        error: `click: no object with id ${target.targetId} in the last reading. Read the state again; it may have been destroyed.`,
      };
    }
    return pointOf(found.object, found.active, state.scene);
  }

  const matches = pool.filter((entry) => entry.object.selector === target.selector);
  if (matches.length === 0) {
    return {
      ok: false,
      error: `click: no object has the selector "${target.selector}". Use search_targets to find its exact selector.`,
    };
  }
  if (matches.length > 1) {
    const candidates = matches.map((entry) => describe(entry.object, state.scene)).join(", ");
    return { ok: false, error: `click: selector "${target.selector}" matches ${matches.length} objects: ${candidates}. Click by id.` };
  }
  return pointOf(matches[0]!.object, matches[0]!.active, state.scene);
}

/// 점을 누르는 세 action. 왼쪽 버튼(0)이다 — 엔진이 `OnMouse*` 를 왼쪽에만 보낸다.
export function clickWireActions(point: ClickPoint): Array<{ method: string; params: unknown[] }> {
  return [
    { method: "move_mouse", params: [point.x, point.y] },
    { method: "mouse_down", params: [0] },
    { method: "mouse_up", params: [0] },
  ];
}
